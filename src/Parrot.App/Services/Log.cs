using System.IO;
using System.Text;
using Parrot.Core;

namespace Parrot.App.Services;

/// <summary>
/// Minimal append-only log. A background app that dies silently is impossible to debug,
/// and a full logging framework would be more setup than this needs.
/// </summary>
public static class Log
{
    private static readonly Lock Gate = new();

    public static string CurrentFile =>
        Path.Combine(AppPaths.LogDirectory, $"parrot-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.LogDirectory);
                File.AppendAllText(CurrentFile, $"{DateTime.Now:HH:mm:ss} [{level}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch (IOException)
        {
            // Logging must never be the reason the app falls over.
        }
    }
}
