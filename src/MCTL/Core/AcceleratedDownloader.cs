using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace MCTL.Core;

/// <summary>类似 Motrix 的分段多线程下载：把大文件切成多段并发下载后合并。</summary>
public static class AcceleratedDownloader
{
    /// <summary>低于该大小不用分段，收益很小。</summary>
    private const long MinimumSize = 4L * 1024 * 1024;

    /// <summary>
    /// 尝试分段下载。成功返回 true（文件已写入 targetPath）；服务器不支持 Range 或文件过小时返回 false。
    /// </summary>
    public static async Task<bool> TryDownloadAsync(HttpClient client, string url, string targetPath,
        IProgress<double> progress, CancellationToken ct)
    {
        if (!Settings.Current.MultiThreadDownload) return false;

        long total;
        try
        {
            using var probe = new HttpRequestMessage(HttpMethod.Get, url);
            probe.Headers.Range = new RangeHeaderValue(0, 0);
            using var response = await client.SendAsync(probe, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode != HttpStatusCode.PartialContent) return false;
            total = response.Content.Headers.ContentRange?.Length ?? -1;
            if (total < MinimumSize) return false;
        }
        catch
        {
            return false;
        }

        var threads = Math.Clamp(Settings.Current.DownloadThreads, 2, 16);
        Logger.Info($"分段下载：{threads} 线程，共 {total / 1024.0 / 1024.0:0.0} MB");
        var chunkSize = total / threads;
        var parts = new List<string>();
        long received = 0;
        progress?.Report(0);

        try
        {
            var tasks = new List<Task>(threads);
            for (var i = 0; i < threads; i++)
            {
                var index = i;
                var start = index * chunkSize;
                var end = index == threads - 1 ? total - 1 : start + chunkSize - 1;
                var partPath = targetPath + "." + index + ".part";
                parts.Add(partPath);

                tasks.Add(Task.Run(async () =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Range = new RangeHeaderValue(start, end);
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    response.EnsureSuccessStatusCode();

                    await using var source = await response.Content.ReadAsStreamAsync(ct);
                    await using var destination = new FileStream(partPath, FileMode.Create, FileAccess.Write,
                        FileShare.None, 81920, true);

                    var buffer = new byte[81920];
                    int read;
                    while ((read = await source.ReadAsync(buffer, ct)) > 0)
                    {
                        await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                        var now = Interlocked.Add(ref received, read);
                        progress?.Report(Math.Min(1.0, (double)now / total));
                    }
                }, ct));
            }

            await Task.WhenAll(tasks);

            await using (var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                foreach (var part in parts)
                {
                    await using var input = new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                    await input.CopyToAsync(output, ct);
                }
            }

            progress?.Report(1);
            return true;
        }
        finally
        {
            foreach (var part in parts)
            {
                try { if (File.Exists(part)) File.Delete(part); } catch { }
            }
        }
    }
}
