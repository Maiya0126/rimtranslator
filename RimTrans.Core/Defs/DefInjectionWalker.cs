using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace RimTrans.Core.Defs
{
    /// <summary>
    /// Def 元数据的离线近似：从合并后的 XML 推断可翻译字段，规则对齐 Verse.DefInjectionUtility：
    /// - 只遍历 XML 里实际出现的字段（离线无法反射 C# 类型，用 XML 节点树代替对象树）；
    /// - 顶层 defName 段之后的路径 = 字段名.字段名 / 字段名.索引 / 字段名.索引.字段名…；
    /// - 文本叶子 → 单值注入；li 文本列表 → 集合注入（.0/.1/… 或全列表）；
    /// - ShouldCheckMissingInjection 近似：值非空且（含空格 或 已有翻译注入该路径）才输出 TODO 行；
    ///   纯单词值（如 label "Warg"）游戏默认不强制翻译，但已有翻译时仍保留（回填场景）。
    /// </summary>
    public static class DefInjectionWalker
    {
        public class PossibleInjection
        {
            public string SuggestedPath = "";
            public string NormalizedPath = "";
            public bool IsCollection;
            public List<string>? CollectionValues;
            public string? Value;
        }

        private static readonly string[] SkipFields = { "defName", "Name", "ParentName", "Abstract", "Class", "Inherit", "MayRequire", "MayRequireAnyOf", "TKey", "IgnoreIfNoMatchingField" };

        // 游戏侧这些字段是值类型/枚举/颜色类（C# 反射知道类型，离线不知道）——按类型名排除
        private static readonly string[] SkipFieldNameSuffixes = { "Color", "Filter", "Class", "Path", "def", "Def", "Tag", "Tags", "Level", "Mode", "hides", "StatBase", "workTags", "skillRequirements", "products" };

        private static bool ShouldSkipField(string name)
        {
            if (SkipFields.Contains(name)) return true;
            foreach (var suf in SkipFieldNameSuffixes)
                if (name.EndsWith(suf, StringComparison.Ordinal) && name.Length > suf.Length)
                    return true;
            // 全小写开头且无空格可能的值字段：保守不跳过，仅跳过已知值型字段名
            return false;
        }

        public static List<PossibleInjection> Walk(OfflineDef def)
        {
            var result = new List<PossibleInjection>();
            foreach (var field in def.Node.Elements())
            {
                if (ShouldSkipField(field.Name.LocalName)) continue;
                WalkField(field, def.DefName, def.DefName, result);
            }
            return result;
        }

        private static void WalkField(XElement field, string normalizedPath, string suggestedPath, List<PossibleInjection> result)
        {
            string name = field.Name.LocalName;
            string nPath = normalizedPath + "." + name;
            string sPath = suggestedPath + "." + name;

            var liChildren = field.Elements("li").ToList();
            if (liChildren.Count > 0)
            {
                var values = liChildren.Select(li => ConcatText(li)).ToList();
                result.Add(new PossibleInjection
                {
                    SuggestedPath = sPath,
                    NormalizedPath = nPath,
                    IsCollection = true,
                    CollectionValues = values,
                });
                for (int i = 0; i < liChildren.Count; i++)
                {
                    // 列表句柄近似（对齐 TranslationHandleUtility）：compClass 值做句柄，如 comps.CompProperties_Usable→CompUsable
                    string handle = ListElementHandle(liChildren[i]);
                    string sSeg = handle ?? i.ToString();
                    WalkNested(liChildren[i], nPath + "." + i, sPath + "." + sSeg, result);
                }
                return;
            }

            if (field.HasElements)
            {
                var namedChildren = field.Elements().Where(e => e.Name.LocalName != "li").ToList();
                if (namedChildren.Count == 0)
                {
                    foreach (var li in field.Elements("li")) { }
                }
                int idx = 0;
                foreach (var child in field.Elements())
                {
                    if (child.Name.LocalName == "li")
                    {
                        WalkNested(child, nPath + "." + idx, sPath + "." + idx, result);
                        idx++;
                    }
                    else
                    {
                        WalkField(child, nPath, sPath, result);
                    }
                }
                return;
            }

            string text = ConcatText(field);
            if (text.Length == 0) return;
            result.Add(new PossibleInjection
            {
                SuggestedPath = sPath,
                NormalizedPath = nPath,
                IsCollection = false,
                Value = text,
            });
        }

        /// <summary>
        /// 列表元素句柄（近似 Verse.TranslationHandleUtility）：
        /// li 含 compClass → 取类名去 CompProperties_ 前缀（如 CompProperties_Usable→CompUsable）；
        /// 含 label/title/name 等命名性子元素 → 取其值。
        /// </summary>
        private static string? ListElementHandle(XElement li)
        {
            // li 的 Class 属性（对齐游戏 GetBestHandleWithIndexForListElement 的 TranslationHandle 语义）
            string? cls = (string?)li.Attribute("Class");
            if (!string.IsNullOrEmpty(cls))
            {
                string n = cls!;
                int cut = n.LastIndexOf('.');
                if (cut >= 0) n = n[(cut + 1)..];
                // CompProperties_Usable → CompUsable（去 CompProperties_ 前缀、保留 Comp 头）
                int cp = n.IndexOf("CompProperties_");
                if (cp >= 0) return "Comp" + n[(cp + "CompProperties_".Length)..];
                return n;
            }
            string? compClass = (string?)li.Element("compClass");
            if (!string.IsNullOrEmpty(compClass))
            {
                string n = compClass!;
                int cut = n.IndexOf("CompProperties_");
                return cut >= 0 ? n[(cut + "CompProperties_".Length)..] : n;
            }
            foreach (var key in new[] { "label", "defName", "title", "name" })
            {
                string? v = (string?)li.Element(key);
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
            return null;
        }

        private static void WalkNested(XElement li, string nPath, string sPath, List<PossibleInjection> result)
        {
            foreach (var child in li.Elements())
            {
                string name = child.Name.LocalName;
                if (ShouldSkipField(name)) continue;
                WalkField(child, nPath, sPath, result);
            }
        }

        private static string ConcatText(XElement el)
        {
            if (el.HasElements) return "";
            return string.Concat(el.Nodes().OfType<XText>().Select(t => t.Value)).Trim();
        }

        /// <summary>近似 Verse.DefInjectionUtility.ShouldCheckMissingInjection：默认仅含空格的值强制要求翻译。</summary>
        public static bool ShouldCheckMissing(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            return value.Contains(' ');
        }
    }
}
