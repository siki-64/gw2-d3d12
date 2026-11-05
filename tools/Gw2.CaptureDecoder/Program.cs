using Gw2.Formats;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Gw2.Protocol.Capture;
using Gw2.Protocol.MsgPack;
using Gw2.Protocol.Schema;

namespace Gw2.CaptureDecoder
{
    public static class Program
    {
        private const uint LoginProtocol = 3;
        private const int BlobPreviewBytes = 32;
        private const int DefaultListPreviewItems = 4;
        private static int _listPreviewItems = DefaultListPreviewItems;
        private static bool _printsFullBlobs;

        private static readonly string[] OptionsWithValues = { "--exe", "--list-items", "--keys", "--receive-handlers", "--send-ids" };

        public static int Main(string[] args)
        {
            var inputs = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (Array.IndexOf(OptionsWithValues, args[i]) >= 0)
                {
                    i++;
                    continue;
                }
                if (args[i] == "--bytes" || args[i] == "--full-blobs") continue;
                inputs.Add(args[i]);
            }
            string? receiveHandlersOutput = StringOption(args, "--receive-handlers", null);
            string? sendIdsOutput = StringOption(args, "--send-ids", null);
            if (inputs.Count == 0 && receiveHandlersOutput == null && sendIdsOutput == null)
            {
                Console.Error.WriteLine(
                    "usage: Gw2.CaptureDecoder <capture.gw2cap>... [--exe <Gw2-64.exe>] [--bytes] [--keys <out.json>] [--receive-handlers <out.csv>] [--send-ids <out.csv>]");
                return 1;
            }
            string? executable = StringOption(args, "--exe", Gw2Installation.ExecutablePath());
            if (executable == null)
            {
                Console.Error.WriteLine("Pass --exe or install Guild Wars 2.");
                return 1;
            }
            ExecutableProtocolSchemas schemas = ExecutableProtocolSchemas.FromExecutable(executable);
            if (receiveHandlersOutput != null) WriteReceiveHandlers(schemas, receiveHandlersOutput);
            if (sendIdsOutput != null) WriteSendIds(schemas, sendIdsOutput);
            if ((receiveHandlersOutput != null || sendIdsOutput != null) && inputs.Count == 0) return 0;
            string? keysOutput = StringOption(args, "--keys", null);
            bool printsWireBytes = args.Contains("--bytes");
            _printsFullBlobs = args.Contains("--full-blobs");
            bool extractsKeys = keysOutput != null;
            _listPreviewItems = int.Parse(StringOption(args, "--list-items", DefaultListPreviewItems.ToString()));

            ProtocolSchemaCorpus loginCorpus = schemas.Login;
            ProtocolSchemaCorpus gameCorpus = schemas.Game;
            var extractor = extractsKeys ? new KeyExtraction() : null;
            var streams = new Dictionary<(ulong, uint, CaptureRecordKind), CapturedStream>();
            long? firstTimestamp = null;

            foreach (string input in inputs)
            {
                streams.Clear();
                firstTimestamp = null;
                var capture = NetworkCapture.Read(input);
                foreach (CapturedChunk record in capture.Chunks.Where(chunk => chunk.Kind == CaptureRecordKind.Outbound || chunk.Kind == CaptureRecordKind.Inbound))
                {
                    firstTimestamp ??= record.Timestamp;
                    var key = (record.Connection, record.Protocol, record.Kind);
                    bool isClientToServer = record.Kind == CaptureRecordKind.Outbound;
                    if (!streams.TryGetValue(key, out CapturedStream? stream))
                    {
                        ProtocolSchemaCorpus corpus = record.Protocol == LoginProtocol ? loginCorpus : gameCorpus;
                        stream = new CapturedStream(isClientToServer ? corpus.Send : corpus.Recv, !isClientToServer);
                        streams[key] = stream;
                    }

                    bool wasFaulted = stream.Fault != null;
                    double seconds = (double)(record.Timestamp - firstTimestamp.Value) / capture.TimestampFrequency;
                    string prefix = $"{seconds,8:F3} {(record.Protocol == LoginProtocol ? "login" : "game ")} {(isClientToServer ? "C>S" : "S>C")}";
                    foreach (CapturedMessage message in stream.Append(record.Plaintext))
                    {
                        if (extractor != null && !isClientToServer) extractor.Add(message.Decoded);
                        if (extractsKeys) continue;
                        Console.WriteLine($"{prefix} 0x{message.Decoded.MessageId:x4} {message.Decoded.WireLength,5}B {FormatFields(message.Decoded.Fields.Skip(1))}");
                        if (printsWireBytes) Console.WriteLine($"         {Convert.ToHexString(message.WireBytes)}");
                    }
                    if (!wasFaulted && stream.Fault != null && !extractsKeys) Console.WriteLine($"{prefix} decode stopped: {stream.Fault}");
                }

                if (extractsKeys) continue;
                foreach (var entry in streams.Where(entry => entry.Value.PendingBytes > 0))
                    Console.WriteLine($"protocol {entry.Key.Item2} {entry.Key.Item3}: {entry.Value.PendingBytes} bytes left undecoded");
            }

