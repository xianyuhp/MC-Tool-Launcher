using System.Windows;
using System.Windows.Input;
using MCTL.Core;
using MCTL.Core.Mc;

namespace MCTL.Views;

/// <summary>服务端后台窗口：实时输出、命令输入、停止与重启。</summary>
public partial class ServerConsoleWindow : Window
{
    private readonly ServerRunner _runner;
    private bool _closing;
    private readonly Queue<string> _pending = new();
    private readonly System.Windows.Threading.DispatcherTimer _flushTimer;

    public ServerConsoleWindow(ServerRunner runner)
    {
        InitializeComponent();
        _runner = runner;
        TitleText.Text = "服务端后台 · " + runner.Instance.Name;
        InfoText.Text = $"{runner.Instance.Dir}　·　内存 {runner.MemoryMb} MB" +
                        (runner.RestartMinutes > 0 ? $"　·　每 {runner.RestartMinutes} 分钟重启" : "");

        // 高频日志先入队，定时批量刷新，避免大量 AppendText 把界面卡住
        _flushTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _flushTimer.Tick += (_, _) => FlushPending();
        _flushTimer.Start();

        _runner.Output += OnOutput;
        _runner.RunningChanged += OnRunningChanged;
        Closed += (_, _) =>
        {
            _flushTimer.Stop();
            _runner.Output -= OnOutput;
            _runner.RunningChanged -= OnRunningChanged;
        };
    }

    private void FlushPending()
    {
        if (_pending.Count == 0) return;
        var builder = new System.Text.StringBuilder();
        lock (_pending)
        {
            while (_pending.Count > 0) builder.AppendLine(_pending.Dequeue());
        }
        OutputBox.AppendText(builder.ToString());
        if (OutputBox.Text.Length > 300_000)
            OutputBox.Text = OutputBox.Text[^150_000..];
        OutputBox.ScrollToEnd();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (!_runner.IsRunning) _runner.Start();
        UpdateState(_runner.IsRunning);
        CommandBox.Focus();
    }

    private void OnOutput(string line)
    {
        lock (_pending)
        {
            _pending.Enqueue(line);
            while (_pending.Count > 2000) _pending.Dequeue();
        }
    }

    private void OnRunningChanged(bool running) => Dispatcher.BeginInvoke(() => UpdateState(running));

    private void UpdateState(bool running)
    {
        StateText.Text = running ? "运行中" : "未运行";
        StateText.Foreground = running ? (System.Windows.Media.Brush)FindResource("AccentBrush")
                                       : (System.Windows.Media.Brush)FindResource("SubTextBrush");
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void Send_Click(object sender, RoutedEventArgs e) => SendCommand();

    private void CommandBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SendCommand();
    }

    private void SendCommand()
    {
        var command = (CommandBox.Text ?? "").Trim();
        if (command.Length == 0) return;
        _runner.Send(command);
        CommandBox.Clear();
        CommandBox.Focus();
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        OutputBox.AppendText("正在停止服务端…" + Environment.NewLine);
        await _runner.StopAsync();
        UpdateState(_runner.IsRunning);
    }

    private async void Restart_Click(object sender, RoutedEventArgs e)
    {
        OutputBox.AppendText("正在重启服务端…" + Environment.NewLine);
        await _runner.StopAsync();
        await Task.Delay(800);
        _runner.Start();
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => OutputBox.Clear();

    private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closing || !_runner.IsRunning) return;
        e.Cancel = true;
        _closing = true;
        await _runner.StopAsync(8);
        _closing = false;
        Close();
    }
}
