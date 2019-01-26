using System;
using System.IO;
using JetBrains.Annotations;

namespace BattleTechModLoader
{
    internal static class Logger
    {
        internal static string LogPath { get; set; }

        internal static void LogException(string message, Exception e)
        {
            using (var logWriter = File.AppendText(LogPath))
            {
                logWriter.WriteLine(message);
                logWriter.WriteLine(e.ToString());
            }
        }

        [StringFormatMethod("message")]
        internal static void Log(string message, params object[] formatObjects)
        {
            if (string.IsNullOrEmpty(LogPath)) return;
            using (var logWriter = File.AppendText(LogPath))
            {
                logWriter.WriteLine(message, formatObjects);
            }
        }

        [StringFormatMethod("message")]
        internal static void LogWithDate(string message, params object[] formatObjects)
        {
            if (string.IsNullOrEmpty(LogPath)) return;
            using (var logWriter = File.AppendText(LogPath))
            {
                logWriter.WriteLine(DateTime.Now.ToLongTimeString() + " - " + message, formatObjects);
            }
        }
    }
}