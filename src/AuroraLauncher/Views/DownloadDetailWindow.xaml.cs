using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using AuroraLauncher.Core;

namespace AuroraLauncher.Views;

public partial class DownloadDetailWindow : Window
{
    private readonly DownloadContext _context;
    private readonly string _gameDir;
    private readonly Action _cancel;

    public DownloadDetailWindow(DownloadContext context, string gameDir, Action cancel = null)
    {
        InitializeComponent();
        _context = context;
        _gameDir = gameDir;
        _cancel = cancel;

        TitleText.Text = context.Title;
        StartedText.Text = "开始时间 " + context.StartedAt;
        StatusText.Text = context.Status;
        FileText.Text = context.CurrentFile;
        Bar.Value = context.Progress;
        LogList.ItemsSource = context.Log;

        context.PropertyChanged += (_, e) => Dispatcher.BeginInvoke(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(DownloadContext.Status): StatusText.Text = context.Status; break;
                case nameof(DownloadContext.Progress): Bar.Value = context.Progress; break;
                case nameof(DownloadContext.CurrentFile): FileText.Text = context.CurrentFile; break;
            }
        });

        context.Log.CollectionChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[^1]);
        });
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_gameDir);
            Process.Start(new ProcessStartInfo("explorer.exe", _gameDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "打开失败");
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cancel?.Invoke();
        _context.Report("已请求取消任务…");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
