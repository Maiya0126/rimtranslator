using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RimTrans.Core
{
    public enum ModSource
    {
        Workshop,
        Local,
        GameData,
        Custom
    }

    public class ModInfo
    {
        public string Name { get; set; } = "";
        public string PackageId { get; set; } = "";
        public string Author { get; set; } = "";
        public string Description { get; set; } = "";
        public string RootDir { get; set; } = "";
        public ModSource Source { get; set; }
        public string PublishedFileId { get; set; } = "";
        public bool AboutParsed { get; set; }
        public List<string> SupportedVersions { get; set; } = new();
        public Dictionary<string, List<string>> LoadFolders { get; set; } = new();
        public List<string> Languages { get; set; } = new();

        public string SourceDisplay => Source switch
        {
            ModSource.Workshop => "工坊",
            ModSource.Local => "本地",
            ModSource.GameData => "Data",
            ModSource.Custom => "自定义",
            _ => ""
        };

        public string VersionsDisplay => SupportedVersions.Count == 0 ? "-" : string.Join(",", SupportedVersions);
        public string LanguagesDisplay => Languages.Count == 0 ? "-" : string.Join(",", Languages);

        public List<string> GetContentDirs(string version)
        {
            var result = new List<string>();
            if (LoadFolders.Count > 0 && LoadFolders.TryGetValue(version, out var rels))
            {
                foreach (var rel in rels)
                {
                    string path = rel == "/" ? RootDir : Path.GetFullPath(Path.Combine(RootDir, rel.TrimStart('/', '\\')));
                    if (Directory.Exists(path) && !result.Contains(path, StringComparer.OrdinalIgnoreCase))
                        result.Add(path);
                }
                if (result.Count == 0) result.Add(RootDir);
            }
            else
            {
                result.Add(RootDir);
            }
            return result;
        }

        public List<string> GetAllVersions()
        {
            var set = new HashSet<string>(SupportedVersions, StringComparer.OrdinalIgnoreCase);
            foreach (var v in LoadFolders.Keys) set.Add(v);
            return set.ToList();
        }
    }
}
