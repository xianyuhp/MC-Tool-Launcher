using System.Windows;
using System.Windows.Input;
using MCTL.Core.Mc;

namespace MCTL.Views;

/// <summary>自动下载 / 安装 Java 的进度窗口，支持取消。</summary>
public partial class JavaDownloadWindow : Window
{
    private readonly int _major;
    private readonly CancellationTokenSource _cts = new();
    private bool _finished;

    /// <summary>安装成功后的 Java 信息。</summary>
    public JavaInfo Installed { get; private set; }

    public JavaDownloadWindow(int major)
    {
        InitializeComponent();
        _major = major;
        DetailText.Text = $"需要 Java {major}，将从 Adoptium 下载 Temurin JRE（Windows x64）" +
                          $"并解压到：{JavaInstaller.Root}";
        Loaded += async (_, _) => await InstallAsync();
        Closed += (_, _) => _cts.Cancel();
    }

    private async Task InstallAsync()
    {
        var progress = new Progress<(double Percent, string Message)>(p =>
        {
            Bar.Value = Math.Clamp(p.Percent, 0, 100);
            StatusText.Text = p.Message;
        });

        try
        {
            Installed = await JavaInstaller.InstallAsync(_major, progress, _cts.Token);
            _finished = true;
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消。";
            DialogResult = false;
        }
        catch (Exception ex)
        {
            Core.Logger.Warn("自动安装 Java 失败：" + ex.Message);
            MessageBox.Show(this, "自动安装 Java 失败：" + ex.Message, "Java 安装",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            DialogResult = false;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (!_finished) _cts.Cancel();
        Close();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
