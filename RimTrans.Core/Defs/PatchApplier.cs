using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace RimTrans.Core.Defs
{
    /// <summary>
    /// PatchOperation 离线解析（V1.1）：Add / Replace / Insert / Remove 四操作。
    /// 对齐 Verse.PatchOperation*：xpath 选取目标 → 按 value 子树操作；Patches 目录 *.xml。
    /// 不支持：FindMod/Sequence/Conditional/Test/AttributeXxx（少数派，后续按需加）。
    /// </summary>
    public static class PatchApplier
    {
        public static void ApplyPatches(XDocument defsDoc, string patchesDir, Action<string>? onError = null)
        {
            if (!Directory.Exists(patchesDir)) return;
            foreach (var file in Directory.EnumerateFiles(patchesDir, "*.xml", SearchOption.AllDirectories))
            {
                XDocument patchDoc;
                try { patchDoc = XDocument.Load(file); }
                catch (Exception ex) { onError?.Invoke($"补丁解析失败 {file}: {ex.Message}"); continue; }

                foreach (var op in patchDoc.Root?.Elements() ?? Enumerable.Empty<XElement>())
                {
                    // 实际补丁形态：<Operation Class="PatchOperationAdd">；兼容直接以类名为元素名的写法
                    string typeName = ((string?)op.Attribute("Class")) ?? op.Name.LocalName;
                    try { ApplyOne(defsDoc, op, typeName, file); }
                    catch (Exception ex) { onError?.Invoke($"补丁应用失败 {Path.GetFileName(file)}/{typeName}: {ex.Message}"); }
                }
            }
        }

        private static void ApplyOne(XDocument doc, XElement op, string typeName, string file)
        {
            string? xpath = (string?)op.Element("xpath");
            if (xpath == null) return;
            xpath = xpath.Trim().Replace("./", "");

            switch (typeName)
            {
                case "PatchOperationAdd":
                case "PatchOperationInsert":
                case "PatchOperationReplace":
                {
                    var value = op.Element("value");
                    if (value == null) return;
                    var targets = SelectXpath(doc, xpath).ToList();
                    bool insertAfter = typeName == "PatchOperationInsert";
                    string? orderVal = (string?)op.Element("order");
                    bool prepend = typeName == "PatchOperationInsert" && !(orderVal?.Equals("Append", StringComparison.OrdinalIgnoreCase) ?? false)
                        || typeName == "PatchOperationAdd" && (orderVal?.Equals("Prepend", StringComparison.OrdinalIgnoreCase) ?? false);

                    if (typeName == "PatchOperationReplace")
                    {
                        foreach (var target in targets)
                        {
                            var parent = target.Parent;
                            if (parent == null) continue;
                            foreach (var child in value.Elements())
                                target.AddBeforeSelf(new XElement(child));
                            target.Remove();
                        }
                    }
                    else
                    {
                        foreach (var target in targets)
                        {
                            if (insertAfter)
                            {
                                var parent = target.Parent!;
                                foreach (var child in value.Elements())
                                    target.AddAfterSelf(new XElement(child));
                            }
                            else
                            {
                                foreach (var child in prepend ? value.Elements().Reverse() : value.Elements())
                                    target.Add(new XElement(child));
                            }
                        }
                    }
                    break;
                }
                case "PatchOperationRemove":
                {
                    foreach (var target in SelectXpath(doc, xpath).ToList())
                        target.Remove();
                    break;
                }
            }
        }

        /// <summary>
        /// 简化 xpath 引擎：支持 //ElementName、/Root/Child、[@attr='v']、[Element='v'] 谓词（社区补丁的 95% 用法）。
        /// </summary>
        private static System.Collections.Generic.IEnumerable<XElement> SelectXpath(XDocument doc, string xpath)
        {
            xpath = xpath.Trim();
            string[] segments = xpath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) return Enumerable.Empty<XElement>();

            // 绝对路径首段必须匹配根元素名（Defs）
            System.Collections.Generic.IEnumerable<XElement> current;
            if (xpath.StartsWith("//"))
            {
                current = doc.Descendants().Where(e => MatchSegment(e, segments[0]));
                return current.SelectMany(e => WalkSegments(e, segments, 1)).Distinct();
            }
            if (xpath.StartsWith("/"))
            {
                if (doc.Root == null || !MatchSegment(doc.Root, segments[0]))
                    return Enumerable.Empty<XElement>();
                current = new[] { doc.Root };
                return WalkSegments(doc.Root, segments, 1).Distinct();
            }
            // 相对路径：全文档 Descendants 找首段
            current = doc.Descendants().Where(e => MatchSegment(e, segments[0]));
            return current.SelectMany(e => WalkSegments(e, segments, 1)).Distinct();
        }

        private static System.Collections.Generic.IEnumerable<XElement> WalkSegments(XElement start, string[] segments, int from)
        {
            var current = new[] { start } as System.Collections.Generic.IEnumerable<XElement>;
            for (int i = from; i < segments.Length; i++)
            {
                string seg = segments[i];
                if (seg == "") continue;
                current = current.SelectMany(e => e.Elements().Where(c => MatchSegment(c, seg)));
            }
            return current;
        }

        private static bool MatchSegment(XElement el, string segment)
        {
            // 解析 Name[cond] / Name[@a='b'] / Name[child='v'] / li[2]
            string name = segment;
            string? predicate = null;
            int bracket = segment.IndexOf('[');
            if (bracket >= 0 && segment.EndsWith("]"))
            {
                name = segment[..bracket];
                predicate = segment[(bracket + 1)..^1];
            }
            if (name != "*" && !el.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return false;
            if (predicate == null) return true;

            // li[2] 数字索引在外层处理不了——按全局计数近似：这里简化为忽略数字谓词
            if (int.TryParse(predicate, out _)) return true;

            // [@attr='value']
            if (predicate.StartsWith("@"))
            {
                int eq = predicate.IndexOf('=');
                if (eq < 0) return el.Attribute(predicate[1..]) != null;
                string attrName = predicate[1..eq].Trim();
                string want = predicate[(eq + 1)..].Trim().Trim('\'', '"');
                return (string?)el.Attribute(attrName) == want;
            }
            // [child='value']
            int eq2 = predicate.IndexOf('=');
            if (eq2 < 0) return el.Element(predicate) != null;
            string childName = predicate[..eq2].Trim();
            string want2 = predicate[(eq2 + 1)..].Trim().Trim('\'', '"');
            return (string?)el.Element(childName) == want2;
        }
    }
}
