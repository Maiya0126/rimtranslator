using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RimTrans.Core
{
    public static class GameLanguageFinder
    {
        public static List<string> GetLanguages(string dataDir)
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "English" };
            string core = Path.Combine(dataDir, "Core", "Languages");
            if (Directory.Exists(core))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(core))
                {
                    string n = Path.GetFileName(entry);
                    if (n.Equals("English", StringComparison.OrdinalIgnoreCase)) continue;
                    if (n.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
                        n = n[..^4];
                    int sp = n.IndexOf(' ');
                    if (sp > 0) n = n[..sp];
                    if (n.Length > 0) set.Add(n);
                }
            }
            return set.ToList();
        }
    }
}
