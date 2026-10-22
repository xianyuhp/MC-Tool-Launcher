using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AuroraLauncher.Core;

public interface IRefreshable
{
    void Refresh();
}

public static class ImageCache
{
    private static readonly Dictionary<string, BitmapImage> Cache = new();

    public static ImageSource Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        if (Cache.TryGetValue(path, out var cached)) return cached;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.UriSource = new Uri(path);
        bitmap.EndInit();
        bitmap.Freeze();
        Cache[path] = bitmap;
        return bitmap;
    }

    public static void Clear() => Cache.Clear();
}
