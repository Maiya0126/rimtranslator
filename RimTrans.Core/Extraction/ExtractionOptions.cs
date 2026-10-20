using System;
using System.Collections.Generic;

namespace RimTrans.Core.Extraction
{
    public enum OutputMode
    {
        ModDir,
        CustomDir,
        SkeletonMod
    }

    public class ExtractionOptions
    {
        public string GameVersion { get; set; } = "1.6";
        public string Language { get; set; } = "ChineseSimplified";
        public OutputMode Mode { get; set; } = OutputMode.ModDir;
        public string CustomDir { get; set; } = "";
        public string GameDataDir { get; set; } = "";
        public bool SkipBackup { get; set; }

        /// <summary>
        /// 目标语言完整输出文件夹名（如 "ChineseSimplified (简体中文)"，与游戏 Data tar 名一致）。
        /// 由 MainWindow 在启动提取前填充；为空时退回 Language 纯名（兼容旧调用）。
        /// </summary>
        public string LanguageFolder { get; set; } = "";

        public string LanguageFolderResolved => string.IsNullOrWhiteSpace(LanguageFolder) ? Language : LanguageFolder;

        public const string GeneratorName = "边缘译者 RimTranslator";
    }

    public interface IExtractLogger
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
    }

    public class ExtractResult
    {
        public int FilesWritten;
        public int KeysTotal;
        public int KeysBackfilled;
        public int KeysTodo;
        public int KeysUnused;
        public List<string> Warnings = new();
    }
}
