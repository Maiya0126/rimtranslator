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
