using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using System;
using System.IO;

namespace CoreCAD.Core.Diagnostics
{
    public static class DebugLogger
    {
        private static string _logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoreCAD", "debug.log");

        static DebugLogger()
        {
            try
            {
                string? dir = Path.GetDirectoryName(_logPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch { }
        }

        public static void Log(string message, string category = "INFO")
        {
            string formattedMessage = $"[{DateTime.Now:HH:mm:ss}] [{category}] {message}";
            
            // 1. Output to AutoCAD Command Line
            try
            {
                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    Editor ed = doc.Editor;
                    ed.WriteMessage($"\n[coreCAD DEBUG] {formattedMessage}");
                }
            }
            catch { }

            // 2. Output to File
            try
            {
                File.AppendAllText(_logPath, formattedMessage + Environment.NewLine);
            }
            catch { }
        }

        public static void Error(string message, Exception ex)
        {
            string detail = $"{message}\nException: {ex.Message}\nStack Trace: {ex.StackTrace}";
            Log(detail, "ERROR");
        }
    }
}
