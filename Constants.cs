using System;
using System.IO;

namespace AlKesser
{
    internal static class Constants
    {
        public static string ExeDir { get; private set; }
        public static string APIHeaderFile { get; private set; }
        public static string MainCppPath { get; private set; }
        public static string Bin2ShellScript { get; private set; }
        public static string Bin2ShellAlgos { get; private set; }
        public static string MainCppFile { get; private set; }
        public static string TemplateCppFile { get; private set; }

        // Static constructor runs once
        static Constants()
        {
            try
            {
                ExeDir = AppDomain.CurrentDomain.BaseDirectory;
                Logger.Info($"Executable base directory: {ExeDir}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to get executable base directory: {ex}");
                throw;
            }

            // APIHeaderFile
            APIHeaderFile = Path.Combine(ExeDir, "VX-API-main", "VX-API", "Win32Helper.h");
            EnsureExists(APIHeaderFile, isFile: true, nameof(APIHeaderFile));

            // MainCppPath (directory)
            MainCppPath = Path.Combine(ExeDir, "VX-API-main", "VX-API");
            EnsureExists(MainCppPath, isFile: false, nameof(MainCppPath));

            // Files inside MainCppPath
            MainCppFile = Path.Combine(MainCppPath, "main.cpp");
            EnsureExists(MainCppFile, isFile: true, nameof(MainCppFile));

            TemplateCppFile = Path.Combine(MainCppPath, "template.cpp");
            EnsureExists(TemplateCppFile, isFile: true, nameof(TemplateCppFile));

            // Bin2Shell files
            Bin2ShellScript = Path.Combine(ExeDir, "Tools", "Bin2Shell", "main.py");
            EnsureExists(Bin2ShellScript, isFile: true, nameof(Bin2ShellScript));

            Bin2ShellAlgos = Path.Combine(ExeDir, "Tools", "Bin2Shell", "algos.yaml");
            EnsureExists(Bin2ShellAlgos, isFile: true, nameof(Bin2ShellAlgos));

            Logger.Ok("All constants initialized successfully.");
        }

        private static void EnsureExists(string path, bool isFile, string name)
        {
            if (isFile)
            {
                if (!File.Exists(path))
                {
                    Logger.Error($"{name} not found at path: {path}");
                    throw new FileNotFoundException($"{name} not found.", path);
                }
                Logger.Ok($"{name} verified: {path}");
            }
            else
            {
                if (!Directory.Exists(path))
                {
                    Logger.Error($"{name} directory not found at path: {path}");
                    throw new DirectoryNotFoundException($"{name} directory not found: {path}");
                }
                Logger.Ok($"{name} directory verified: {path}");
            }
        }
    }
}
