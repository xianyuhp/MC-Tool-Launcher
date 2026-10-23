using System.IO;
using System.Windows;
using System.Windows.Input;
using MCTL.Core;
using MCTL.Core.Mc;

namespace MCTL.Views;

/// <summary>整合包导入窗口：显示解压 / 下载进度。</summary>
public partial class ModpackImportWindow : Window
{
    private readonly string _file;
    private readonly Action<string> _onImported;
    private CancellationTokenSource _cts;
    private bool _running;

    public ModpackImportWindow(string file, Action<string> onImported)
    {
        InitializeComponent();
        _file = file;
        _onImported = onImported;
        FileText.Text = file;
        StatusText.Text = $"将安装到 {Settings.Current.EffectiveGameDir}，完成后出现在游戏列表中。";

        // 拖入后自动开始导入
        Loaded += async (_, _) =>
        {
            await Task.Delay(250);
            if (!_running) Start_Click(this, new RoutedEventArgs());
        };
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        _running = true;
        StartBtn.IsEnabled = false;
        CancelBtn.Content = "取消导入";
        ProgressHost.Visibility = Visibility.Visible;
        LogBox.Clear();
        _cts = new CancellationTokenSource();

        var log = new Progress<string>(s => Dispatcher.BeginInvoke(() =>
        {
            LogBox.AppendText(s + Environment.NewLine);
            LogBox.ScrollToEnd();
        }));
        var progress = new Progress<double>(p =>
        {
            Bar.Value = p * 100;
            PercentText.Text = $"{p * 100:0}%";
        });

        try
        {
            var name = await ModpackService.ImportAsync(_file, Settings.Current.EffectiveGameDir,
                log, progress, _cts.Token);
            StatusText.Text = "导入完成：" + name;
            _onImported?.Invoke(name);
            StartBtn.Content = "完成";
            StartBtn.IsEnabled = true;
            StartBtn.Click -= Start_Click;
            StartBtn.Click += (_, _) => Close();
            _running = false;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消导入。";
            ProgressHost.Visibility = Visibility.Collapsed;
            StartBtn.IsEnabled = true;
            _running = false;
        }
        catch (Exception ex)
        {
            StatusText.Text = "导入失败：" + ex.Message;
            Logger.Warn("整合包导入失败：" + ex);
            StartBtn.IsEnabled = true;
            _running = false;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_running)
        {
            _cts?.Cancel();
            return;
        }
        Close();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_running) return;
        _cts?.Cancel();
    }
}
