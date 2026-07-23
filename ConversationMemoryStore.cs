using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace KeyMapper
{
    internal static class ConversationMemoryStore
    {
        private const int MaximumTurnsPerCharacter = 24;
        private static readonly object SyncRoot = new();
        private static readonly string MemoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KeyMapper",
            "conversation-memory.json");

        public static IReadOnlyList<ConversationTurn> Load(string characterName)
        {
            lock (SyncRoot)
            {
                MemoryDocument document = ReadDocument();
                return document.Characters.TryGetValue(
                    characterName,
                    out List<StoredTurn>? turns)
                    ? turns
                        .Where(IsValid)
                        .TakeLast(MaximumTurnsPerCharacter)
                        .Select(turn => new ConversationTurn(turn.Role, turn.Content))
                        .ToArray()
                    : [];
            }
        }

        public static void Save(
            string characterName,
            IReadOnlyList<ConversationTurn> turns)
        {
            lock (SyncRoot)
            {
                MemoryDocument document = ReadDocument();
                document.Characters[characterName] = turns
                    .Where(turn =>
                        !string.IsNullOrWhiteSpace(turn.Role) &&
                        !string.IsNullOrWhiteSpace(turn.Content))
                    .TakeLast(MaximumTurnsPerCharacter)
                    .Select(turn => new StoredTurn
                    {
                        Role = turn.Role,
                        Content = turn.Content.Length > 4000
                            ? turn.Content[..4000]
                            : turn.Content
                    })
                    .ToList();
                WriteDocument(document);
            }
        }

        public static void Clear(string characterName)
        {
            lock (SyncRoot)
            {
                MemoryDocument document = ReadDocument();
                if (!document.Characters.Remove(characterName)) return;
                WriteDocument(document);
            }
        }

        private static MemoryDocument ReadDocument()
        {
            try
            {
                if (!File.Exists(MemoryPath))
                    return new MemoryDocument();

                string json = File.ReadAllText(MemoryPath);
                return JsonSerializer.Deserialize<MemoryDocument>(json)
                       ?? new MemoryDocument();
            }
            catch
            {
                return new MemoryDocument();
            }
        }

        private static void WriteDocument(MemoryDocument document)
        {
            try
            {
                string? directory = Path.GetDirectoryName(MemoryPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                string temporaryPath = $"{MemoryPath}.tmp";
                File.WriteAllText(
                    temporaryPath,
                    JsonSerializer.Serialize(
                        document,
                        new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporaryPath, MemoryPath, true);
            }
            catch
            {
                // Conversation remains available in memory even if local
                // persistence is unavailable.
            }
        }

        private static bool IsValid(StoredTurn turn) =>
            !string.IsNullOrWhiteSpace(turn.Role) &&
            !string.IsNullOrWhiteSpace(turn.Content);

        private sealed class MemoryDocument
        {
            public MemoryDocument()
            {
            }

            public Dictionary<string, List<StoredTurn>> Characters { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class StoredTurn
        {
            public StoredTurn()
            {
            }

            public string Role { get; set; } = string.Empty;
            public string Content { get; set; } = string.Empty;
        }
    }
}
