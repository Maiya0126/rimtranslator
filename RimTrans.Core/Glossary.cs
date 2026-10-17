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
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RimTranslator", $"glossary-{language}.json");

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

        /// <summary>
        /// 从既有翻译文件导入词库：支持 Languages\<语言>\Keyed\*.xml 与 DefInjected\*\*.xml，
        /// EN 原文来自紧邻元素的 &lt;!-- EN: ... --&gt; 注释（本工具与官方提取格式都有）。
        /// 用于把现成汉化包变成参考语料：精确命中直接回填，其余作为 AI 风格参考。
        /// </summary>
        public static (int added, int skipped) ImportFromTranslationDir(string languageDir, string language, List<GlossaryEntry> into)
        {
            int added = 0, skipped = 0;
            var seen = new HashSet<string>(into.Select(e => e.En), StringComparer.OrdinalIgnoreCase);

            string keyedDir = Path.Combine(languageDir, "Keyed");
            string diDir = Path.Combine(languageDir, "DefInjected");
            var dirs = new List<string>();
            if (Directory.Exists(keyedDir)) dirs.Add(keyedDir);
            if (Directory.Exists(diDir))
                dirs.AddRange(Directory.EnumerateDirectories(diDir));

            foreach (var dir in dirs)
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.xml", SearchOption.AllDirectories))
                {
                    try
                    {
                        var doc = System.Xml.Linq.XDocument.Load(file);
                        var root = doc.DescendantNodes().OfType<System.Xml.Linq.XElement>().FirstOrDefault();
                        if (root == null) continue;
                        foreach (var el in root.Elements())
                        {
                            string value = el.Value.Trim();
                            if (value.Length == 0 || value == "TODO") { skipped++; continue; }
                            if (value.Length > 300) { skipped++; continue; }

                            string? en = FindEnComment(el);
                            if (string.IsNullOrWhiteSpace(en) || en.Length < 2 || en.Length > 200) { skipped++; continue; }
                            if (string.Equals(en, value, StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }

                            if (seen.Add(en.Trim()))
                            {
                                into.Add(new GlossaryEntry
                                {
                                    En = en.Trim(),
                                    Translation = value,
                                    Note = $"导入自 {Path.GetFileName(Path.GetDirectoryName(file))}\\{Path.GetFileName(file)}",
                                    Source = "导入",
                                });
                                added++;
                            }
                            else skipped++;
                        }
                    }
                    catch { }
                }
            }
            return (added, skipped);

            static string? FindEnComment(System.Xml.Linq.XElement el)
            {
                System.Xml.Linq.XNode? node = el.PreviousNode;
                while (node != null)
                {
                    if (node is System.Xml.Linq.XComment c && c.Value.TrimStart().StartsWith("EN:", StringComparison.OrdinalIgnoreCase))
                        return c.Value.Trim().Substring(3).Trim();
                    if (node is System.Xml.Linq.XComment) return null;
                    node = node.PreviousNode;
                }
                return null;
            }
        }
    }
}
