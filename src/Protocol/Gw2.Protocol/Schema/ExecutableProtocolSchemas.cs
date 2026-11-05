#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Gw2.Formats;

namespace Gw2.Protocol.Schema
{
    public sealed class ExecutableProtocolSchemas
    {
        private const int FieldDescriptorStride = 0x28;
        private const int FieldParamOffset = 0x10;
        private const int FieldReferenceOffset = 0x18;
        private const int MaxChainDepth = 16;
        private const int MaxChainLength = 4096;
        private const int MessageIdFieldType = 1;
        private const int ChainTerminatorFieldType = 0x18;
        private const int LoginChannel = 3;
        private const int RegistrationWindow = 0x40;
        private const int RuntimeParamLookback = 16;

        private const string DefinitionAssert = "src[i].defArray[0].fieldType == MP_MSGID";
        private const string DispatchAssert = "ptr->dispatch";
        private const string ProtocolLogFormat = "MsgConn Protocol: {} Id: {}";
        private const string ChannelReferenceAssert = "channel->GetRefCount() == 1";

        private static readonly byte[] StackReserve = { 0x48, 0x83, 0xEC };
        private static readonly byte[] LeaR9Rip = { 0x4C, 0x8D, 0x0D };
        private static readonly byte[] LeaRaxRip = { 0x48, 0x8D, 0x05 };
        private static readonly byte[] MoveR8dImmediate = { 0x41, 0xB8 };
        private static readonly byte[] MoveStack20Immediate = { 0xC7, 0x44, 0x24, 0x20 };
        private static readonly byte[] MoveEcxImmediate = { 0xB9 };
        private static readonly byte[] StoreEaxRip = { 0x89, 0x05 };
        private static readonly byte[] LoadEaxRip = { 0x8B, 0x05 };
        private static readonly byte[] LoadXmm0Rip = { 0xF3, 0x0F, 0x10, 0x05 };
        private const byte CallOpcode = 0xE8;

        private static ExecutableProtocolSchemas? _current;

        private enum RegistrarKind
        {
            SendAndKeyedReceive,
            KeyedReceive,
            SendAndReceive,
            Send,
        }

        private ExecutableProtocolSchemas(int build, IReadOnlyDictionary<int, FieldDefinition[]> gameRecv, IReadOnlyDictionary<int, FieldDefinition[]> gameSend,
            IReadOnlyDictionary<int, FieldDefinition[]> loginRecv, IReadOnlyDictionary<int, FieldDefinition[]> loginSend,
            IReadOnlyDictionary<int, ulong> gameReceiveHandlers, IReadOnlyDictionary<int, ulong> loginReceiveHandlers,
            IReadOnlyCollection<int> gameChannelSendIds)
        {
            Build = build;
            GameChannelSendIds = gameChannelSendIds;
            GameReceiveHandlers = gameReceiveHandlers;
            LoginReceiveHandlers = loginReceiveHandlers;
            GameRecvChains = gameRecv;
            GameSendChains = gameSend;
            LoginRecvChains = loginRecv;
            LoginSendChains = loginSend;
            Game = new ProtocolSchemaCorpus(ToSet(gameRecv), ToSet(gameSend), build);
            Login = new ProtocolSchemaCorpus(ToSet(loginRecv), ToSet(loginSend), build);
        }

        public static ExecutableProtocolSchemas Current => _current ??= Extract(ExecutableImage.Current);

        public int Build { get; }

        public ProtocolSchemaCorpus Game { get; }

        public ProtocolSchemaCorpus Login { get; }

        public IReadOnlyDictionary<int, ulong> GameReceiveHandlers { get; }

        public IReadOnlyDictionary<int, ulong> LoginReceiveHandlers { get; }

        public IReadOnlyCollection<int> GameChannelSendIds { get; }

        public IReadOnlyCollection<int> LoginSendIds => LoginSendChains.Keys.ToArray();

        internal IReadOnlyDictionary<int, FieldDefinition[]> GameRecvChains { get; }

