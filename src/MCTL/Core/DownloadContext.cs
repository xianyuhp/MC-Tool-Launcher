using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace MCTL.Core;

/// <summary>一次下载/安装任务的实时状态，供进度条与「详情」窗口共用。</summary>
public class DownloadContext : INotifyPropertyChanged
{
    public string Title { get; }
    public ObservableCollection<string> Log { get; } = new();

    private string _status = "准备中…";
    public string Status
    {
        get => _status;
        set { _status = value; Notify(nameof(Status)); }
    }

    private double _progress;
    public double Progress
    {
        get => _progress;
        set { _progress = value; Notify(nameof(Progress)); }
    }

    private string _currentFile = "";
    public string CurrentFile
    {
        get => _currentFile;
        set { _currentFile = value; Notify(nameof(CurrentFile)); }
    }

    public string StartedAt { get; } = DateTime.Now.ToString("HH:mm:ss");

    public DownloadContext(string title) => Title = title;

    public event PropertyChangedEventHandler PropertyChanged;

    private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    public void Report(string message) => OnUi(() =>
    {
        Status = message;
        Log.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        while (Log.Count > 800) Log.RemoveAt(0);
    });

    public void ReportProgress(double value) => OnUi(() => Progress = Math.Clamp(value * 100, 0, 100));

    public void ReportFile(string file) => OnUi(() => CurrentFile = file);

    public void Complete(string message)
    {
        OnUi(() =>
        {
            Progress = 100;
            Status = message;
            Log.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        });
    }

    public void Fail(string message)
    {
        OnUi(() =>
        {
            Status = "失败：" + message;
            Log.Add($"[{DateTime.Now:HH:mm:ss}] 失败：{message}");
        });
    }
}
