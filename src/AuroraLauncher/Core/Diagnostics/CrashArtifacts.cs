using System.IO;
using AuroraLauncher.Core.Mc;

namespace AuroraLauncher.Core.Diagnostics;

/// <summary>收集崩溃相关日志：启动器日志、游戏 latest.log、crash-report、hs_err_pid。</summary>
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