        internal IReadOnlyDictionary<int, FieldDefinition[]> GameSendChains { get; }

        internal IReadOnlyDictionary<int, FieldDefinition[]> LoginRecvChains { get; }

        internal IReadOnlyDictionary<int, FieldDefinition[]> LoginSendChains { get; }

        public static ExecutableProtocolSchemas FromExecutable(string path) => Extract(ExecutableImage.Open(path));

        internal static ExecutableProtocolSchemas Extract(ExecutableImage executable)
        {
            var registrars = FindRegistrars(executable);
            var sites = executable.CallSites(registrars.Keys.ToArray());
            var gameRecv = new Dictionary<int, ulong>();
            var loginRecv = new Dictionary<int, ulong>();
            var gameSend = new Dictionary<int, ulong>();
            var loginSend = new Dictionary<int, ulong>();
            var gameReceiveHandlers = new Dictionary<int, ulong>();
            var loginReceiveHandlers = new Dictionary<int, ulong>();
            var gameChannelSend = new SortedSet<int>();
            foreach (var kind in new[] { RegistrarKind.SendAndKeyedReceive, RegistrarKind.KeyedReceive, RegistrarKind.SendAndReceive, RegistrarKind.Send })
            foreach (var registrar in registrars.Where(pair => pair.Value == kind).Select(pair => pair.Key))
            foreach (ulong call in sites[registrar])
            {
                var registration = ReadRegistration(executable, call);
                bool login = registration.Channel == LoginChannel;
                ulong sendTable = kind == RegistrarKind.KeyedReceive ? 0 : registration.TableA;
                uint sendCount = kind == RegistrarKind.KeyedReceive ? 0 : registration.CountA;
                ulong receiveTable = kind switch
                {
                    RegistrarKind.KeyedReceive => registration.TableA,
                    RegistrarKind.Send => 0,
                    _ => registration.TableB,
                };
                uint receiveCount = kind switch
                {
                    RegistrarKind.KeyedReceive => registration.CountA,
                    RegistrarKind.Send => 0,
                    _ => registration.CountB,
                };
                for (uint index = 0; index < sendCount; index++)
                    if (MessageDefinition(executable, executable.Qword(sendTable + 8 * index)) is var (id, definition))
                    {
                        gameSend.TryAdd(id, definition);
                        if (login) loginSend.TryAdd(id, definition);
                        else gameChannelSend.Add(id);
                    }
                for (uint index = 0; index < receiveCount; index++)
                    if (MessageDefinition(executable, executable.Qword(receiveTable + 16 * index)) is var (id, definition)
                        && (login ? loginRecv : gameRecv).TryAdd(id, definition))
                        (login ? loginReceiveHandlers : gameReceiveHandlers)[id] = executable.Qword(receiveTable + 16 * index + 8);
            }

            var runtimeParams = RuntimeParams(executable);
            return new ExecutableProtocolSchemas(ExecutableIdentity.Build(executable),
                ReadChains(executable, gameRecv, runtimeParams), ReadChains(executable, gameSend, runtimeParams),
                ReadChains(executable, loginRecv, runtimeParams), ReadChains(executable, loginSend, runtimeParams),
                gameReceiveHandlers, loginReceiveHandlers, gameChannelSend);
        }

