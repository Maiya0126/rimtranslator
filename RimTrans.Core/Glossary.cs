using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RimTrans.Core
{
    public class GlossaryEntry
    {
        public string En { get; set; } = "";
        public string Translation { get; set; } = "";
        public string Note { get; set; } = "";
        public string Source { get; set; } = "自定义";
    }

    /// <summary>
    /// 术语库：官方库（从 Data 官方翻译构建，只读）+ 玩家自定义词库（%APPDATA%\RimTrans\glossary-<语言>.json，可增删改）。
    /// 匹配时自定义词库优先于官方库。
    /// </summary>
    public class Glossary
    {
        private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);
        public int Count => _map.Count;

        public void AddRange(IEnumerable<GlossaryEntry> entries)
        {
            foreach (var e in entries)
                if (!string.IsNullOrWhiteSpace(e.En) && !string.IsNullOrWhiteSpace(e.Translation))
                    _map[e.En.Trim()] = e.Translation.Trim();
        }

        public bool TryMatch(string en, out string translation) => _map.TryGetValue(en ?? "", out translation!);

        public IEnumerable<GlossaryEntry> Entries
        {
            get
            {
                foreach (var kv in _map)
                    yield return new GlossaryEntry { En = kv.Key, Translation = kv.Value };
            }
        }

        private static string CustomPath(string language) =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RimTrans", $"glossary-{language}.json");

        public static List<GlossaryEntry> LoadCustom(string language)
        {
            try
            {
                string path = CustomPath(language);
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<List<GlossaryEntry>>(File.ReadAllText(path)) ?? new();
            }
            catch { }
            return new();
        }

        public static void SaveCustom(string language, List<GlossaryEntry> entries)
        {
            string path = CustomPath(language);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
