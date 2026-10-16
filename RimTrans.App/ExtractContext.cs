using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimTrans.Core;

namespace RimTrans.App
{
    /// <summary>提取上下文：Patches 目录 + 跨 Mod 继承的依赖 Defs 目录（V1.1）。</summary>
    internal static class ExtractContext
    {
        /// <summary>收集各内容目录下的 Patches 目录。</summary>
        public static List<string> GetPatchDirs(ModInfo mod, string version)
        {
            var list = new List<string>();
            foreach (var dir in mod.GetContentDirs(version))
            {
                string p = Path.Combine(dir, "Patches");
                if (Directory.Exists(p)) list.Add(p);
            }
            return list;
        }

        /// <summary>
        /// 解析 About 的依赖（modDependencies/loadAfter）→ 从扫描结果找依赖 Mod 根目录 → 其 Defs 供跨 Mod 继承。
        /// 依赖未安装则跳过（与游戏行为一致：ParentName 找不到时报错跳过该 Def）。
        /// </summary>
        public static List<string> GetDependencyDefDirs(ModInfo mod, IEnumerable<ModInfo> allMods, string version, Action<string>? log = null)
        {
            var result = new List<string>();
            string aboutFile = Path.Combine(mod.RootDir, "About", "About.xml");
            if (!File.Exists(aboutFile)) return result;
            var depIds = new List<string>();
            try
            {
                var doc = System.Xml.Linq.XDocument.Load(aboutFile);
                foreach (var li in doc.Root?.Element("modDependencies")?.Elements("li") ?? Enumerable.Empty<System.Xml.Linq.XElement>())
                {
                    string? id = (string?)li.Element("packageId");
                    if (!string.IsNullOrWhiteSpace(id)) depIds.Add(id.Trim());
                }
                foreach (var li in doc.Root?.Element("loadAfter")?.Elements("li") ?? Enumerable.Empty<System.Xml.Linq.XElement>())
                {
                    string? id = (string?)li.Value;
                    if (!string.IsNullOrWhiteSpace(id)) depIds.Add(id.Trim());
                }
            }
            catch { }

            foreach (var depId in depIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                // loadAfter 只是排序建议，不一定是继承来源；只有 modDependencies 才可靠——这里两者都查但仅取真正存在的
                var dep = allMods.FirstOrDefault(m => string.Equals(m.PackageId, depId, StringComparison.OrdinalIgnoreCase));
                if (dep == null)
                {
                    log?.Invoke($"依赖 {depId} 未安装，其抽象节点不可用（相关 Def 将跳过）");
                    continue;
                }
                foreach (var dir in dep.GetContentDirs(version))
                {
                    string defs = Path.Combine(dir, "Defs");
                    if (Directory.Exists(defs)) result.Add(dir);
                }
            }
            return result;
        }
    }
}