        private static Dictionary<ulong, RegistrarKind> FindRegistrars(ExecutableImage executable)
        {
            var installers = FunctionsReferencing(executable, DefinitionAssert);
            var receiveInstallers = FunctionsReferencing(executable, DispatchAssert);
            var keyedInstallers = FunctionsReferencing(executable, ProtocolLogFormat);
            var channelLookups = FunctionsReferencing(executable, ChannelReferenceAssert);
            if (installers.Count == 0 || channelLookups.Count != 1)
                throw new InvalidOperationException("The executable has no message channel registration routines.");

            var sendInstallers = installers.Where(installer => !receiveInstallers.Contains(installer)).ToHashSet();
            var registrars = new Dictionary<ulong, RegistrarKind>();
            foreach (ulong caller in executable.CallSites(channelLookups.ToArray()).Values.Single())
            {
                if (executable.FunctionStart(caller) is not { } registrar || registrars.ContainsKey(registrar)) continue;
                var callees = executable.CallTargets(registrar).ToHashSet();
                bool sends = callees.Overlaps(sendInstallers);
                bool keyed = callees.Overlaps(keyedInstallers);
                bool receives = callees.Overlaps(receiveInstallers.Where(installer => installers.Contains(installer)));
                RegistrarKind? kind = (sends, receives, keyed) switch
                {
                    (true, true, true) => RegistrarKind.SendAndKeyedReceive,
                    (true, true, false) => RegistrarKind.SendAndReceive,
                    (false, true, true) => RegistrarKind.KeyedReceive,
                    (true, false, _) => RegistrarKind.Send,
                    _ => null,
                };
                if (kind is { } found) registrars[registrar] = found;
            }
            if (registrars.Count == 0) throw new InvalidOperationException("The executable has no message channel registrars.");
            return registrars;
        }

        private static HashSet<ulong> FunctionsReferencing(ExecutableImage executable, string text)
        {
            var strings = executable.FindAscii(".rdata", text).ToHashSet();
            var functions = new HashSet<ulong>();
            foreach (ulong reference in executable.LeaReferences(strings))
                if (executable.FunctionStart(reference) is { } function) functions.Add(function);
            return functions;
        }

        private static (int Channel, ulong TableA, uint CountA, ulong TableB, uint CountB) ReadRegistration(ExecutableImage executable, ulong call)
        {
            ulong windowStart = call - RegistrationWindow;
            var window = executable.Bytes(windowStart, RegistrationWindow).ToArray();
            int prologue = LastIndexOf(window, StackReserve, window.Length);
            if (prologue < 0) throw new InvalidOperationException($"No registration wrapper before 0x{call:X}.");
            var wrapper = window.AsSpan(prologue).ToArray();
            ulong wrapperStart = windowStart + (ulong)prologue;
            return (Channel(wrapper), RipTarget(wrapper, wrapperStart, LeaR9Rip), Immediate(wrapper, MoveR8dImmediate),
                RipTarget(wrapper, wrapperStart, LeaRaxRip), Immediate(wrapper, MoveStack20Immediate));
        }

        private static int Channel(byte[] wrapper)
        {
            int move = LastIndexOf(wrapper, MoveEcxImmediate, wrapper.Length - 4);
            return move < 0 ? 0 : BitConverter.ToInt32(wrapper, move + 1);
        }

        private static ulong RipTarget(byte[] wrapper, ulong wrapperStart, byte[] opcode)
        {
            int at = LastIndexOf(wrapper, opcode, wrapper.Length);
            return at < 0 ? 0 : wrapperStart + (ulong)at + 7 + (ulong)(long)BitConverter.ToInt32(wrapper, at + 3);
        }

        private static uint Immediate(byte[] wrapper, byte[] opcode)
        {
            int at = LastIndexOf(wrapper, opcode, wrapper.Length);
            return at < 0 ? 0 : BitConverter.ToUInt32(wrapper, at + opcode.Length);
        }

        private static int LastIndexOf(byte[] bytes, byte[] pattern, int limit)
        {
            for (int index = Math.Min(limit, bytes.Length) - pattern.Length; index >= 0; index--)
                if (bytes.AsSpan(index, pattern.Length).SequenceEqual(pattern)) return index;
            return -1;
        }

        private static (int Id, ulong Definition)? MessageDefinition(ExecutableImage executable, ulong definition)
        {
            if (definition == 0 || !executable.IsMapped(definition) || executable.InCode(definition)) return null;
            if (executable.Dword(definition) != MessageIdFieldType) return null;
            return ((int)executable.Dword(definition + FieldParamOffset), definition);
        }

