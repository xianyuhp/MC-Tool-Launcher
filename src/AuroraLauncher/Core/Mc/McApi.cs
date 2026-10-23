using System.IO;
using System.Net;
using System.Net.Http;
using AuroraLauncher.Core.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace AuroraLauncher.Core.Mc;

public record McVersion(string Id, string Type, string Url, DateTime ReleaseTime);
public record FabricLoader(string Version, bool Stable);

public static class McApi
{
    public static readonly HttpClient Client = CreateClient();

    /// <summary>当前版本清单是否来自镜像。</summary>
    public static bool UsingMirror { get; private set; }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            // 复用连接但定期重建，避免 CDN 边缘节点断开后仍复用死连接
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            // HTTP/2 下允许多条并发连接，提升多文件同时下载的吞吐
            EnableMultipleHttp2Connections = true,
            // 并发拨号：DNS 返回的多个 IP 里有坏地址时也能快速连上（修复 cdn.modrinth.com 超时）
            ConnectCallback = HttpConnection.ConnectAsync
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        // 使用常见浏览器 UA / Accept，避免部分 CDN 直接拦截非浏览器请求
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8");
        return client;
    }

    private static List<McVersion> _versionCache;

    /// <summary>切换下载源后清空版本清单缓存。</summary>
    public static void ResetCache() => _versionCache = null;

    private static async Task<string> GetStringAsync(string url, int timeoutSeconds)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        return await Client.GetStringAsync(url, cts.Token);
    }

    public static async Task<List<McVersion>> GetVersionsAsync(bool force = false)
    {
        if (_versionCache != null && !force) return _versionCache;

        var sources = Mirror.PreferMirror ? new[] { true, false } : new[] { false };
        Exception last = null;
        foreach (var useMirror in sources)
        {
            try
            {
                var json = await GetStringAsync(Mirror.ManifestUrl(useMirror), 30);
                using var doc = JsonDocument.Parse(json);
                var list = new List<McVersion>();
                foreach (var v in doc.RootElement.GetProperty("versions").EnumerateArray())
                {
                    list.Add(new McVersion(
                        v.GetProperty("id").GetString(),
                        v.GetProperty("type").GetString(),
                        v.GetProperty("url").GetString(),
                        DateTime.TryParse(v.GetProperty("releaseTime").GetString(), out var t) ? t : DateTime.MinValue));
                }
                _versionCache = list;
                UsingMirror = useMirror;
                Logger.Info($"版本清单来源：{(useMirror ? "BMCLAPI 镜像" : "Mojang 官方")}，共 {list.Count} 个版本");
                return list;
            }
            catch (Exception ex)
            {
                last = ex;
                // 镜像失败会自动回退官方源，属于正常流程，不当作警告刷屏
                if (useMirror) Logger.Info($"镜像版本清单不可用，尝试官方源：{ex.Message}");
                else Logger.Warn($"官方版本清单获取失败：{ex.Message}");
            }
        }
        throw new Exception("版本清单获取失败：" + last?.Message);
    }

    public static async Task<string> GetVersionJsonAsync(McVersion version)
    {
        if (Mirror.PreferMirror)
        {
            try { return await GetStringAsync(Mirror.VersionJsonUrl(version.Id), 30); }
            catch (Exception ex) { Logger.Warn($"镜像版本 JSON 失败，回退官方：{ex.Message}"); }
        }
        return await GetStringAsync(version.Url, 40);
    }

    public static async Task<List<FabricLoader>> GetFabricLoadersAsync(string mcId)
    {
        string json = null;
        if (Mirror.PreferMirror)
        {
            try { json = await GetStringAsync(Mirror.FabricLoadersUrl(true, mcId), 20); }
            catch (Exception ex) { Logger.Warn("Fabric 镜像 meta 失败，回退官方：" + ex.Message); }
        }
        json ??= await GetStringAsync(Mirror.FabricLoadersUrl(false, mcId), 30);

        using var doc = JsonDocument.Parse(json);
        var list = new List<FabricLoader>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var loader = item.GetProperty("loader");
            list.Add(new FabricLoader(loader.GetProperty("version").GetString(),
                loader.TryGetProperty("stable", out var s) && s.GetBoolean()));
        }
        return list;
    }

    public static async Task<string> GetFabricProfileAsync(string mcId, string loaderVersion)
    {
        if (Mirror.PreferMirror)
        {
            try { return await GetStringAsync(Mirror.FabricProfileUrl(true, mcId, loaderVersion), 25); }
            catch (Exception ex) { Logger.Warn("Fabric 镜像 profile 失败，回退官方：" + ex.Message); }
        }
        return await GetStringAsync(Mirror.FabricProfileUrl(false, mcId, loaderVersion), 30);
    }

    public static async Task<JsonDocument> SearchModrinthAsync(string query, string projectType, int limit = 20,
        string index = "relevance", int offset = 0, string category = null, string gameVersion = null,
        string loader = null)
    {
        var parts = new List<string> { $"[\"project_type:{projectType}\"]" };
        if (!string.IsNullOrEmpty(category)) parts.Add($"[\"categories:{category}\"]");
        if (!string.IsNullOrEmpty(gameVersion)) parts.Add($"[\"versions:{gameVersion}\"]");
        if (!string.IsNullOrEmpty(loader)) parts.Add($"[\"categories:{loader}\"]");
        var facets = Uri.EscapeDataString("[" + string.Join(",", parts) + "]");
        var url = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query ?? "")}&limit={limit}" +
                  $"&offset={Math.Max(0, offset)}&index={index}&facets={facets}";
        var json = await GetStringAsync(url, 30);
        return JsonDocument.Parse(json);
    }

    public static async Task<JsonDocument> GetModrinthVersionsAsync(string projectId, string gameVersion, string loader = null)
    {
        var url = $"https://api.modrinth.com/v2/project/{projectId}/version";
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(gameVersion)) parts.Add($"game_versions={Uri.EscapeDataString($"[\"{gameVersion}\"]")}");
        if (!string.IsNullOrEmpty(loader)) parts.Add($"loaders={Uri.EscapeDataString($"[\"{loader}\"]")}");
        if (parts.Count > 0) url += "?" + string.Join("&", parts);
        var json = await GetStringAsync(url, 30);
        return JsonDocument.Parse(json);
    }

    /// <summary>按版本 ID 获取单个 Modrinth 版本（用于前置依赖）。</summary>
    public static async Task<JsonDocument> GetModrinthVersionAsync(string versionId)
    {
        var json = await GetStringAsync($"https://api.modrinth.com/v2/version/{versionId}", 30);
        return JsonDocument.Parse(json);
    }

    public static string Sha1File(string path)
    {
        using var sha = SHA1.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    /// <summary>下载文件并校验 SHA1（若提供）。返回是否实际下载。</summary>
    public static Task<bool> DownloadAsync(string url, string path, string sha1, CancellationToken ct, string fallbackUrl = null)
        => DownloadAsyncCore(url, path, sha1, ct, null, fallbackUrl);

    /// <summary>带单文件进度（0-1）的下载。</summary>
    public static Task<bool> DownloadAsync(string url, string path, string sha1, CancellationToken ct,
        IProgress<double> progress, string fallbackUrl = null)
        => DownloadAsyncCore(url, path, sha1, ct, progress, fallbackUrl);

    private static async Task<bool> DownloadAsyncCore(string url, string path, string sha1, CancellationToken ct,
        IProgress<double> progress, string fallbackUrl)
    {
        var extra = string.IsNullOrEmpty(fallbackUrl) ? Array.Empty<string>() : new[] { fallbackUrl };
        Exception last = null;
        Exception definitive = null;

        // 官方源 + Maven 镜像，每个地址最多重试 3 次（指数退避）
        foreach (var candidate in ResilientHttp.Candidates(url, extra))
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    return await DownloadOnceAsync(candidate, path, sha1, ct, progress);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException ex)
                {
                    last = ex;
                }
                catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
                {
                    // 4xx 说明地址本身不存在，换下一个源，不再重试
                    definitive = ex;
                    last = ex;
                    Logger.Warn($"下载失败（{candidate}）：{ex.Message}");
                    break;
                }
                catch (Exception ex)
                {
                    last = ex;
                }

                Logger.Warn($"下载失败，准备重试（{attempt}/3）：{candidate} → {last?.Message}");
                // 连不上就不要再等同一域名，直接换镜像
                if (last != null && ResilientHttp.IsConnectionFailure(last)) break;
                if (attempt < 3) await ResilientHttp.RetryDelayAsync(attempt, ct);
            }
        }

        throw new DownloadUnavailableException(url, definitive ?? last);
    }

    private static async Task<bool> DownloadOnceAsync(string url, string path, string sha1, CancellationToken ct,
        IProgress<double> progress)
    {
        // 分块进度先节流，避免大文件下载时刷爆界面线程
        var report = progress == null ? null : new ThrottledProgress(progress);

        // 已存在且校验通过就直接跳过；SHA1 计算放到后台线程，避免大文件卡住界面
        var existsAndValid = false;
        if (File.Exists(path))
        {
            if (string.IsNullOrEmpty(sha1)) existsAndValid = true;
            else
            {
                var hash = await Task.Run(() => Sha1File(path), ct);
                existsAndValid = hash.Equals(sha1, StringComparison.OrdinalIgnoreCase);
            }
        }
        if (existsAndValid)
        {
            progress?.Report(1);
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".part";
        try
        {
            // 支持 Range 的服务器启用分段多线程下载（类似 Motrix）
            if (report != null && await AcceleratedDownloader.TryDownloadAsync(Client, url, tmp, report, ct))
            {
                File.Move(tmp, path, true);
                progress.Report(1);
                return true;
            }

            using (var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? -1;
                report?.Report(0);
                await using var fs = File.Create(tmp);
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, n), ct);
                    read += n;
                    if (total > 0) report?.Report(Math.Min(1.0, (double)read / total));
                }
            }
            File.Move(tmp, path, true);
            progress?.Report(1);
            return true;
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    /// <summary>
    /// 并发下载多个文件（默认 16 路，可在设置里调），镜像失败自动回退官方。
    /// 进度按 120ms 节流上报，避免几千个资源文件把界面线程刷爆。
    /// </summary>
    public static async Task DownloadManyAsync(IEnumerable<(string Url, string Path, string Sha1)> files,
        Action<int, int> progress, CancellationToken ct)
    {
        var list = files
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        var total = list.Count;
        var done = 0;
        var concurrency = Math.Clamp(Settings.Current.DownloadThreads, 4, 16);
        using var semaphore = new SemaphoreSlim(concurrency);

        // 节流：同一时间只让一个线程触发界面更新
        long lastTicks = 0;
        void Notify(bool force)
        {
            var now = Environment.TickCount64;
            var previous = Interlocked.Read(ref lastTicks);
            if (!force && now - previous < 120) return;
            if (Interlocked.CompareExchange(ref lastTicks, now, previous) != previous) return;
            progress(Volatile.Read(ref done), total);
        }

        var tasks = list.Select(f => Task.Run(async () =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await DownloadAsync(f.Url, f.Path, f.Sha1, ct, Mirror.ToOfficial(f.Url));
                Interlocked.Increment(ref done);
                Notify(false);
            }
            finally
            {
                semaphore.Release();
            }
        }, ct));

        await Task.WhenAll(tasks);
        Notify(true);
    }
}
