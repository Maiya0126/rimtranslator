using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace RimTrans.Core.AI
{
    /// <summary>
    /// 扫描提取产物中的 TODO 条目（Keyed + DefInjected），生成 TranslationItem 列表（含文件与元素键）；
    /// 确认后写回译文（保留 EN 注释，替换 TODO 文本，写前 .bak 备份）。
    /// 语言目录兼容双名：优先完整名（"ChineseSimplified (简体中文)"），退回纯名（"ChineseSimplified"）。
    /// </summary>
    public static class TodoScanner
    {
        public static List<TranslationItem> Scan(string languagesDir, string language)
        {
            var items = new List<TranslationItem>();
            int id = 1;
            string? root = FindLanguageDir(languagesDir, language);
            if (root == null) return items;
            ScanDir(Path.Combine(root, "Keyed"), ref id, items);
            string di = Path.Combine(root, "DefInjected");
            if (Directory.Exists(di))
                foreach (var dir in Directory.EnumerateDirectories(di))
                    ScanDir(dir, ref id, items);
            return items;
        }

        /// <summary>语言目录双名探测：完整名（Data tar 一致，如 "ChineseSimplified (简体中文)"）优先，纯名后备。</summary>
        public static string? FindLanguageDir(string languagesDir, string language)
        {
            var dirs = LanguageDirs(languagesDir, language);
            return dirs.FirstOrDefault();
        }

        /// <summary>目标语言全部候选目录：完整名（本工具输出，游戏内优先生效）在前，纯名（原 Mod 自带）在后。</summary>
        public static List<string> LanguageDirs(string languagesDir, string language)
        {
            var result = new List<string>();
            if (!Directory.Exists(languagesDir)) return result;
            foreach (var d in Directory.EnumerateDirectories(languagesDir))
            {
                string n = Path.GetFileName(d);
                if (n.Equals("English", StringComparison.OrdinalIgnoreCase)) continue;
                if (n.Equals(language, StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith(language + " ", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith(language + "(", StringComparison.OrdinalIgnoreCase))
                    result.Add(d);
            }
            // 完整名（带后缀）优先于纯名：与游戏 legacyFolderName 合并顺序一致
            result = result
                .OrderByDescending(d => Path.GetFileName(d).Length != language.Length)
                .ThenBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                .ToList();
            return result;
        }

        private static void ScanDir(string dir, ref int id, List<TranslationItem> items)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.EnumerateFiles(dir, "*.xml", SearchOption.AllDirectories))
            {
                string content;
                try { content = File.ReadAllText(file, Encoding.UTF8); } catch { continue; }
                if (!content.Contains("TODO")) continue;
                try
                {
                    var doc = XDocument.Parse(content);
                    foreach (var el in doc.Root!.Elements())
                    {
                        if (el.Value.Trim() == "TODO")
                            items.Add(new TranslationItem
                            {
                                Id = id++,
                                Source = FindEnComment(el) ?? el.Name.ToString(),
                                File = file,
                                Key = el.Name.ToString(),
                            });                    }
                }
                catch { }
            }
        }

        private static string? FindEnComment(XElement el)
        {
            // 形态1：注释在元素内部开头（KeyedExtractor 产物：<Key><!-- EN: ... -->\n  TODO</Key>）
            foreach (var n in el.Nodes())
            {
                if (n is XComment c0 && c0.Value.TrimStart().StartsWith("EN:", StringComparison.OrdinalIgnoreCase))
                    return c0.Value.Trim().Substring(3).Trim();
                if (n is XComment) break;
                if (n is XText t)
                {
                    if (!string.IsNullOrWhiteSpace(t.Value)) break;
                    continue;
                }
                break;
            }
            // 形态2：注释在元素外部之前（官方 TranslationFilesCleaner 产物惯例），跳过中间纯空白文本节点
            XNode? node = el.PreviousNode;
            while (node != null)
            {
                if (node is XComment c && c.Value.TrimStart().StartsWith("EN:", StringComparison.OrdinalIgnoreCase))
                    return c.Value.Trim().Substring(3).Trim();
                if (node is XComment) return null;
                if (node is XText tx && string.IsNullOrWhiteSpace(tx.Value)) { node = node.PreviousNode; continue; }
                return null;
            }
            return null;
        }

        /// <summary>写回：只处理 glossary/ai/edited 且 Final 非空的项；TODO/failed 原样保留。
        /// 只替换元素的文本内容，保留 EN 注释；元素值 Trim 后为 TODO 才写（防误覆盖已翻译内容）。
        /// skipBackup：独立汉化Mod 等全新生成场景，无需 .bak（被备份的只是本工具刚生成的模板）。</summary>
        public static int WriteBack(IEnumerable<TranslationItem> items, bool skipBackup = false)
        {
            int written = 0;
            foreach (var group in items.Where(i => IsWritable(i)).GroupBy(i => i.File))
            {
                try
                {
                    string content = File.ReadAllText(group.Key, Encoding.UTF8);
                    var doc = XDocument.Parse(content);
                    bool changed = false;
                    foreach (var el in doc.Root!.Elements())
                    {
                        var item = group.FirstOrDefault(i => i.Key == el.Name.ToString());
                        if (item != null && el.Value.Trim() == "TODO")
                        {
                            // 只移除文本节点，保留 EN 注释；再写入译文
                            foreach (var t in el.Nodes().OfType<XText>().ToList()) t.Remove();
                            el.Add(new XText(item.Final.Replace("\n", "\\n")));
                            changed = true;
                            written++;
                        }
                    }
                    if (changed)
                    {
                        if (!skipBackup) Extraction.XmlIO.BackupFile(group.Key);
                        File.WriteAllText(group.Key,
                            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" + doc.ToString().Replace("&gt;", ">"),
                            new UTF8Encoding(true));
                    }
                }
                catch
                {
                }
            }
            return written;
        }

        private static bool IsWritable(TranslationItem i) =>
            (i.Status == "glossary" || i.Status == "ai" || i.Status == "edited") && !string.IsNullOrWhiteSpace(i.Final);
    }
}
