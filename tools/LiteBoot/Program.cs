using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

/// <summary>
/// MC Tool Launcher 简洁版引导器（.NET Framework 4.8 编译，系统自带即可运行）。
/// 检测不到 .NET 10 桌面运行时会自动下载安装，然后释放并启动内置的启动器。
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // .NET Framework 默认可能仍用旧版 TLS，必须显式开启 TLS 1.2/1.3 才能访问微软下载站
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;
        ServicePointManager.Expect100Continue = false;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new BootForm());
    }
}

internal sealed class BootForm : Form
{
    private const string RuntimeUrl = "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe";
    private const string AppVersion = "1.32.2.0";

    /// <summary>仅用于自测：强制走「下载 + 安装运行时」流程。</summary>
    private static readonly bool ForceInstall =
        Environment.GetEnvironmentVariable("MCTL_TEST_FORCE_INSTALL") == "1";

    private readonly Label _status;
    private readonly ProgressBar _bar;

    public BootForm()
    {
        Text = "MC Tool Launcher 简洁版";
        ClientSize = new Size(470, 148);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(24, 26, 32);
        ForeColor = Color.Gainsboro;
        Font = new Font("Microsoft YaHei UI", 9f);

        _status = new Label
        {
            Left = 20,
            Top = 26,
            Width = 430,
            Height = 46,
            ForeColor = Color.Gainsboro,
            Text = "正在检查运行环境…"
        };

        _bar = new ProgressBar
        {
            Left = 20,
            Top = 84,
            Width = 430,
            Height = 12,
            Minimum = 0,
            Maximum = 100
        };

        Controls.Add(_status);
        Controls.Add(_bar);

        Shown += delegate
        {
            var worker = new Thread(Work) { IsBackground = true };
            worker.Start();
        };
    }

    /// <summary>检测 .NET 10 桌面运行时（x64）是否已安装。</summary>
    private static bool HasRuntime10()
    {
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                @"dotnet\shared\Microsoft.WindowsDesktop.App");
            if (!Directory.Exists(root)) return false;

            foreach (var dir in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith("10.", StringComparison.Ordinal)) return true;
            }
        }
        catch
        {
            // 读取失败时按未安装处理，让安装程序自行判断
        }
        return false;
    }

    private static int _lastLoggedBucket = -1;

    private void SetStatus(string text, int percent)
    {
        // 日志按 5% 分档，避免下载进度把日志刷爆
        var bucket = Math.Max(0, Math.Min(100, percent)) / 5;
        if (bucket != _lastLoggedBucket)
        {
            _lastLoggedBucket = bucket;
            Log(text);
        }

        if (IsDisposed) return;
        try
        {
            BeginInvoke((Action)delegate
            {
                _status.Text = text;
                _bar.Value = Math.Max(0, Math.Min(100, percent));
            });
        }
        catch
        {
        }
    }

    /// <summary>把引导过程写进日志，便于排查安装失败原因。</summary>
    private static void Log(string message)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "mctl-lite.log");
            File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
        }
        catch
        {
        }
    }

    private void Work()
    {
        try
        {
            Log("启动；已装 .NET 10 = " + HasRuntime10() + "，强制安装 = " + ForceInstall);
            if (ForceInstall || !HasRuntime10())
            {
                SetStatus("正在下载 .NET 10 桌面运行时（约 60 MB）…", 2);
                var installer = Path.Combine(Path.GetTempPath(), "windowsdesktop-runtime-10-win-x64.exe");
                DownloadRuntime(installer);
                Log("下载完成：" + new FileInfo(installer).Length + " 字节");

                SetStatus("正在安装 .NET 10 桌面运行时（如弹出权限提示请选择“是”）…", 86);
                var psi = new ProcessStartInfo(installer, "/install /quiet /norestart") { UseShellExecute = false };
                var process = Process.Start(psi);
                if (process != null) process.WaitForExit();
                Log("安装程序退出码：" + (process == null ? "null" : process.ExitCode.ToString()));
                try { File.Delete(installer); } catch { }

                if (!HasRuntime10())
                    throw new Exception("运行时安装未完成，请手动安装后重试。");
                Log("运行时安装成功");
            }

            SetStatus("正在启动 MC Tool Launcher…", 96);
            var exe = ExtractPayload();
            Log("已释放启动器：" + exe);
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
            Log("已启动");
            BeginInvoke((Action)Close);
        }
        catch (Exception ex)
        {
            Log("失败：" + ex);
            BeginInvoke((Action)delegate
            {
                MessageBox.Show(this,
                    "启动失败：" + ex.Message + "\r\n\r\n可手动安装 .NET 10 桌面运行时后重试：\r\n" + RuntimeUrl,
                    "MC Tool Launcher 简洁版", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Close();
            });
        }
    }

    private void DownloadRuntime(string target)
    {
        using (var client = new WebClient())
        {
            var done = new ManualResetEvent(false);
            Exception error = null;

            client.DownloadProgressChanged += delegate(object sender, DownloadProgressChangedEventArgs e)
            {
                SetStatus("正在下载 .NET 10 桌面运行时… " + e.ProgressPercentage + "%",
                    e.ProgressPercentage * 80 / 100);
            };
            client.DownloadFileCompleted += delegate(object sender, AsyncCompletedEventArgs e)
            {
                error = e.Error;
                done.Set();
            };

            client.DownloadFileAsync(new Uri(RuntimeUrl), target);
            done.WaitOne();

            if (error != null) throw error;
        }
    }

    /// <summary>把内置的启动器释放到引导器同级目录（不可写时退回 LocalAppData）。</summary>
    private static string ExtractPayload()
    {
        var target = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MCTL.exe");
        try
        {
            WritePayload(target);
            return target;
        }
        catch
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"MCTL\app");
            Directory.CreateDirectory(dir);
            target = Path.Combine(dir, "MCTL.exe");
            WritePayload(target);
            return target;
        }
    }

    private static void WritePayload(string target)
    {
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MCTL.exe"))
        {
            if (stream == null) throw new Exception("内置启动器缺失。");

            if (File.Exists(target) && new FileInfo(target).Length == stream.Length &&
                FileVersionInfo.GetVersionInfo(target).FileVersion == AppVersion) return;

            try { File.Delete(target); } catch { }
            using (var file = File.Create(target))
                stream.CopyTo(file);
        }
    }
}
