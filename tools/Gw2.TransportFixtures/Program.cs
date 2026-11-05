using Gw2.Formats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Gw2.Protocol;
using Gw2.Protocol.Framing;
using Gw2.Protocol.MsgPack;
using Gw2.Protocol.Schema;
using Gw2.Protocol.Session;
using Gw2.Protocol.Transport;

namespace Gw2.TransportFixtures
{
    public static class Program
    {
        private const int FrameHeaderLength = 4;
        private const int UnencryptedOutboundPackets = 2;
        private const int MaximumInboundFixtureLength = 512;
        private const string NoServerSupportClaim = "Not a claim of server support.";
        private static readonly JsonSerializerOptions Indented = new JsonSerializerOptions { WriteIndented = true };

        public static int Main(string[] arguments)
        {
            if (arguments.Length < 2)
            {
                Console.Error.WriteLine("usage: Gw2.TransportFixtures <live-transport.json> <output directory> [--inbound-ids 404,23] [--frame-ids 424] [--outbound-ids 1a4] [--list] [--describe-ids 1ac]");
                return 1;
            }
            using JsonDocument recording = JsonDocument.Parse(File.ReadAllText(arguments[0]));
            JsonElement root = recording.RootElement;
            string outputDirectory = arguments[1];
            int build = root.GetProperty("build").GetInt32();
            string? executable = Gw2Installation.ExecutablePath();
            if (executable == null)
            {
                Console.Error.WriteLine("Install the Guild Wars 2 build the recording was made with.");
                return 1;
            }
            ProtocolSchemaCorpus corpus = ExecutableProtocolSchemas.FromExecutable(executable).Game;
            bool listsOnly = arguments.Contains("--list");
            int[] inboundIds = Ids(arguments, "--inbound-ids");
            int[] frameIds = Ids(arguments, "--frame-ids");
            int[] outboundIds = Ids(arguments, "--outbound-ids");
            int[] describedIds = Ids(arguments, "--describe-ids");

            byte[] seed = Hex(root, "handshakeSeedHex");
            byte[] serverPublicValue = Hex(root, "serverPublicValueHex");
            byte[] serverKeyPacket = Hex(root, "serverKeyFrameHex");
            byte[] serverKeyFrame = serverKeyPacket.AsSpan(0, serverKeyPacket[1]).ToArray();
            byte[] recordedTransportKey = Hex(root, "transportKeyHex");
            HandshakeGroup group = HandshakeGroup.FromExecutable(executable);
            HandshakeKeyPair keys = serverPublicValue.Length == 0
                ? HandshakeKeyExchange.Derive(seed, group)
                : HandshakeKeyExchange.Derive(seed, group, serverPublicValue);
            if (!HandshakeFrame.TryDecodeServerKey(serverKeyFrame, out byte[]? serverValue))
                throw new InvalidDataException("the recorded server key frame does not decode");
            byte[] transportKey = HandshakeFrame.DeriveTransportKey(serverValue, keys.SharedSecretPrefix());
            if (!transportKey.SequenceEqual(recordedTransportKey))
                throw new InvalidDataException("the derived transport key differs from the recorded one");
            var afterKeySchedule = new TransportCipherState();
            TransportCipher.KeySchedule(transportKey, afterKeySchedule);

            List<(bool Inbound, byte[] Bytes)> packets = root.GetProperty("packets").EnumerateArray()
                .Select(packet => (packet.GetProperty("inbound").GetBoolean(), Convert.FromHexString(packet.GetProperty("hex").GetString()!)))
                .ToList();
            List<byte[]> inboundChunks = InboundAfterServerKey(packets, serverKeyFrame.Length);
            List<byte[]> outboundChunks = packets.Where(packet => !packet.Inbound).Skip(UnencryptedOutboundPackets).Select(packet => packet.Bytes).ToList();

            if (describedIds.Length > 0)
            {
                DescribeInbound(corpus, afterKeySchedule, inboundChunks, describedIds);
                return 0;
            }
            if (listsOnly)
            {
                ListInbound(corpus, afterKeySchedule, inboundChunks);
                ListOutbound(corpus, afterKeySchedule, outboundChunks);
                return 0;
            }

            Directory.CreateDirectory(outputDirectory);
            var common = new Dictionary<string, object>
            {
                ["build"] = build,
                ["buildLabel"] = build.ToString(),
                ["imageSha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executable))),
                ["kind"] = "captured",
                ["source"] = $"live {build} game connection, recorded by Gw2.LiveLogin --record-transport",
            };
            Write(outputDirectory, "handshake-live.json", With(common, new Dictionary<string, object>
            {
                ["provenance"] = "One live game-server handshake: the seed, the client's derived prefix, the server key frame from the wire, and the transport key the session keyed its cipher with.",
                ["noClaim"] = "One handshake. Confirms the handshake derivation and cipher chain against a live server. " + NoServerSupportClaim,
                ["seed"] = HexText(seed),
                ["outA20"] = HexText(keys.SharedSecretPrefix()),
                ["serverKeyFrameHex"] = HexText(serverKeyFrame),
                ["serverValueHex"] = HexText(serverValue),
                ["transportKeyHex"] = HexText(transportKey),
                ["sboxAfterKsaHex"] = HexText(afterKeySchedule.CopySBox()),
            }));
            TransportCipherState afterFirstInbound = Copy(afterKeySchedule);
            TransportCipher.Crypt(afterFirstInbound, inboundChunks[0]);
            Write(outputDirectory, "keystream-reuse.json", With(common, new Dictionary<string, object>
            {
                ["provenance"] = "Both directions start from the state after the key schedule. The inbound state is the one before the second encrypted inbound packet, replayed over the first; the second packet decodes from it.",
                ["noClaim"] = "State-advance evidence for the cipher only; not a wire decode.",
                ["outboundState"] = StateJson(afterKeySchedule),
                ["inboundState"] = StateJson(afterFirstInbound),
                ["outboundToInboundAdvance"] = inboundChunks[0].Length,
            }));
            WriteInbound(outputDirectory, common, corpus, afterKeySchedule, inboundChunks, inboundIds, frameIds);
            WriteOutbound(outputDirectory, common, corpus, afterKeySchedule, outboundChunks, outboundIds);
            return 0;
        }

        private static List<byte[]> InboundAfterServerKey(List<(bool Inbound, byte[] Bytes)> packets, int serverKeyFrameLength)
        {
            var chunks = new List<byte[]>();
            int handshakeBytesLeft = serverKeyFrameLength;
            foreach ((bool inbound, byte[] bytes) in packets.Where(packet => packet.Inbound))
            {
                int skipped = Math.Min(handshakeBytesLeft, bytes.Length);
                handshakeBytesLeft -= skipped;
                if (skipped < bytes.Length) chunks.Add(bytes.AsSpan(skipped).ToArray());
            }
            return chunks;
        }

        private static List<(int Start, int Length, TransportCipherState Before)> ChunkStates(TransportCipherState afterKeySchedule, List<byte[]> chunks)
        {
            TransportCipherState state = Copy(afterKeySchedule);
            var states = new List<(int, int, TransportCipherState)>();
            int start = 0;
            foreach (byte[] chunk in chunks)
            {
                states.Add((start, chunk.Length, Copy(state)));
                TransportCipher.Crypt(state, chunk);
                start += chunk.Length;
            }
            return states;
        }

        private static (byte[] Transport, SortedSet<int> FrameStarts, List<(int Offset, int Length)> Frames) InboundFrames(TransportCipherState afterKeySchedule, List<byte[]> chunks)
        {
            byte[] transport = TransportCipher.Crypt(Copy(afterKeySchedule), chunks.SelectMany(chunk => chunk).ToArray());
            var frameStarts = new SortedSet<int>();
            var frames = new List<(int, int)>();
            int offset = 0;
            while (offset + FrameHeaderLength <= transport.Length)
            {
                int compressedLength = transport[offset] | (transport[offset + 1] << 8);
                int decodedLength = transport[offset + 2] | (transport[offset + 3] << 8);
                int length = FrameHeaderLength + (compressedLength != 0 ? compressedLength : decodedLength);
                if (offset + length > transport.Length) break;
                frameStarts.Add(offset);
                frames.Add((offset, length));
                offset += length;
            }
            frameStarts.Add(offset);
            return (transport, frameStarts, frames);
        }

        private static void ListInbound(ProtocolSchemaCorpus corpus, TransportCipherState afterKeySchedule, List<byte[]> chunks)
        {
            (byte[] transport, SortedSet<int> frameStarts, _) = InboundFrames(afterKeySchedule, chunks);
            foreach ((int start, int length, _) in ChunkStates(afterKeySchedule, chunks))
            {
                bool aligned = frameStarts.Contains(start) && frameStarts.Contains(start + length);
                string ids = aligned ? MessageIds(DecodeFrames(corpus, transport.AsSpan(start, length).ToArray())) : "(splits a frame)";
                Console.WriteLine($"in  {start,8} {length,6} {ids}");
            }
        }

        private static void DescribeInbound(ProtocolSchemaCorpus corpus, TransportCipherState afterKeySchedule, List<byte[]> chunks, int[] describedIds)
        {
            (byte[] transport, SortedSet<int> frameStarts, _) = InboundFrames(afterKeySchedule, chunks);
            var stream = new List<byte>();
            int offset = 0;
            while (offset < frameStarts.Max)
            {
                stream.AddRange(TransportFrame.Deframe(transport.AsSpan(offset), out int consumed));
                offset += consumed;
            }
            byte[] messageStream = stream.ToArray();
            foreach (DecodedMessage message in MessageStreamDecoder.TryDecode(corpus.Recv, messageStream, out _))
                if (describedIds.Contains(message.MessageId))
                    Console.WriteLine($"0x{message.MessageId:x} {string.Join(", ", message.Fields.Select(FieldShape))} {Convert.ToHexString(messageStream, message.WireOffset, message.WireLength)}");
        }

        private static string FieldShape(DecodedField field) => field.Value switch
        {
            null => field.Present ? "null" : "absent",
            byte[] bytes => $"bytes[{bytes.Length}]",
            string text => $"string[{text.Length}]",
            DecodedField[] nested => "{" + string.Join(", ", nested.Select(FieldShape)) + "}",
            object value => value.GetType().Name,
        };

        private static void ListOutbound(ProtocolSchemaCorpus corpus, TransportCipherState afterKeySchedule, List<byte[]> chunks)
        {
            TransportCipherState state = Copy(afterKeySchedule);
            foreach (byte[] chunk in chunks)
            {
                byte[] plaintext = TransportCipher.Crypt(state, chunk);
                IReadOnlyList<DecodedMessage> messages = MessageStreamDecoder.TryDecode(corpus.ForDirection(TrafficDirection.ClientToServer), plaintext, out int consumed);
                Console.WriteLine($"out {chunk.Length,6} {MessageIds(messages)}{(consumed == plaintext.Length ? "" : " (partial)")}");
            }
        }

        private static void WriteInbound(string outputDirectory, Dictionary<string, object> common, ProtocolSchemaCorpus corpus,
                                         TransportCipherState afterKeySchedule, List<byte[]> chunks, int[] inboundIds, int[] frameIds)
        {
            (byte[] transport, SortedSet<int> frameStarts, List<(int Offset, int Length)> frames) = InboundFrames(afterKeySchedule, chunks);
            if (inboundIds.Length > 0)
            {
                bool written = false;
                foreach ((int start, int length, TransportCipherState before) in ChunkStates(afterKeySchedule, chunks))
                {
                    if (length > MaximumInboundFixtureLength || !frameStarts.Contains(start) || !frameStarts.Contains(start + length)) continue;
                    byte[] frame = transport.AsSpan(start, length).ToArray();
                    IReadOnlyList<DecodedMessage> messages = DecodeFrames(corpus, frame);
                    if (messages.Count < 2 || !messages.All(message => inboundIds.Contains(message.MessageId))) continue;
                    Write(outputDirectory, "captured-inbound.json", With(common, new Dictionary<string, object>
                    {
                        ["direction"] = "ServerToClient",
                        ["representation"] = "wire-bytes",
                        ["provenance"] = "The state is the inbound cipher state before this packet, replayed from the transport key over every earlier inbound byte.",
                        ["noClaim"] = "Captured wire bytes for one packet. " + NoServerSupportClaim,
                        ["cipherState"] = StateJson(before),
                        ["wireHex"] = HexText(chunks.Skip(ChunkIndex(chunks, start)).First()),
                        ["transportFrameHex"] = HexText(frame),
                        ["messages"] = messages.Select(message => $"0x{message.MessageId:x}").ToArray(),
                    }));
                    written = true;
                    break;
                }
                if (!written) throw new InvalidDataException("no whole inbound packet carries only the allowed ids");
            }

            foreach (int frameId in frameIds)
            {
                (int Offset, int Length) match = frames.FirstOrDefault(frame =>
                {
                    IReadOnlyList<DecodedMessage> decoded = DecodeFrames(corpus, transport.AsSpan(frame.Offset, frame.Length).ToArray());
                    return decoded.Count == 1 && decoded[0].MessageId == frameId;
                });
                if (match.Length == 0) throw new InvalidDataException($"no inbound frame carries 0x{frameId:x} alone");
                Write(outputDirectory, $"captured-inbound-0x{frameId:x}.json", With(common, new Dictionary<string, object>
                {
                    ["direction"] = "ServerToClient",
                    ["messageId"] = $"0x{frameId:x}",
                    ["representation"] = "transport-frame",
                    ["provenance"] = "Decrypted with the session's transport key; the first frame that carries this message alone.",
                    ["noClaim"] = "Decrypted transport frame for one inbound message. " + NoServerSupportClaim,
                    ["transportFrameHex"] = HexText(transport.AsSpan(match.Offset, match.Length)),
                    ["messages"] = new[] { $"0x{frameId:x}" },
                }));
            }
        }

        private static int ChunkIndex(List<byte[]> chunks, int start)
        {
            int offset = 0;
            for (int index = 0; index < chunks.Count; index++)
            {
                if (offset == start) return index;
                offset += chunks[index].Length;
            }
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        private static void WriteOutbound(string outputDirectory, Dictionary<string, object> common, ProtocolSchemaCorpus corpus,
                                          TransportCipherState afterKeySchedule, List<byte[]> chunks, int[] outboundIds)
        {
            TransportCipherState state = Copy(afterKeySchedule);
            var written = new HashSet<int>();
            foreach (byte[] chunk in chunks)
            {
                TransportCipherState before = Copy(state);
                byte[] plaintext = TransportCipher.Crypt(state, chunk);
                IReadOnlyList<DecodedMessage> messages = MessageStreamDecoder.TryDecode(corpus.ForDirection(TrafficDirection.ClientToServer), plaintext, out int consumed);
                if (consumed != plaintext.Length || messages.Count != 1) continue;
                int messageId = messages[0].MessageId;
                if (!outboundIds.Contains(messageId) || !written.Add(messageId)) continue;
                Write(outputDirectory, $"captured-outbound-0x{messageId:x}.json", With(common, new Dictionary<string, object>
                {
                    ["direction"] = "ClientToServer",
                    ["messageId"] = $"0x{messageId:x}",
                    ["representation"] = "wire-bytes",
                    ["provenance"] = "The state is the outbound cipher state before this flush, replayed from the transport key over every earlier outbound byte.",
                    ["noClaim"] = "Captured wire bytes for one flush. " + NoServerSupportClaim,
                    ["cipherState"] = StateJson(before),
                    ["plaintextHex"] = HexText(plaintext),
                    ["wireHex"] = HexText(chunk),
                }));
            }
            foreach (int missing in outboundIds.Where(id => !written.Contains(id)))
                throw new InvalidDataException($"no outbound flush carries 0x{missing:x} alone");
        }

        private static IReadOnlyList<DecodedMessage> DecodeFrames(ProtocolSchemaCorpus corpus, byte[] frames)
        {
            var stream = new List<byte>();
            int offset = 0;
            while (offset < frames.Length)
            {
                stream.AddRange(TransportFrame.Deframe(frames.AsSpan(offset), out int consumed));
                offset += consumed;
            }
            byte[] buffer = stream.ToArray();
            try
            {
                IReadOnlyList<DecodedMessage> messages = MessageStreamDecoder.TryDecode(corpus.Recv, buffer, out int decoded);
                return decoded == buffer.Length ? messages : Array.Empty<DecodedMessage>();
            }
            catch (FormatException)
            {
                return Array.Empty<DecodedMessage>();
            }
        }

        private static string MessageIds(IReadOnlyList<DecodedMessage> messages) =>
            messages.Count == 0 ? "(undecoded)" : string.Join(",", messages.Select(message => $"0x{message.MessageId:x}"));

        private static TransportCipherState Copy(TransportCipherState state) =>
            TransportCipherState.FromCapturedState(state.I, state.J, state.CopySBox());

        private static Dictionary<string, string> StateJson(TransportCipherState state) => new Dictionary<string, string>
        {
            ["i"] = state.I.ToString("x"),
            ["j"] = state.J.ToString("x"),
            ["sboxHex"] = HexText(state.CopySBox()),
        };

        private static Dictionary<string, object> With(Dictionary<string, object> common, Dictionary<string, object> fields)
        {
            var merged = new Dictionary<string, object>(common);
            foreach (KeyValuePair<string, object> field in fields) merged[field.Key] = field.Value;
            return merged;
        }

        private static void Write(string outputDirectory, string name, Dictionary<string, object> fixture)
        {
            string path = Path.Combine(outputDirectory, name);
            File.WriteAllText(path, JsonSerializer.Serialize(fixture, Indented).ReplaceLineEndings("\n") + "\n");
            Console.WriteLine($"wrote {path}");
        }

        private static int[] Ids(string[] arguments, string option)
        {
            int index = Array.IndexOf(arguments, option);
            return index < 0 || index + 1 >= arguments.Length
                ? Array.Empty<int>()
                : arguments[index + 1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(text => Convert.ToInt32(text, 16)).ToArray();
        }

        private static byte[] Hex(JsonElement root, string property) => Convert.FromHexString(root.GetProperty(property).GetString()!);

        private static string HexText(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
