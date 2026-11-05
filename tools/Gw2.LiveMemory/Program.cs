using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Gw2.Exe.Live;
using Gw2.Exe.Objects;

namespace Gw2.LiveMemory
{
    internal static unsafe class Program
    {
        private const string DefaultProcessName = "Gw2-64";

        private static int Main(string[] arguments)
        {
            string processName = Option(arguments, "--process") ?? DefaultProcessName;
            using LiveProcess process = LiveProcess.OpenByName(processName);
            if (arguments.Contains("--connection")) return DumpConnection(process, Option(arguments, "--save"));
            if (arguments.Contains("--character-select")) return DumpCharacterSelect(process);
            if (arguments.Contains("--ui-materials")) return UiMaterialProbe.Run(process);
            if (arguments.Contains("--ui-pack-states")) return UiPackStateProbe.Run(process);
            if (arguments.Contains("--character-composites")) return CharacterCompositeProbe.Run(process);
            if (arguments.Contains("--compass-edge")) return CompassEdgeProbe.Run(process);
            if (arguments.Contains("--bgfx-caps")) return BgfxCapsProbe.Run(process, Option(arguments, "--save"));
            LivePropContext live = LivePropContext.Find(process);
            Console.WriteLine($"build {process.Addresses.Build}, image 0x{process.ImageBase:X}, PropContext 0x{live.Address:X} on thread {live.ThreadId}");

            ChCliContext characters = live.Read<ChCliContext>(live.Context.ChCliContext);
            Console.WriteLine($"ChCliContext 0x{(ulong)live.Context.ChCliContext:X}: {characters.Characters.Count} character slots, {characters.Players.Count} player slots");
            if (characters.ControlledCharacter == null)
            {
                Console.WriteLine("no controlled character; the client is not in a map");
                return 0;
            }

            ulong coCharTable = process.ImageBase + process.Addresses.CoCharVirtualTableRva;
            Console.WriteLine(ControlledCharacterSnapshot.TryRead(process, live.Address, coCharTable)?.ToString() ?? "no snapshot");
            ChCliCharacter character = live.Read<ChCliCharacter>(characters.ControlledCharacter);
            Agent agent = live.Read<Agent>(character.Agent);
            Console.WriteLine($"controlled character 0x{(ulong)characters.ControlledCharacter:X}, agent {agent.Id}");
            Dump("Agent", agent);
            Dump("ChCliCharacter", character);
            if (character.Health != null) Dump("ChCliHealth", live.Read<ChCliHealth>(character.Health));
            if (character.CoreStats != null) Dump("ChCliCoreStats", live.Read<ChCliCoreStats>(character.CoreStats));
            return 0;
        }

        private static int DumpCharacterSelect(LiveProcess process)
        {
            CharacterSelectScreen? screen = CharacterSelectScreen.TryRead(process,
                process.ImageBase + process.Addresses.FrameIdManagerRva,
                process.ImageBase + process.Addresses.CharacterArrayRva);
            if (screen == null)
            {
                Console.WriteLine("character select is not the active view");
                return 1;
            }

            Console.WriteLine($"scene frame {screen.Scene.Id} {screen.Scene.ScreenRectangle}");
            UiFrameTree tree = UiFrameTree.TryRead(process, process.ImageBase + process.Addresses.FrameIdManagerRva)!;
            DumpFrameChildren(tree, screen.Scene, 1);
            foreach (string name in screen.CharacterNames) Console.WriteLine($"  character {name}");
            return 0;
        }

        private static void DumpFrameChildren(UiFrameTree tree, UiFrame parent, int depth)
        {
            foreach (UiFrame child in tree.Children(parent))
            {
                UiFrameRectangle r = child.ScreenRectangle;
                Console.WriteLine($"{new string(' ', depth * 2)}child {child.ChildIndex,2} frame {child.Id,4} ({r.Left:0.#}, {r.Top:0.#}, {r.Right:0.#}, {r.Bottom:0.#}) flags 0x{child.CreationFlags:X}");
                DumpFrameChildren(tree, child, depth + 1);
            }
        }

        private static int DumpConnection(LiveProcess process, string? savePath)
        {
            LiveGameConnection? connection = LiveGameConnection.Find(process);
            if (connection == null)
            {
                Console.WriteLine("no game connection");
                return 1;
            }

            Console.WriteLine($"game MsgConn 0x{connection.Address:X}, mode {connection.Mode}");
            Console.WriteLine($"  inbound i {connection.Inbound.I} j {connection.Inbound.J}, outbound i {connection.Outbound.I} j {connection.Outbound.J}");
            if (savePath == null) return 0;
            if (!connection.IsEncrypted)
            {
                Console.Error.WriteLine("the connection is not keyed yet; save after the handshake");
                return 1;
            }

            LivePropContext live = LivePropContext.Find(process);
            ChCliContext characters = live.Read<ChCliContext>(live.Context.ChCliContext);
            object? controlled = null;
            if (characters.ControlledCharacter != null)
            {
                ChCliCharacter character = live.Read<ChCliCharacter>(characters.ControlledCharacter);
                controlled = new
                {
                    agentId = live.Read<Agent>(character.Agent).Id,
                    health = character.Health == null ? null : Convert.ToHexString(AsBytes(live.Read<ChCliHealth>(character.Health))),
                    coreStats = character.CoreStats == null ? null : Convert.ToHexString(AsBytes(live.Read<ChCliCoreStats>(character.CoreStats))),
                };
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(savePath))!);
            File.WriteAllText(savePath, JsonSerializer.Serialize(new
            {
                build = process.Addresses.Build,
                readAtUtc = DateTime.UtcNow,
                processId = process.Process.Id,
                clientKeyHalf = Convert.ToHexString(connection.ClientKeyHalf),
                inbound = new { i = connection.Inbound.I, j = connection.Inbound.J, permutation = Convert.ToHexString(connection.Inbound.Permutation) },
                outbound = new { i = connection.Outbound.I, j = connection.Outbound.J, permutation = Convert.ToHexString(connection.Outbound.Permutation) },
                controlled,
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"saved the client key half to {savePath}");
            return 0;
        }

        private static byte[] AsBytes<T>(T value) where T : unmanaged => new ReadOnlySpan<byte>(&value, sizeof(T)).ToArray();

        private static void Dump<T>(string name, in T live) where T : unmanaged
        {
            Console.WriteLine(name);
            foreach (LayoutDifference field in LayoutDiff.Compare(default(T), live))
                Console.WriteLine(FormattableString.Invariant($"  +0x{field.Offset:X3} {field.Field} = {field.Live}"));
        }

        private static string? Option(string[] arguments, string name)
        {
            int index = Array.IndexOf(arguments, name);
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
        }
    }
}
