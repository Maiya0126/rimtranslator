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
    /// 术语库：自定义词库（%APPDATA%\RimTranslator\glossary-<语言>.json，玩家手动增删改，最高优先）+
    /// 本地官方库（从 Data 官方翻译构建，TryAdd 不覆盖自定义）。
    /// 翻译优先级：自定义 &gt; 云端词库（CloudGlossary，维护者持续更新）&gt; 本地官方库。
    /// </summary>
    public class Glossary
    {
        private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _customMap = new(StringComparer.OrdinalIgnoreCase);
        public int Count => _map.Count + _customMap.Count;

        /// <summary>玩家自定义词条（最高优先级，可顶掉官方/云端译名）。</summary>
        public void AddCustomRange(IEnumerable<GlossaryEntry> entries)
        {
            foreach (var e in entries)
                if (!string.IsNullOrWhiteSpace(e.En) && !string.IsNullOrWhiteSpace(e.Translation))
                {
                    _customMap.TryAdd(e.En.Trim(), e.Translation.Trim());
                    _map.TryAdd(e.En.Trim(), e.Translation.Trim());
                }
        }

        public void AddRange(IEnumerable<GlossaryEntry> entries)
        {
            foreach (var e in entries)
                if (!string.IsNullOrWhiteSpace(e.En) && !string.IsNullOrWhiteSpace(e.Translation))
                    _map.TryAdd(e.En.Trim(), e.Translation.Trim());
        }

        public bool TryMatch(string? en, out string translation)
        {
            if (!string.IsNullOrWhiteSpace(en) && _map.TryGetValue(en.Trim(), out translation!)) return true;
            foreach (var v in Variants(en))
                if (_map.TryGetValue(v, out translation!)) return true;
            translation = null!;
            return false;
        }

        /// <summary>仅匹配玩家自定义词条（用于比云端更高的一级优先）。</summary>
        public bool TryMatchCustom(string? en, out string translation)
        {
            if (!string.IsNullOrWhiteSpace(en) && _customMap.TryGetValue(en.Trim(), out translation!)) return true;
            foreach (var v in Variants(en))
                if (_customMap.TryGetValue(v, out translation!)) return true;
            translation = null!;
            return false;
        }

        public static IEnumerable<string> Variants(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) yield break;
            string t = s.Trim();
            string lower = t.ToLowerInvariant();
            string baseWord = t;
            foreach (var article in new[] { "the ", "a ", "an " })
            {
                if (lower.StartsWith(article, StringComparison.Ordinal))
                {
                    baseWord = t[article.Length..].Trim();
                    if (baseWord.Length > 0) yield return baseWord;
                    break;
                }
            }
            if (baseWord.Length > 3 && baseWord.EndsWith('s') && !baseWord.EndsWith("ss", StringComparison.Ordinal))
                yield return baseWord[..^1];
        }

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

        /// <summary>导出词库为 JSON（供汉化组互传）。format: rimtrans-glossary-v1。</summary>
        public static void ExportJson(string path, List<GlossaryEntry> entries, string language)
        {
            var payload = new
            {
                format = "rimtrans-glossary-v1",
                language,
                exportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                count = entries.Count,
                entries = entries.Select(e => new { en = e.En, zh = e.Translation, note = e.Note }),
            };
            File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }

        /// <summary>导入词库 JSON（本工具导出格式）。返回 (新增, 跳过重复)。格式不符返回 null。</summary>
        public static (int added, int skipped)? ImportJson(string path, List<GlossaryEntry> into)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                if (root.TryGetProperty("format", out var fmt) && fmt.GetString() != "rimtrans-glossary-v1")
                    return null;
                if (!root.TryGetProperty("entries", out var arr)) return null;
                var seen = new HashSet<string>(into.Select(e => e.En), StringComparer.OrdinalIgnoreCase);
                int added = 0, skipped = 0;
                foreach (var el in arr.EnumerateArray())
                {
                    string? en = el.TryGetProperty("en", out var e) ? e.GetString() : null;
                    string? zh = el.TryGetProperty("zh", out var z) ? z.GetString() : null;
                    if (string.IsNullOrWhiteSpace(en) || string.IsNullOrWhiteSpace(zh)) { skipped++; continue; }
                    if (seen.Add(en.Trim()))
                    {
                        into.Add(new GlossaryEntry
                        {
                            En = en.Trim(),
                            Translation = zh.Trim(),
                            Note = el.TryGetProperty("note", out var n) ? n.GetString() ?? "" : "",
                            Source = "导入",
                        });
                        added++;
                    }
                    else skipped++;
                }
                return (added, skipped);
            }
            catch
            {
                return null;
            }
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
