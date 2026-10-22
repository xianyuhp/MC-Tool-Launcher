using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using AuroraLauncher.Core;
using AuroraLauncher.Core.Mc;

namespace AuroraLauncher.Views;

public partial class CrashWindow : Window
{
    private readonly GameSession _session;
    private readonly int _exitCode;
    private string _lastExportFolder;

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
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenGameDir_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", _session.GameDir) { UseShellExecute = true }); }
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

            var summary = $"退出代码：{_exitCode}\r\n版本：{_session.VersionId}\r\n" +
                          $"启动时间：{_session.StartedAt:yyyy-MM-dd HH:mm:ss}\r\n" +
                          $"导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n";
            File.WriteAllText(Path.Combine(folder, "summary.txt"), summary);

            _lastExportFolder = folder;
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
