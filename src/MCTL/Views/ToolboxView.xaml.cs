using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using MCTL.Core;
using MCTL.Core.Mc;

namespace MCTL.Views;

/// <summary>工具箱：当前版本的常用目录、存档备份与维护工具。</summary>
public partial class ToolboxView : UserControl, IRefreshable
{
    public ToolboxView()
    {
        InitializeComponent();
    }

    public void Refresh()
    {
        var version = string.IsNullOrEmpty(InstancePaths.VersionId) ? "未选择版本" : InstancePaths.VersionId;
        InstanceHint.Text = $"{version} → {InstancePaths.InstanceDir}";
        ResultText.Text = "当前版本目录：" + InstancePaths.InstanceDir;
    }

    private void Log(string message)
    {
        OutputBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        OutputBox.ScrollToEnd();
        ResultText.Text = message;
        Logger.Info("[工具箱] " + message);
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
            Log("已打开：" + path);
        }
        catch (Exception ex)
        {
            Log("打开失败：" + ex.Message);
        }
    }

    private void OpenGameDir_Click(object sender, RoutedEventArgs e) => OpenFolder(InstancePaths.Ensure(InstancePaths.InstanceDir));
    private void OpenMods_Click(object sender, RoutedEventArgs e) => OpenFolder(InstancePaths.Ensure(InstancePaths.Mods));
    private void OpenResourcePacks_Click(object sender, RoutedEventArgs e) => OpenFolder(InstancePaths.Ensure(InstancePaths.ResourcePacks));
    private void OpenShaderPacks_Click(object sender, RoutedEventArgs e) => OpenFolder(InstancePaths.Ensure(InstancePaths.ShaderPacks));
    private void OpenSaves_Click(object sender, RoutedEventArgs e) => OpenFolder(InstancePaths.Ensure(InstancePaths.Saves));
    private void OpenConfig_Click(object sender, RoutedEventArgs e) => OpenFolder(Settings.ConfigDir);
    private void OpenImages_Click(object sender, RoutedEventArgs e) => OpenFolder(Settings.ImagesDir);
    private void OpenAudio_Click(object sender, RoutedEventArgs e) => OpenFolder(Settings.AudioDir);
    private void OpenInstallers_Click(object sender, RoutedEventArgs e) => OpenFolder(Path.Combine(InstancePaths.Root, "installers"));
    private void OpenCrashReports_Click(object sender, RoutedEventArgs e) => OpenFolder(InstancePaths.Ensure(InstancePaths.CrashReports));

    /// <summary>备份目录：游戏根目录/backup/&lt;版本&gt;。</summary>
    private string BackupDir
    {
        get
        {
            var version = string.IsNullOrEmpty(InstancePaths.VersionId) ? "default" : InstancePaths.VersionId;
            return Path.Combine(InstancePaths.Root, "backup", version);
        }
    }

    private void BackupSaves_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var saves = InstancePaths.Ensure(InstancePaths.Saves);
            var folders = Directory.GetDirectories(saves);
            if (folders.Length == 0)
            {
                MessageBox.Show("当前版本还没有任何存档。", "备份存档");
                OpenFolder(saves);
                return;
            }

            if (MessageBox.Show($"将备份 {folders.Length} 个存档到：\n{BackupDir}\n\n是否继续？", "备份存档",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                OpenFolder(saves);
                return;
            }

            Directory.CreateDirectory(BackupDir);
            var target = Path.Combine(BackupDir, $"saves-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            ZipFile.CreateFromDirectory(saves, target, CompressionLevel.Optimal, false);
            Log("存档已备份：" + target);
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + target + "\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log("备份失败：" + ex.Message);
        }
    }

    /// <summary>列出备份并恢复到 saves 目录。</summary>
    private void RestoreSaves_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Directory.Exists(BackupDir))
            {
                MessageBox.Show("当前版本还没有备份存档。", "使用备份存档");
                return;
            }
            var backups = Directory.GetFiles(BackupDir, "*.zip")
                .OrderByDescending(f => f)
                .ToList();
            if (backups.Count == 0)
            {
                MessageBox.Show("当前版本还没有备份存档。", "使用备份存档");
                return;
            }

            var picker = new LoaderPickerWindow("使用备份存档", "选择要恢复的备份（会解压到当前版本的 saves）",
                backups.Select(f => (f, Path.GetFileName(f))))
            {
                Owner = Window.GetWindow(this)
            };
            if (picker.ShowDialog() != true) return;

            var saves = InstancePaths.Ensure(InstancePaths.Saves);
            ZipFile.ExtractToDirectory(picker.SelectedVersion, saves, true);
            Log("已恢复备份：" + picker.SelectedVersion);
            MessageBox.Show("备份已恢复到：\n" + saves, "使用备份存档");
        }
        catch (Exception ex)
        {
            Log("恢复失败：" + ex.Message);
            MessageBox.Show("恢复失败：" + ex.Message, "使用备份存档", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CleanLogs_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("确定删除游戏日志与崩溃报告吗？\n（不会删除存档）", "清理日志",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            var logs = Path.Combine(InstancePaths.InstanceDir, "logs");
            var crashes = InstancePaths.CrashReports;
            var count = 0;
            if (Directory.Exists(logs))
                foreach (var file in Directory.GetFiles(logs, "*.log", SearchOption.AllDirectories))
                {
                    File.Delete(file);
                    count++;
                }
            if (Directory.Exists(crashes))
                foreach (var file in Directory.GetFiles(crashes, "*.txt", SearchOption.AllDirectories))
                {
                    File.Delete(file);
                    count++;
                }
            Log($"已清理 {count} 个日志文件。");
        }
        catch (Exception ex)
        {
            Log("清理失败：" + ex.Message);
        }
    }

    private void DetectJava_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var javas = JavaFinder.FindAll(true);
            if (javas.Count == 0)
            {
                Log("未检测到 Java，请在设置中手动指定。");
                return;
            }
            OutputBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 检测到 {javas.Count} 个 Java：{Environment.NewLine}");
            foreach (var java in javas) OutputBox.AppendText("    " + java + Environment.NewLine);
            ResultText.Text = $"检测到 {javas.Count} 个 Java，默认使用 {javas.OrderBy(j => j.Major).FirstOrDefault(j => j.Major >= 17) ?? javas[0]}";
        }
        catch (Exception ex)
        {
            Log("检测失败：" + ex.Message);
        }
    }

    private void ExportLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var target = Path.Combine(Settings.ConfigDir, $"launcher-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(target, Logger.GetLauncherLog());
            Log("启动器日志已导出：" + target);
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + target + "\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log("导出失败：" + ex.Message);
        }
    }
}
