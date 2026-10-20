using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace RimTrans.Core.Extraction
{
    /// <summary>
    /// 从 Mod 的目标语言既有翻译目录收集 KeyedReplacement（离线替代游戏内 keyedReplacements）。
    /// 对齐游戏 LoadedLanguage.LoadFromFile_Keyed：值=="TODO" 视为 placeholder。
    /// 语言目录双名兼容：完整名（"ChineseSimplified (简体中文)"，本工具输出）与纯名（原 Mod 自带）合并读取，完整名优先。
    /// </summary>
    public static class ExistingTranslationLoader
    {
        public static List<KeyedReplacement> LoadFromLanguagesDir(string languagesDir, string language, string dataDirToExclude)
        {
            var list = new List<KeyedReplacement>();
            foreach (var langDir in RimTrans.Core.AI.TodoScanner.LanguageDirs(languagesDir, language))
            {
                string keyed = Path.Combine(langDir, "Keyed");
                if (!Directory.Exists(keyed)) continue;
                LoadFromKeyedDir(keyed, dataDirToExclude, list);
            }
            return list;
        }

        internal static void LoadFromKeyedDir(string keyedDir, string dataDirToExclude, List<KeyedReplacement> list)
        {
            foreach (var file in Directory.EnumerateFiles(keyedDir, "*.xml", SearchOption.AllDirectories))
            {
                try
                {
                    var doc = XDocument.Load(file, LoadOptions.PreserveWhitespace);
                    var root = doc.DescendantNodes().OfType<XElement>().FirstOrDefault();
                    if (root == null) continue;
                    foreach (var el in root.Elements())
                    {
                        string value = el.Nodes().OfType<XText>().Select(t => t.Value).DefaultIfEmpty("").Aggregate((a, b) => a + b);
                        var kr = new KeyedReplacement
                        {
                            Key = el.Name.ToString(),
                            Value = value,
                            IsPlaceholder = value.Trim() == "TODO",
                            FileSourceFullPath = Path.GetFullPath(file),
                        };
                        if (!string.IsNullOrEmpty(dataDirToExclude) && kr.FileSourceFullPath.Contains(dataDirToExclude, StringComparison.OrdinalIgnoreCase))
                            continue;
                        list.Add(kr);
                    }
                }
                catch
                {
                }
            }
        }
    }
}