        private static Dictionary<ulong, int> RuntimeParams(ExecutableImage executable)
        {
            var (codeStart, _) = executable.Section(".text");
            var code = executable.SectionBytes(".text").ToArray();
            var stores = new Dictionary<ulong, List<int>>();
            for (int at = IndexOf(code, StoreEaxRip, 0); at >= 0; at = IndexOf(code, StoreEaxRip, at + 1))
            {
                if (at + 6 > code.Length) break;
                ulong target = RipTarget(code, codeStart, at, 2, 6);
                if (!stores.TryGetValue(target, out var list)) stores[target] = list = new List<int>();
                list.Add(at);
            }

            var runtimeParams = new Dictionary<ulong, int>();
            foreach (var (target, sites) in stores)
            foreach (int site in sites)
            {
                int windowStart = Math.Max(0, site - RuntimeParamLookback);
                int load = LastIndexOf(code.AsSpan(windowStart, site - windowStart).ToArray(), LoadEaxRip, site - windowStart);
                if (load < 0) continue;
                ulong source = RipTarget(code, codeStart, windowStart + load, 2, 6);
                if (!stores.TryGetValue(source, out var sourceStores)) continue;
                foreach (int sourceStore in sourceStores)
                    if (CeiledFloatGlobal(executable, code, codeStart, sourceStore) is { } value)
                        runtimeParams[target] = value;
            }
            return runtimeParams;
        }

        private static int? CeiledFloatGlobal(ExecutableImage executable, byte[] code, ulong codeStart, int store)
        {
            int call = store - 5;
            int load = call - 8;
            if (load < 0 || code[call] != CallOpcode || !code.AsSpan(load, 4).SequenceEqual(LoadXmm0Rip)) return null;
            return (int)Math.Ceiling(executable.Single(RipTarget(code, codeStart, load, 4, 8)));
        }

        private static ulong RipTarget(byte[] code, ulong codeStart, int at, int opcodeLength, int instructionLength) =>
            codeStart + (ulong)at + (ulong)instructionLength + (ulong)(long)BitConverter.ToInt32(code, at + opcodeLength);

        private static int IndexOf(byte[] bytes, byte[] pattern, int start)
        {
            int found = bytes.AsSpan(start).IndexOf(pattern);
            return found < 0 ? -1 : start + found;
        }

        private static IReadOnlyDictionary<int, FieldDefinition[]> ReadChains(ExecutableImage executable, Dictionary<int, ulong> definitions, Dictionary<ulong, int> runtimeParams)
        {
            var chains = new SortedDictionary<int, FieldDefinition[]>();
            foreach (var (id, definition) in definitions)
                if (ReadChain(executable, definition, 0, new HashSet<ulong>(), runtimeParams) is { Length: > 0 } chain)
                    chains[id] = chain;
            return chains;
        }

        private static FieldDefinition[]? ReadChain(ExecutableImage executable, ulong address, int depth, HashSet<ulong> active, Dictionary<ulong, int> runtimeParams)
        {
            var fields = new List<FieldDefinition>();
            for (int index = 0; index < MaxChainLength; index++, address += FieldDescriptorStride)
            {
                if (!executable.IsMapped(address) || !executable.IsMapped(address + FieldReferenceOffset + 7)) return null;
                int fieldType = (int)executable.Dword(address);
                if (fieldType == 0 || fieldType == ChainTerminatorFieldType) break;
                int param = runtimeParams.TryGetValue(address + FieldParamOffset, out int runtime) ? runtime : (int)executable.Dword(address + FieldParamOffset);
                ulong reference = executable.Qword(address + FieldReferenceOffset);
                FieldDefinition[]? nested = null;
                if (reference != 0 && depth < MaxChainDepth && active.Add(reference))
                {
                    nested = ReadChain(executable, reference, depth + 1, active, runtimeParams);
                    active.Remove(reference);
                    if (nested is { Length: 0 }) nested = null;
                }
                fields.Add(new FieldDefinition(fieldType, param, nested));
            }
            return fields.ToArray();
        }

        private static MessageSchemaSet ToSet(IReadOnlyDictionary<int, FieldDefinition[]> chains) =>
            new MessageSchemaSet(chains.Values.Select(MessageSchema.FromChain));
    }
}
