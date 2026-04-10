using System;
using System.IO;

namespace CoreCAD.Core
{
    /// <summary>
    /// Global Forensic Logging for CoreCAD.
    /// </summary>
    public static class Logger
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CoreCAD",
            "coreCAD_error.log"
        );

        /// <summary>
        /// Records an exception to the log file.
        /// </summary>
        /// <param name="ex">The exception to log.</param>
        public static void Write(Exception ex)
        {
            try
            {
                var directory = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}";
                File.AppendAllText(LogPath, logEntry);
            }
            catch
            {
                // Fallback to console if file logging fails
                System.Diagnostics.Debug.WriteLine($"CRITICAL: Logging failed. Original Error: {ex.Message}");
            }
        }
    }
}
