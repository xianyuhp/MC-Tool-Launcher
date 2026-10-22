using System.IO;
using System.Windows;

namespace AuroraLauncher.Core.Mc;

public static class GameRunner
{
    public static bool IsRunning { get; private set; }
    public static GameSession CurrentSession { get; private set; }
    public static event Action<bool> RunningChanged;

    /// <summary>主动结束当前游戏进程（含子进程）。</summary>
    public static void StopGame()
    {
        try
        {
            var session = CurrentSession;
            if (session?.Process != null && !session.Process.HasExited)
            {
                session.Process.Kill(true);
                Logger.Info("已手动结束游戏进程");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("结束游戏失败：" + ex.Message);
        }
    }

    public static async Task RunAsync(LaunchOptions options)
    {
        GameSession session;
        try
        {
            session = McLauncher.Start(options);
        }
        catch (Exception ex)
        {
            Logger.Error("启动游戏失败：" + ex);
            MessageBox.Show(ex.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CurrentSession = session;
        IsRunning = true;
        RunningChanged?.Invoke(true);
        try
        {
            await session.Process.WaitForExitAsync();
        }
        finally
        {
            IsRunning = false;
            CurrentSession = null;
            RunningChanged?.Invoke(false);
        }

        var exitCode = session.Process.ExitCode;
        var minutes = (DateTime.Now - session.StartedAt).TotalMinutes;
        Logger.Info($"游戏进程已退出：代码 {exitCode}，运行 {minutes:0.0} 分钟");

        if (exitCode != 0)
        {
            var window = new Views.CrashWindow(session, exitCode);
            window.ShowDialog();
        }
    }
}

public static class CrashArtifacts
{
    public static string CollectLauncherLog() => Logger.GetLauncherLog();

    public static string CollectGameLog(GameSession session)
    {
        var path = Path.Combine(session.GameDir, "logs", "latest.log");
        return File.Exists(path) ? SafeRead(path) : "（未找到 logs/latest.log）";
    }

    public static (string Path, string Content) CollectCrashReport(GameSession session)
    {
        try
        {
            var crashDir = Path.Combine(session.GameDir, "crash-reports");
            if (Directory.Exists(crashDir))
            {
                var file = Directory.EnumerateFiles(crashDir, "*.txt")
                    .OrderByDescending(File.GetLastWriteTime)
                    .FirstOrDefault();
                if (file != null) return (file, SafeRead(file));
            }

            var hsErr = Directory.EnumerateFiles(session.GameDir, "hs_err_pid*.log")
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();
            if (hsErr != null) return (hsErr, SafeRead(hsErr));
        }
        catch (Exception ex)
        {
            Logger.Warn("读取崩溃报告失败：" + ex.Message);
        }
        return ("", "（未找到 crash-reports 或 hs_err_pid 日志）");
    }

    private static string SafeRead(string path)
    {
        try { return File.ReadAllText(path); }
        catch (Exception ex) { return "（读取失败：" + ex.Message + "）"; }
    }
}
