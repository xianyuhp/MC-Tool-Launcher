using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace MCTL.Core;

public static class Logger
{
    private static readonly object Lock = new();
    private static readonly List<string> Circular = new();
    private static string _logFile;

    public static ObservableCollection<string> Lines { get; } = new();
    public static event Action<string> LineAdded;

    public static string LogFile
    {
        get
        {
            if (_logFile != null) return _logFile;
            Directory.CreateDirectory(Path.Combine(Settings.AppDir, "logs"));
            _logFile = Path.Combine(Settings.AppDir, "logs", $"launcher-{DateTime.Now:yyyyMMdd}.log");
            return _logFile;
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    public static void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}";
        lock (Lock)
        {
            Circular.Add(line);
            if (Circular.Count > 400) Circular.RemoveRange(0, Circular.Count - 400);
            try { File.AppendAllText(LogFile, line + Environment.NewLine, Encoding.UTF8); } catch { }
        }
        App.Current?.Dispatcher.BeginInvoke(() =>
        {
            Lines.Add(line);
            while (Lines.Count > 2000) Lines.RemoveAt(0);
            LineAdded?.Invoke(line);
        });
    }

    public static string GetRuntimeLog(int maxLines = 400)
    {
        lock (Lock)
            return string.Join(Environment.NewLine, Circular.TakeLast(maxLines));
    }

    public static string GetLauncherLog()
    {
        try { return File.Exists(LogFile) ? File.ReadAllText(LogFile) : GetRuntimeLog(); }
        catch { return GetRuntimeLog(); }
    }
}