            if (!extractsKeys) return 0;
            extractor!.Write(keysOutput!, schemas.Build.ToString());
            Console.WriteLine(
                $"{extractor.KeyCount} keys, {extractor.ConflictCount} conflicts, {extractor.RejectedCount} rejected -> {keysOutput}");
            return extractor.ConflictCount > 0 ? 1 : 0;
        }

        private static void WriteSendIds(ExecutableProtocolSchemas schemas, string path)
        {
            var rows = new List<string> { "channel,id,shape" };
            rows.AddRange(schemas.GameChannelSendIds.OrderBy(id => id).Select(id => $"game,{id:x},{SchemaShape(schemas.Game.Send, id)}"));
            rows.AddRange(schemas.LoginSendIds.OrderBy(id => id).Select(id => $"login,{id:x},{SchemaShape(schemas.Login.Send, id)}"));
            File.WriteAllLines(path, rows);
            Console.WriteLine($"{rows.Count - 1} send ids of build {schemas.Build} -> {path}");
        }

        private static string SchemaShape(MessageSchemaSet set, int id) =>
            set.TryGet(id, out var schema) ? FieldShape(schema.Fields.Skip(1)) : "";

        private static string FieldShape(IEnumerable<FieldDefinition> fields) =>
            string.Join(" ", fields.Select(field => field.Reference is null
                ? $"{field.FieldType:x}:{field.Param:x}"
                : $"{field.FieldType:x}:{field.Param:x}[{FieldShape(field.Reference)}]"));

        private static void WriteReceiveHandlers(ExecutableProtocolSchemas schemas, string path)
        {
            var rows = new List<string> { "channel,id,handler" };
            rows.AddRange(schemas.GameReceiveHandlers.OrderBy(entry => entry.Key).Select(entry => $"game,{entry.Key:x},{entry.Value:x}"));
            rows.AddRange(schemas.LoginReceiveHandlers.OrderBy(entry => entry.Key).Select(entry => $"login,{entry.Key:x},{entry.Value:x}"));
            File.WriteAllLines(path, rows);
            Console.WriteLine($"{rows.Count - 1} receive handlers of build {schemas.Build} -> {path}");
        }

        private static string FormatFields(IEnumerable<DecodedField> fields) =>
            string.Join(" ", fields.Select((field, index) => $"[{index + 1}]={FormatValue(field.Value)}"));

        private static string FormatValue(object? value) => value switch
        {
            null => "-",
            byte[] bytes => bytes.Length <= BlobPreviewBytes || _printsFullBlobs
                ? Convert.ToHexString(bytes)
                : $"{Convert.ToHexString(bytes, 0, BlobPreviewBytes)}..({bytes.Length})",
            string text => $"\"{text}\"",
            DecodedArrayHeader position => $"({BitConverter.ToSingle(position.Header, 0)}, {BitConverter.ToSingle(position.Header, 4)}, {BitConverter.ToSingle(position.Header, 8)}; {position.Count})",
            IEnumerable<DecodedField> nested => $"{{{FormatFields(nested)}}}",
            IList list => $"[{list.Count}]{{{string.Join(",", list.Cast<object>().Take(_listPreviewItems).Select(FormatValue))}{(list.Count > _listPreviewItems ? ",.." : "")}}}",
            _ => value.ToString() ?? string.Empty,
        };

        [return: NotNullIfNotNull(nameof(fallback))]
        private static string? StringOption(string[] args, string name, string? fallback)
        {
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == name) return args[i + 1];
            return fallback;
        }
    }
}
