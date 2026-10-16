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
    /// </summary>
    public static class TodoScanner
    {
        public static List<TranslationItem> Scan(string languagesDir, string language)
        {
            var items = new List<TranslationItem>();
            int id = 1;
            string root = Path.Combine(languagesDir, language);
            ScanDir(Path.Combine(root, "Keyed"), ref id, items);
            string di = Path.Combine(root, "DefInjected");
            if (Directory.Exists(di))
                foreach (var dir in Directory.EnumerateDirectories(di))
                    ScanDir(dir, ref id, items);
            return items;
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
                        if (el.Value == "TODO")
                            items.Add(new TranslationItem
                            {
                                Id = id++,
                                Source = FindEnComment(el) ?? el.Name.ToString(),
                                File = file,
                                Key = el.Name.ToString(),
                            });
                    }
                }
                catch { }
            }
        }

        private static string? FindEnComment(XElement el)
        {
            XNode? node = el.PreviousNode;
            while (node != null)
            {
                if (node is XComment c && c.Value.TrimStart().StartsWith("EN:", StringComparison.OrdinalIgnoreCase))
                    return c.Value.Trim().Substring(3).Trim();
                if (node is XComment) return null;
                node = node.PreviousNode;
            }
            return null;
        }

        /// <summary>写回：只处理 glossary/ai/edited 且 Final 非空的项；TODO/failed 原样保留。</summary>
        public static int WriteBack(IEnumerable<TranslationItem> items)
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
                        if (item != null && el.Value == "TODO")
                        {
                            el.Value = item.Final.Replace("\n", "\\n");
                            changed = true;
                            written++;
                        }
                    }
                    if (changed)
                    {
                        Extraction.XmlIO.BackupFile(group.Key);
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
