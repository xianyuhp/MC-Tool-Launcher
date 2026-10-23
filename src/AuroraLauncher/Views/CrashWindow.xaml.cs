using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using AuroraLauncher.Core;
using AuroraLauncher.Core.Diagnostics;
using AuroraLauncher.Core.Mc;

namespace AuroraLauncher.Views;

public partial class CrashWindow : Window
{
    private readonly GameSession _session;
    private readonly int _exitCode;
    private CrashAnalysis _analysis;

    public CrashWindow(GameSession session, int exitCode)
    {
        InitializeComponent();
        _session = session;
        _exitCode = exitCode;

        SubTitle.Text = $"版本 {session.VersionId} · 退出代码 {exitCode} · 运行 {(DateTime.Now - session.StartedAt).TotalMinutes:0.0} 分钟";
        var (reportPath, reportContent) = CrashArtifacts.CollectCrashReport(session);
        Preview.Text = "===== 崩溃前日志（末尾） =====\r\n" + session.GetPreCrashLog(120) +
                       "\r\n\r\n===== 崩溃报告 =====\r\n" +
                       (reportContent.Length > 6000 ? reportContent[..6000] : reportContent);
        if (string.IsNullOrEmpty(reportPath)) ChkReport.IsChecked = false;
        Logger.Info("检测到游戏异常退出，已弹出日志导出窗口");

        Loaded += async (_, _) => await AnalyzeAsync();
    }

    /// <summary>后台读取日志并生成崩溃分析，避免打开窗口时卡顿。</summary>
    private async Task AnalyzeAsync()
    {
        try
        {
            var session = _session;
            var exitCode = _exitCode;
            _analysis = await Task.Run(() =>
            {
                var report = CrashArtifacts.CollectCrashReport(session).Content;
                var gameLog = CrashArtifacts.CollectGameLog(session);
                var preCrash = session.GetPreCrashLog(200);
                var modsDir = Path.Combine(session.GameDir, "mods");
                return CrashAnalyzer.Analyze(exitCode, report, gameLog, preCrash, modsDir);
            });
            AnalysisText.Text = _analysis.ToDisplayText();
            Logger.Info("崩溃分析：" + _analysis.Summary);
        }
        catch (Exception ex)
        {
            AnalysisText.Text = "崩溃分析失败：" + ex.Message;
            Logger.Warn("崩溃分析失败：" + ex.Message);
        }
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenGameDir_Click(object sender, RoutedEventArgs e) =>
        OpenFolder(_session.GameDir);

    private void OpenModsDir_Click(object sender, RoutedEventArgs e) =>
        OpenFolder(Path.Combine(_session.GameDir, "mods"));

    private static void OpenFolder(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "打开失败"); }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = Path.Combine(_session.GameDir, "crash-export", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(folder);

            if (ChkLauncher.IsChecked == true)
                File.WriteAllText(Path.Combine(folder, "launcher.log"), CrashArtifacts.CollectLauncherLog());
            if (ChkPreCrash.IsChecked == true)
                File.WriteAllText(Path.Combine(folder, "pre-crash.log"), _session.GetPreCrashLog());
            if (ChkGame.IsChecked == true)
                File.WriteAllText(Path.Combine(folder, "game-latest.log"), CrashArtifacts.CollectGameLog(_session));
            if (ChkReport.IsChecked == true)
                File.WriteAllText(Path.Combine(folder, "crash-report.txt"), CrashArtifacts.CollectCrashReport(_session).Content);

            // 分析结论一并导出，方便发给别人时直接看懂
            File.WriteAllText(Path.Combine(folder, "analysis.txt"),
                _analysis?.ToDisplayText() ?? "（分析尚未完成）");

            var summary = $"退出代码：{_exitCode}\r\n版本：{_session.VersionId}\r\n" +
                          $"启动时间：{_session.StartedAt:yyyy-MM-dd HH:mm:ss}\r\n" +
                          $"导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n" +
                          $"\r\n可能原因：{_analysis?.Summary ?? "未完成分析"}\r\n";
            File.WriteAllText(Path.Combine(folder, "summary.txt"), summary);

            Status.Text = "已导出到：" + folder;
            Logger.Info("崩溃日志已导出：" + folder);
            Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Status.Text = "导出失败：" + ex.Message;
            Logger.Error("导出崩溃日志失败：" + ex.Message);
        }
    }
}
