using System;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace RimTrans.Core.Extraction
{
    public static class XmlIO
    {
        public static string SanitizeXComment(string comment)
        {
            while (comment.Contains("-----"))
                comment = comment.Replace("-----", "- - -");
            while (comment.Contains("--"))
                comment = comment.Replace("--", "- -");
            return comment;
        }

        public static void SaveWithProcessedNewlineTags(XNode doc, string path)
        {
            string content = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n"
                + doc.ToString().Replace("<!--NEWLINE-->", "").Replace("&gt;", ">");
            File.WriteAllText(path, content, new UTF8Encoding(true));
        }

        public static void BackupFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Copy(path, path + ".bak", true);
            }
            catch (IOException)
            {
            }
        }
    }
}
