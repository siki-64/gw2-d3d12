using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Gw2.Protocol.MsgPack;

namespace Gw2.CaptureDecoder
{
    public sealed class KeyExtraction
    {
        public const int TextKeyBatchMessageId = 0x2EC;
        public const int AssetKeyMessageId = 0x4D1;
        public const string TextKind = "text";
        public const string AudioKind = "audio";

        private const ulong TextKeyHighBits = 0xFFFFE00000000000;
        private const int EntriesFieldIndex = 1;
        private const int EntryIdFieldIndex = 0;
        private const int EntryKeyFieldIndex = 1;
        private const int KeyByteLength = 8;

        private readonly Dictionary<string, Dictionary<uint, ulong>> _keys = new Dictionary<string, Dictionary<uint, ulong>>
        {
            [TextKind] = new Dictionary<uint, ulong>(),
            [AudioKind] = new Dictionary<uint, ulong>(),
        };
        private readonly List<Conflict> _conflicts = new List<Conflict>();

        public int RejectedCount { get; private set; }

        public int KeyCount => _keys.Values.Sum(keys => keys.Count);

        public int ConflictCount => _conflicts.Count;

        public void Add(DecodedMessage message)
        {
            if (message.MessageId == TextKeyBatchMessageId) Collect(message, TextKind, true);
            else if (message.MessageId == AssetKeyMessageId) Collect(message, AudioKind, false);
        }

        public void Write(string path, string build)
        {
            if (File.Exists(path)) Merge(File.ReadAllText(path));
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            using var stream = File.Create(path);
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, IndentSize = 2 });
            writer.WriteStartObject();
            writer.WriteString("build", build);
            writer.WriteStartObject("keys");
            foreach (KeyValuePair<string, Dictionary<uint, ulong>> kind in _keys.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                writer.WriteStartObject(kind.Key);
                foreach (KeyValuePair<uint, ulong> entry in kind.Value.OrderBy(entry => entry.Key))
                {
                    writer.WritePropertyName(entry.Key.ToString());
                    writer.WriteStringValue(entry.Value.ToString("X16"));
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteStartArray("conflicts");
            foreach (Conflict conflict in _conflicts)
            {
                writer.WriteStartObject();
                writer.WriteString("kind", conflict.Kind);
                writer.WriteNumber("id", conflict.Id);
                writer.WriteStartArray("keys");
                writer.WriteStringValue(conflict.Known.ToString("X16"));
                writer.WriteStringValue(conflict.Found.ToString("X16"));
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private void Collect(DecodedMessage message, string kind, bool rejectHighTextKeyBits)
        {
            if (message.Fields.Count <= EntriesFieldIndex) return;
            if (!(message.Fields[EntriesFieldIndex].Value is IReadOnlyList<DecodedField[]> entries)) return;
            foreach (DecodedField[] entry in entries)
            {
                if (entry.Length <= EntryKeyFieldIndex) continue;
                if (!(entry[EntryIdFieldIndex].Value is ulong id)) continue;
                if (!(entry[EntryKeyFieldIndex].Value is byte[] keyBytes) || keyBytes.Length != KeyByteLength) continue;
                ulong key = BitConverter.ToUInt64(keyBytes, 0);
                if (key == 0) continue;
                if (rejectHighTextKeyBits && (key & TextKeyHighBits) != 0)
                {
                    RejectedCount++;
                    continue;
                }
                Store(kind, (uint)id, key);
            }
        }

        private void Store(string kind, uint id, ulong key)
        {
            Dictionary<uint, ulong> keys = _keys[kind];
            if (keys.TryGetValue(id, out ulong known))
            {
                if (known != key) _conflicts.Add(new Conflict(kind, id, known, key));
                return;
            }
            keys[id] = key;
        }

        private void Merge(string existingJson)
        {
            using JsonDocument document = JsonDocument.Parse(existingJson);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("keys", out JsonElement kinds)) return;
            foreach (JsonProperty kind in kinds.EnumerateObject())
            {
                foreach (JsonProperty entry in kind.Value.EnumerateObject())
                {
                    uint id = uint.Parse(entry.Name);
                    ulong key = ulong.Parse(entry.Value.GetString()!, System.Globalization.NumberStyles.HexNumber);
                    if (key == 0) continue;
                    if (_keys.TryGetValue(kind.Name, out Dictionary<uint, ulong>? keys))
                    {
                        if (keys.TryGetValue(id, out ulong known))
                        {
                            if (known != key) _conflicts.Add(new Conflict(kind.Name, id, key, known));
                        }
                        else keys[id] = key;
                    }
                }
            }
        }

        private readonly struct Conflict
        {
            public Conflict(string kind, uint id, ulong known, ulong found)
            {
                Kind = kind;
                Id = id;
                Known = known;
                Found = found;
            }

            public string Kind { get; }
            public uint Id { get; }
            public ulong Known { get; }
            public ulong Found { get; }
        }
    }
}
