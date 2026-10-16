using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace RimTrans.Core.Defs
{
    /// <summary>
    /// 离线 Def 表示：继承合并后的 XElement + 元数据。
    /// 对齐游戏 DirectXmlLoader.DefFromNode：Abstract="True" 跳过、Class 属性决定 Def 类型、
    /// defName 来自 &lt;defName&gt;（缺省时 Name 属性去掉 Base 后缀）。
    /// </summary>
    public class OfflineDef
    {
        public string DefTypeName = "";
        public string DefName = "";
        public string FileName = "";
        public XElement Node = null!;
        public string ModId = "";
    }

    /// <summary>
    /// 离线版 XmlInheritance：Name/ParentName/Abstract 继承合并，算法对齐 Verse.XmlInheritance：
    /// - 父节点深拷贝为基底，子节点递归覆盖：文本节点整体替换；同名子元素递归合并；li 直接追加；
    ///   Inherit="False" 清空父内容全量替换；子无元素但有文本→替换全部子内容；子无元素无文本→保留父。
    /// - 父查找规则：同 Mod 内最近定义 > 跨 Mod（本项目单 Mod 独立解析，同库内按文件加载顺序取最新）。
    /// </summary>
    public static class XmlInheritanceMerger
    {
        public static List<OfflineDef> LoadAndMerge(IEnumerable<string> defDirs, string modId, Action<string>? onError = null)
        {
            var files = new List<(string path, string relName)>();
            foreach (var dir in defDirs)
            {
                string defsRoot = Path.Combine(dir, "Defs");
                if (!Directory.Exists(defsRoot)) continue;
                foreach (var f in Directory.EnumerateFiles(defsRoot, "*.xml", SearchOption.AllDirectories))
                    files.Add((f, Path.GetFileName(f)));
            }

            var nodesByName = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
            var pending = new List<(XElement node, string file)>();

            foreach (var (path, relName) in files)
            {
                XDocument doc;
                try { doc = XDocument.Load(path); }
                catch (Exception ex) { onError?.Invoke($"XML 解析失败 {path}: {ex.Message}"); continue; }

                foreach (var el in doc.Root?.Elements() ?? Enumerable.Empty<XElement>())
                {
                    string? name = (string?)el.Attribute("Name");
                    if (name != null)
                        nodesByName[name] = el;
                    pending.Add((el, relName));
                }
            }

            var result = new List<OfflineDef>();
            var cache = new Dictionary<XElement, XElement>();
            foreach (var (node, file) in pending)
            {
                if (IsAbstract(node)) continue;
                try
                {
                    XElement resolved = Resolve(node, nodesByName, cache, new HashSet<XElement>());
                    string typeName = (string?)resolved.Attribute("Class") ?? node.Name.LocalName;
                    string? defName = (string?)resolved.Element("defName");
                    if (string.IsNullOrWhiteSpace(defName))
                    {
                        string? nameAttr = (string?)node.Attribute("Name");
                        defName = nameAttr != null && nameAttr.EndsWith("Base", StringComparison.OrdinalIgnoreCase)
                            ? nameAttr[..^4]
                            : null;
                    }
                    if (string.IsNullOrWhiteSpace(defName)) continue;
                    result.Add(new OfflineDef { DefTypeName = typeName, DefName = defName!, FileName = file, Node = resolved, ModId = modId });
                }
                catch (Exception ex)
                {
                    onError?.Invoke($"Def 解析失败 {file}/{node.Name}: {ex.Message}");
                }
            }
            return result;
        }

        private static bool IsAbstract(XElement el)
        {
            string? v = (string?)el.Attribute("Abstract");
            return v != null && v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        private static XElement Resolve(XElement node, Dictionary<string, XElement> byName, Dictionary<XElement, XElement> cache, HashSet<XElement> visiting)
        {
            if (cache.TryGetValue(node, out var cached)) return cached;
            string? parentName = (string?)node.Attribute("ParentName");
            if (parentName == null)
            {
                cache[node] = node;
                return node;
            }
            if (!visiting.Add(node)) throw new InvalidOperationException("循环继承: " + parentName);
            if (!byName.TryGetValue(parentName, out var parent))
                throw new InvalidOperationException("找不到父节点: " + parentName);
            XElement resolvedParent = Resolve(parent, byName, cache, visiting);
            XElement merged = new XElement(resolvedParent);
            RecursiveCopyOverwrite(node, merged);
            cache[node] = merged;
            return merged;
        }

        /// <summary>对齐 Verse.XmlInheritance.RecursiveNodeCopyOverwriteElements</summary>
        private static void RecursiveCopyOverwrite(XElement child, XElement current)
        {
            string? inherit = (string?)child.Attribute("Inherit");
            if (inherit != null && inherit.ToLower() == "false")
            {
                current.RemoveNodes();
                foreach (var n in child.Nodes()) current.Add(n);
                foreach (var a in child.Attributes())
                    if (a.Name != "Inherit")
                        current.SetAttributeValue(a.Name, a.Value);
                return;
            }

            current.RemoveAttributes();
            foreach (var a in child.Attributes())
                current.SetAttributeValue(a.Name, a.Value);

            var childText = child.Nodes().OfType<XText>().LastOrDefault();
            bool childHasElements = child.Elements().Any();

            if (childText != null)
            {
                current.RemoveNodes();
                current.Add(new XText(childText.Value));
                return;
            }
            if (!childHasElements)
            {
                if (current.Elements().Any()) return;
                current.RemoveNodes();
                return;
            }

            foreach (var el in child.Elements())
            {
                if (el.Name.LocalName == "li")
                {
                    current.Add(new XElement(el));
                    continue;
                }
                var existing = current.Elements(el.Name).FirstOrDefault();
                if (existing != null)
                {
                    RecursiveCopyOverwrite(el, existing);
                }
                else
                {
                    current.Add(new XElement(el));
                }
            }
        }
    }
}
