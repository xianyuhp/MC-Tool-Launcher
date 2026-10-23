namespace MCTL.Core.Net;

/// <summary>
/// 下载进度节流：把高频的分块回调压到约每 100ms 一次，
/// 避免大文件下载时成千上万次界面更新把 UI 线程堵住。
/// </summary>
public sealed class ThrottledProgress : IProgress<double>
{
    private readonly IProgress<double> _inner;
    private readonly int _intervalMs;
    private long _lastTicks;

    public ThrottledProgress(IProgress<double> inner, int intervalMs = 100)
    {
        _inner = inner;
        _intervalMs = intervalMs;
    }

    public void Report(double value)
    {
        // 100% 必须上报，其余按时间间隔丢弃
        if (value < 1)
        {
            var now = Environment.TickCount64;
            var previous = Interlocked.Read(ref _lastTicks);
            if (now - previous < _intervalMs) return;
            if (Interlocked.CompareExchange(ref _lastTicks, now, previous) != previous) return;
        }
        _inner.Report(value);
    }
}
