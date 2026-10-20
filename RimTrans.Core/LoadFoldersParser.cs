using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace RimTrans.Core
{
    public static class LoadFoldersParser
    {
        /// <summary>
        /// 解析 LoadFolders.xml。键为版本号（"1.6"/"1.7"）。
        /// 兼容三种写法：&lt;v1.6&gt;（RimWorld 1.0-1.6 惯例）、&lt;1.7&gt;（数字元素名，1.7 Waylith 起出现）、
        /// 以及带尾缀的 &lt;v1.7-Waylith&gt; / &lt;1.7.1&gt;（取主版本号）。
        /// </summary>
        public static Dictionary<string, List<string>> Parse(string file)
        {
            var dict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var root = XDocument.Load(file).Root;
                if (root == null) return dict;
                foreach (var el in root.Elements())
                {
                    string key = el.Name.LocalName;
                    if (key.Length > 1 && (key[0] == 'v' || key[0] == 'V')) key = key[1..];
                    // 主版本号归一：1.7.1 → 1.7；1.7-Waylith → 1.7
                    int dash = key.IndexOf('-');
                    if (dash > 0) key = key[..dash];
                    int dot2 = key.IndexOf('.', key.IndexOf('.') + 1);
                    if (dot2 > 0) key = key[..dot2];
                    var list = el.Elements("li")
                        .Select(e => e.Value?.Trim())
                        .Where(v => !string.IsNullOrEmpty(v))
                        .Select(v => v!.Replace('\\', '/'))
                        .ToList();
                    if (list.Count > 0)
                    {
                        if (dict.TryGetValue(key, out var existing))
                            existing.AddRange(list.Where(x => !existing.Contains(x, StringComparer.Ordinal)));
                        else
                            dict[key] = list;
                    }
                }
            }
            catch
            {
            }
            return dict;
        }
    }
}
