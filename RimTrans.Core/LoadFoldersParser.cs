using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace RimTrans.Core
{
    public static class LoadFoldersParser
    {
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
                    if (key.Length > 1 && key[0] == 'v' || key[0] == 'V') key = key[1..];
                    var list = el.Elements("li")
                        .Select(e => e.Value?.Trim())
                        .Where(v => !string.IsNullOrEmpty(v))
                        .Select(v => v!.Replace('\\', '/'))
                        .ToList();
                    if (list.Count > 0) dict[key] = list;
                }
            }
            catch
            {
            }
            return dict;
        }
    }
}
