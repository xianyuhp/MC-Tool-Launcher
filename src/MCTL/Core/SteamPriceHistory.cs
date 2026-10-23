using System.IO;
using System.Text.Json;

namespace MCTL.Core;

/// <summary>本地价格记录：当在线史低接口不可用时，用我们观察到的历史最低价兜底。</summary>
public static class SteamPriceHistory
{
    private static readonly string FilePath = Path.Combine(Settings.ConfigDir, "steam-prices.json");
    private static readonly object Gate = new();
    private static Dictionary<string, PriceRecord> _records;

    public class PriceRecord
    {
        public decimal LowestCny { get; set; }
        public string Date { get; set; } = "";
        public decimal LastCny { get; set; }
        public int LastDiscount { get; set; }
        public string Updated { get; set; } = "";
    }

    private static void EnsureLoaded()
    {
        if (_records != null) return;
        try
        {
            _records = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Dictionary<string, PriceRecord>>(File.ReadAllText(FilePath)) ??
                  new Dictionary<string, PriceRecord>()
                : new Dictionary<string, PriceRecord>();
        }
        catch
        {
            _records = new Dictionary<string, PriceRecord>();
        }
    }

    /// <summary>记录一次观察到的国区价格（自动维护最低价）。</summary>
    public static void Record(long appId, decimal cny, int discount)
    {
        if (appId <= 0 || cny <= 0) return;
        lock (Gate)
        {
            EnsureLoaded();
            var key = appId.ToString();
            if (!_records.TryGetValue(key, out var record))
                _records[key] = record = new PriceRecord { LowestCny = cny, Date = DateTime.Now.ToString("yyyy-MM-dd") };

            if (cny < record.LowestCny || record.LowestCny <= 0)
            {
                record.LowestCny = cny;
                record.Date = DateTime.Now.ToString("yyyy-MM-dd");
            }
            record.LastCny = cny;
            record.LastDiscount = discount;
            record.Updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            Save();
        }
    }

    public static PriceRecord Get(long appId)
    {
        lock (Gate)
        {
            EnsureLoaded();
            return _records.TryGetValue(appId.ToString(), out var record) ? record : null;
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Settings.ConfigDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_records,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Logger.Warn("保存本地价格记录失败：" + ex.Message);
        }
    }
}
