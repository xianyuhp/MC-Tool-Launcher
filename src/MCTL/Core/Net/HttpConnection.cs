using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MCTL.Core.Net;

/// <summary>
/// 多地址并发连接：部分网络下域名的解析结果里只有个别 IP 可达（例如 cdn.modrinth.com 的
/// Fastly 节点），系统默认只会串行尝试并很快超时。这里：
/// 1) 同时用系统 DNS 与阿里 DoH 解析，合并去重（避开单一解析结果全是不通 IP 的情况）；
/// 2) 对所有地址并发拨号，取最快连上的那个，其余立即取消。
/// </summary>
internal static class HttpConnection
{
    private static readonly HttpClient DohClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5),
        DefaultRequestHeaders = { { "accept", "application/dns-json" } }
    };

    private static readonly ConcurrentDictionary<string, (DateTime At, IPAddress[] Addresses)> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;

        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal))
        {
            addresses = new[] { literal };
        }
        else
        {
            addresses = await ResolveAsync(host, ct);
        }

        if (addresses.Length == 0)
            throw new IOException($"无法解析域名：{host}");

        // IPv4 排前面：很多网络里 IPv6 只是黑洞地址
        var ordered = addresses
            .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
            .ToArray();

        using var race = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tasks = ordered.Select(a => ConnectOneAsync(a, port, race.Token)).ToList();
        Exception last = null;

        while (tasks.Count > 0)
        {
            var done = await Task.WhenAny(tasks);
            tasks.Remove(done);
            try
            {
                var socket = await done;
                race.Cancel();
                foreach (var pending in tasks)
                {
                    _ = pending.ContinueWith(t =>
                    {
                        if (t.Status == TaskStatus.RanToCompletion) t.Result.Dispose();
                        else _ = t.Exception;
                    }, TaskScheduler.Default);
                }
                return new NetworkStream(socket, true);
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        throw new IOException($"无法连接到 {host}:{port}（{ordered.Length} 个地址均连接失败）", last);
    }

    /// <summary>系统 DNS + 阿里 DoH 双路解析，结果缓存 10 分钟。</summary>
    private static async Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
    {
        if (Cache.TryGetValue(host, out var cached) &&
            DateTime.UtcNow - cached.At < TimeSpan.FromMinutes(10))
            return cached.Addresses;

        var list = new List<IPAddress>();
        try
        {
            list.AddRange(await Dns.GetHostAddressesAsync(host, ct));
        }
        catch
        {
            // 系统解析失败时继续尝试 DoH
        }

        try
        {
            list.AddRange(await ResolveViaAliDnsAsync(host, ct));
        }
        catch
        {
            // DoH 不可用时只用系统结果
        }

        var distinct = list.Distinct().ToArray();
        if (distinct.Length > 0) Cache[host] = (DateTime.UtcNow, distinct);
        return distinct;
    }

    private static async Task<IEnumerable<IPAddress>> ResolveViaAliDnsAsync(string host, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));

        var url = "https://dns.alidns.com/resolve?name=" + Uri.EscapeDataString(host) + "&type=A";
        using var response = await DohClient.GetAsync(url, timeout.Token);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        if (!doc.RootElement.TryGetProperty("Answer", out var answers)) return Array.Empty<IPAddress>();

        var list = new List<IPAddress>();
        foreach (var answer in answers.EnumerateArray())
        {
            if (!answer.TryGetProperty("type", out var type) || type.GetInt32() != 1) continue;
            if (!answer.TryGetProperty("data", out var data)) continue;
            if (IPAddress.TryParse(data.GetString(), out var ip)) list.Add(ip);
        }
        return list;
    }

    private static async Task<Socket> ConnectOneAsync(IPAddress address, int port, CancellationToken ct)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), ct);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
