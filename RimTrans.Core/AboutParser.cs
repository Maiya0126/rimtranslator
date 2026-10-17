using System.Linq;
using System.Xml.Linq;

namespace RimTrans.Core
{
    public static class AboutParser
    {
        public static void Parse(string aboutFile, ModInfo mod)
        {
            try
            {
                var root = XDocument.Load(aboutFile).Root;
                if (root == null) return;
                var name = (string?)root.Element("name");
                if (!string.IsNullOrWhiteSpace(name)) mod.Name = name.Trim();
                var pkg = (string?)root.Element("packageId");
                if (pkg != null) mod.PackageId = pkg.Trim();
                var authorEl = root.Element("author");
                if (authorEl != null)
                {
                    mod.Author = authorEl.HasElements
                        ? string.Join(",", authorEl.Elements("li").Select(e => e.Value.Trim()))
                        : authorEl.Value.Trim();
                }
                var sv = root.Element("supportedVersions");
                if (sv != null)
                    mod.SupportedVersions = sv.Elements("li")
                        .Select(e => e.Value.Trim())
                        .Where(v => v.Length > 0)
                        .ToList();
                var desc = (string?)root.Element("description");
                if (!string.IsNullOrWhiteSpace(desc)) mod.Description = desc.Trim();
                mod.AboutParsed = true;
            }
            catch
            {
            }
        }
    }
}
