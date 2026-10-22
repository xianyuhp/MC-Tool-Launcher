using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using AuroraLauncher.Core.Mc;

namespace AuroraLauncher.Core.Net;

/// <summary>
/// 带容错的 HTTP 客户端：浏览器请求头 + 指数退避重试 + Maven 镜像替换。
/// 只依赖 .NET 基础库（System.Net.Http），不需要额外 NuGet 包。
/// </summary>
public static class ResilientHttp
{
    private const int MaxAttempts = 3;

    private static readonly HttpClient Client = CreateClient();
    private static readonly Dictionary<string, DateTime> DeadHosts = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            // 并发拨号：域名解析出多个 IP 时，避开其中不通的地址
            ConnectCallback = HttpConnection.ConnectAsync
        };
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };

        // 常见浏览器请求头，避免被部分 CDN / WAF 直接拦截
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8");
        client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate, br");
        return client;
    }

    /// <summary>
    /// 为一个地址生成候选下载源：原始地址优先，其次自动替换为 Maven 镜像。
    /// </summary>
    public static List<string> Candidates(string url, params string[] extra)
    {
        var list = new List<string>();
        void Add(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return;
            if (!list.Contains(candidate, StringComparer.OrdinalIgnoreCase)) list.Add(candidate);
        }

        var mirrors = MavenMirrors(url).ToList();
        // NeoForge / Forge 的官方 Maven 在部分网络下完全不通，镜像优先更快
        var mirrorFirst = url.Contains("neoforged", StringComparison.OrdinalIgnoreCase) ||
                          url.Contains("minecraftforge", StringComparison.OrdinalIgnoreCase);
        if (mirrorFirst)
        {
            foreach (var mirror in mirrors) Add(mirror);
            Add(url);
        }
        else
        {
            Add(url);
            foreach (var mirror in mirrors) Add(mirror);
        }

        foreach (var candidate in extra) Add(candidate);

        // 已知不通的域名排到最后，避免每次都在死域名上等超时
        var alive = list.Where(u => !IsHostDead(u)).ToList();
        var dead = list.Where(IsHostDead).ToList();
        return alive.Concat(dead).ToList();
    }

    /// <summary>官方 Maven 域名 → 可用镜像（ForgeCDN / BMCLAPI）。</summary>
    public static IEnumerable<string> MavenMirrors(string url)
    {
        if (string.IsNullOrEmpty(url)) yield break;

        const string neoOfficial = "https://maven.neoforged.net/";
        const string forgeOfficial = "https://maven.minecraftforge.net/";

        if (url.StartsWith(neoOfficial, StringComparison.OrdinalIgnoreCase))
        {
            var tail = url[neoOfficial.Length..]; // releases/net/neoforged/...
            yield return "https://neoforged.forgecdn.net/" + tail;
            if (tail.StartsWith("releases/", StringComparison.OrdinalIgnoreCase))
                yield return Mirror.Bmclapi + "/maven/" + tail["releases/".Length..];
            else
                yield return Mirror.Bmclapi + "/maven/" + tail;
            yield break;
        }

        if (url.StartsWith(forgeOfficial, StringComparison.OrdinalIgnoreCase))
        {
            var tail = url[forgeOfficial.Length..];
            yield return "https://files.minecraftforge.net/maven/" + tail;
            yield return Mirror.Bmclapi + "/maven/" + tail;
            yield break;
        }

        if (url.StartsWith("https://libraries.minecraft.net/", StringComparison.OrdinalIgnoreCase))
            yield return Mirror.Bmclapi + "/maven/" + url["https://libraries.minecraft.net/".Length..];
    }

    /// <summary>该地址对应的官网 / 手动下载页，用于提示用户。</summary>
    public static string OfficialPage(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (url.Contains("neoforged", StringComparison.OrdinalIgnoreCase))
            return "https://neoforged.net/";
        if (url.Contains("minecraftforge", StringComparison.OrdinalIgnoreCase))
            return "https://files.minecraftforge.net/";
        if (url.Contains("fabricmc", StringComparison.OrdinalIgnoreCase))
            return "https://fabricmc.net/";
        return null;
    }

    /// <summary>下载文本（元数据 / JSON），失败自动重试并切换镜像。</summary>
    public static async Task<string> GetStringAsync(string url, int timeoutSeconds = 30, CancellationToken ct = default)
    {
        Exception last = null;
        Exception definitive = null;
        foreach (var candidate in Candidates(url))
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                    using var response = await Client.GetAsync(candidate, timeout.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        if (!IsRetryableStatus(response.StatusCode))
                        {
                            definitive = new HttpRequestException(
                                $"HTTP {(int)response.StatusCode}（{Host(candidate)}）", null, response.StatusCode);
                            last = definitive;
                            break;
                        }
                        throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
                    }

                    Logger.Info($"请求成功：{Host(candidate)}");
                    return await response.Content.ReadAsStringAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    last = new TimeoutException($"连接 {Host(candidate)} 超时（{timeoutSeconds}s）");
                    Logger.Warn($"请求超时：{Host(candidate)}（第 {attempt} 次）");
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && IsTransient(ex))
                {
                    last = ex;
                    Logger.Warn($"请求失败：{Host(candidate)}（第 {attempt} 次）{ex.Message}");
                }

                // 连不上就不要再等同一域名，直接换镜像
                if (last != null && IsConnectionFailure(last)) break;
                if (attempt < MaxAttempts) await DelayAsync(attempt, ct);
            }

            if (last != null && IsConnectionFailure(last)) MarkDead(candidate);
        }

        throw new DownloadUnavailableException(url, definitive ?? last);
    }

    /// <summary>下载文件，带指数退避重试、镜像切换与 SHA1 校验。</summary>
    public static async Task<bool> DownloadFileAsync(string url, string path, string sha1,
        CancellationToken ct, IProgress<double> progress = null, params string[] extraSources)
    {
        if (File.Exists(path) && (string.IsNullOrEmpty(sha1) ||
            Sha1File(path).Equals(sha1, StringComparison.OrdinalIgnoreCase)))
        {
            progress?.Report(1);
            return true;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Exception last = null;
        Exception definitive = null;

        foreach (var candidate in Candidates(url, extraSources))
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".part";
                try
                {
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        timeout.CancelAfter(TimeSpan.FromMinutes(5));
                        using var response = await Client.GetAsync(candidate,
                            HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                        if (!response.IsSuccessStatusCode)
                        {
                            if (!IsRetryableStatus(response.StatusCode))
                            {
                                definitive = new HttpRequestException(
                                    $"HTTP {(int)response.StatusCode}（{Host(candidate)}）", null, response.StatusCode);
                                last = definitive;
                                break;
                            }
                            throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
                        }

                        var total = response.Content.Headers.ContentLength ?? -1;
                        progress?.Report(0);
                        await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
                        await using var file = File.Create(tmp);
                        var buffer = new byte[81920];
                        long read = 0;
                        int n;
                        while ((n = await source.ReadAsync(buffer, timeout.Token)) > 0)
                        {
                            await file.WriteAsync(buffer.AsMemory(0, n), timeout.Token);
                            read += n;
                            if (total > 0) progress?.Report(Math.Min(1.0, (double)read / total));
                        }
                    }

                    if (!string.IsNullOrEmpty(sha1))
                    {
                        var actual = Sha1File(tmp);
                        if (!actual.Equals(sha1, StringComparison.OrdinalIgnoreCase))
                            throw new IOException($"SHA1 校验失败（期望 {sha1}，实际 {actual}）");
                    }

                    File.Move(tmp, path, true);
                    progress?.Report(1);
                    Logger.Info($"下载成功：{Host(candidate)} → {Path.GetFileName(path)}");
                    return true;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    last = new TimeoutException($"连接 {Host(candidate)} 超时");
                    Logger.Warn($"下载超时：{Host(candidate)}（第 {attempt} 次）");
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && IsTransient(ex))
                {
                    last = ex;
                    Logger.Warn($"下载失败：{Host(candidate)}（第 {attempt} 次）{ex.Message}");
                }
                finally
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                }

                if (last != null && IsConnectionFailure(last)) break;
                if (attempt < MaxAttempts) await DelayAsync(attempt, ct);
            }

            if (last != null && IsConnectionFailure(last)) MarkDead(candidate);
        }

        throw new DownloadUnavailableException(url, definitive ?? last);
    }

    public static string Sha1File(string path)
    {
        using var sha = SHA1.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    /// <summary>指数退避延迟，供自建下载循环复用。</summary>
    public static Task RetryDelayAsync(int attempt, CancellationToken ct) => DelayAsync(attempt, ct);

    /// <summary>是否属于可重试的瞬时错误（超时 / 连接被拒 / IO）。</summary>
    public static bool IsTransientError(Exception ex) => IsTransient(ex);

    /// <summary>
    /// 连接层面失败（超时 / 拒绝 / DNS）：换镜像比原地重试更有效，
    /// 调用方看到这类错误应直接跳到下一个候选源。
    /// </summary>
    public static bool IsConnectionFailure(Exception ex) =>
        ex is TimeoutException or OperationCanceledException or System.Net.Sockets.SocketException ||
        (ex is HttpRequestException http &&
         (http.InnerException is System.Net.Sockets.SocketException || http.InnerException is TimeoutException));

    // ---------------- 内部工具 ----------------

    /// <summary>指数退避：0.6s → 1.2s → 2.4s，附少量随机抖动避免同时重试。</summary>
    private static Task DelayAsync(int attempt, CancellationToken ct)
    {
        var baseDelay = 600 * Math.Pow(2, attempt - 1);
        var jitter = Random.Shared.Next(0, 250);
        return Task.Delay(TimeSpan.FromMilliseconds(baseDelay + jitter), ct);
    }

    private static bool IsRetryableStatus(HttpStatusCode status) =>
        (int)status >= 500 || status == HttpStatusCode.RequestTimeout || (int)status == 429;

    private static bool IsTransient(Exception ex) =>
        ex is HttpRequestException or IOException or TimeoutException or System.Net.Sockets.SocketException;

    private static string Host(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

    private static bool IsHostDead(string url)
    {
        lock (Gate)
        {
            if (!DeadHosts.TryGetValue(Host(url), out var until)) return false;
            if (DateTime.Now > until)
            {
                DeadHosts.Remove(Host(url));
                return false;
            }
            return true;
        }
    }

    private static void MarkDead(string url)
    {
        lock (Gate) DeadHosts[Host(url)] = DateTime.Now.AddMinutes(10);
    }
}

/// <summary>官方源与所有镜像都不可用时抛出，消息中直接告诉用户缺少什么。</summary>
public sealed class DownloadUnavailableException : Exception
{
    public string Url { get; }
    public string OfficialPage { get; }

    public DownloadUnavailableException(string url, Exception inner)
        : this(url, inner, null)
    {
    }

    public DownloadUnavailableException(string url, Exception inner, string extraHint)
        : base(BuildMessage(url, inner, extraHint), inner)
    {
        Url = url;
        OfficialPage = ResilientHttp.OfficialPage(url);
    }

    private static string BuildMessage(string url, Exception inner, string extraHint)
    {
        var name = Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? Path.GetFileName(uri.AbsolutePath)
            : url;
        var page = ResilientHttp.OfficialPage(url);
        var hint = string.IsNullOrEmpty(page)
            ? ""
            : $"\n官方下载页：{page}";
        var extra = string.IsNullOrEmpty(extraHint) ? "" : "\n" + extraHint;
        return $"缺少文件：{name}\n已尝试官方源与镜像（ForgeCDN / BMCLAPI），均未能获取该文件。" +
               hint + extra + $"\n原始地址：{url}\n最后错误：{inner?.Message}";
    }
}
