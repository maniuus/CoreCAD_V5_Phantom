using System;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using CoreCAD.Core;

namespace CoreCAD.Persistence
{
    public static class ProjectContext
    {
        public static string ProjectRoot => DetectProjectRoot();
        public static string DataFolder => ResolveDataFolder();

        private static string DetectProjectRoot()
        {
            // Try to get the active document first, but prepare for Phantom Mode (Database only)
            string fullPath = "";
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;

            if (doc != null)
            {
                fullPath = doc.Name;
            }
            else
            {
                // FALLBACK: If Headless, try to get from current database filename
                // This is critical for Brick 6 GlobalSync
                fullPath = HostApplicationServices.WorkingDatabase.Filename;
            }

            if (string.IsNullOrEmpty(fullPath)) return string.Empty;

            string currentPath = Path.GetDirectoryName(fullPath) ?? string.Empty;
            if (string.IsNullOrEmpty(currentPath)) return string.Empty;

            DirectoryInfo? dir = new DirectoryInfo(currentPath);
            while (dir != null)
            {
                // Look for .dst (Sheet Set Manager) ATAU CoreCAD_Master.json
                if (dir.GetFiles("*.dst").Length > 0 || dir.GetFiles("CoreCAD_Master.json").Length > 0)
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }

            // Fallback to the current drawing directory if no root anchor is found
            return currentPath;
        }

        private static string ResolveDataFolder()
        {
            string root = ProjectRoot;
            if (string.IsNullOrEmpty(root)) return string.Empty;

            string dataPath = Path.Combine(root, "_CoreCAD_Data");

            if (!Directory.Exists(dataPath))
            {
                try
                {
                    Directory.CreateDirectory(dataPath);
                }
                catch
                {
                    // Handle potential permission issues silently for now
                }
            }

            return dataPath;
        }

        public static string GetMasterJsonPath()
        {
            // Mencari CoreCAD_Master.json di root (atau folder induk)
            string root = ProjectRoot;
            if (string.IsNullOrEmpty(root)) return string.Empty;

            // Identitas Resmi: CoreCAD_Master.json
            return Path.Combine(root, "CoreCAD_Master.json");
        }
    }
}
