using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace RimTrans.Core.Defs
{
    /// <summary>
    /// 从目标语言 DefInjected 目录收集既有注入（离线替代 DefInjectionPackage 注入词典）。
    /// 对齐 AddDataFromFile：TODO→占位、li 列表→全列表注入、rep 旧语法、\n 处理。
    /// 返回 normalizedPath → 译文（含全列表语法还原为 li 文本 "\\n" 连接）。
    /// </summary>
    public static class ExistingDefInjectionLoader
    {
        public static Dictionary<string, string> Load(string languagesDir, string language)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string baseDir = Path.Combine(languagesDir, language, "DefInjected");
            if (!Directory.Exists(baseDir)) return dict;

            foreach (var file in Directory.EnumerateFiles(baseDir, "*.xml", SearchOption.AllDirectories))
            {
                try
                {
                    var doc = XDocument.Load(file, LoadOptions.PreserveWhitespace);
                    var root = doc.DescendantNodes().OfType<XElement>().FirstOrDefault();
                    if (root == null) continue;
                    foreach (var el in root.Elements())
                    {
                        string path = ProcessedPath(el.Name.ToString());
                        if (el.Name.LocalName == "rep")
                        {
                            var p = el.Elements("path").FirstOrDefault();
                            var t = el.Elements("trans").FirstOrDefault();
                            if (p != null && t != null)
                                dict[ProcessedPath(p.Value)] = t.Value;
                            continue;
                        }
                        if (el.HasElements)
                        {
                            var items = el.Elements("li").Select(li => li.Value).ToList();
                            dict[path] = string.Join("\\n", items);
                        }
                        else
                        {
                            dict[path] = el.Value;
                        }
                    }
                }
                catch
                {
                }
            }
            return dict;
        }

        private static string ProcessedPath(string path)
        {
            if (!path.Contains('[') && !path.Contains(']')) return path;
            return path.Replace("]", "").Replace('[', '.');
        }
    }
}
