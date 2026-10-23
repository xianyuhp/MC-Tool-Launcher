using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AuroraLauncher.Core;

namespace AuroraLauncher.Views;

public class SteamItem : INotifyPropertyChanged
{
    public SteamGame Game { get; init; }
    public long AppId => Game.AppId;
    public string Name => Game.Name;
    public string PriceText { get; set; }
    private ImageSource _image;
    public ImageSource Image
    {
        get => _image;
        set { _image = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image))); }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    public override string ToString() => Name;
}

public partial class SteamView : UserControl
{
    private CancellationTokenSource _detailCts;
    private bool _loaded;

    public SteamView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            await LoadDiscountedAsync();
        };
    }

    private async Task LoadDiscountedAsync()
    {
        SearchStatus.Text = "正在加载折扣热销…";
        try
        {
            var games = await SteamService.GetDiscountedTopAsync();
            var status = games.Count == 0
                ? (SteamService.LastLoadNote.Length > 0 ? SteamService.LastLoadNote : "暂无折扣数据")
                : SteamService.LastLoadNote.Length > 0
                    ? $"{SteamService.LastLoadNote}（{games.Count} 个）"
                    : $"正在折扣的热销游戏（{games.Count}）";
            FillResults(games, status);
        }
        catch (Exception ex)
        {
            SearchStatus.Text = "加载失败：" + ex.Message;
            Logger.Warn("Steam 特惠加载失败：" + ex.Message);
        }
    }

    private async void RefreshFeatured_Click(object sender, RoutedEventArgs e) => await LoadDiscountedAsync();

    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await SearchAsync();
    }

    private async Task SearchAsync()
    {
        var term = SearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(term))
        {
            await LoadDiscountedAsync();
            return;
        }

        SearchStatus.Text = "搜索中…";
        try
        {
            var games = await SteamService.SearchAsync(term);
            FillResults(games, games.Count == 0 ? "没有找到结果，换个关键词试试。" : $"找到 {games.Count} 个结果");
        }
        catch (Exception ex)
        {
            SearchStatus.Text = "搜索失败：" + ex.Message;
            Logger.Warn("Steam 搜索异常：" + ex.Message);
        }
    }

    private void FillResults(List<SteamGame> games, string status)
    {
        Results.Items.Clear();
        foreach (var game in games)
        {
            var item = new SteamItem { Game = game, PriceText = game.PriceText };
            Results.Items.Add(item);
            if (!string.IsNullOrEmpty(game.ImageUrl)) _ = LoadImageAsync(item, game.ImageUrl);
        }
        SearchStatus.Text = status;

        if (Results.Items.Count > 0)
        {
            Results.SelectedIndex = 0;
            ShowDetail((SteamItem)Results.Items[0]);
        }
        else
        {
            DetailName.Text = "搜索一个游戏开始";
            DetailId.Text = "";
            DetailPrice.Text = "—";
            DetailLow.Text = "—";
            EditionList.ItemsSource = null;
        }
    }

    private static async Task LoadImageAsync(SteamItem item, string url)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var bytes = await client.GetByteArrayAsync(url);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new System.IO.MemoryStream(bytes);
            bitmap.EndInit();
            bitmap.Freeze();
            item.Image = bitmap;
        }
        catch
        {
            // 图片加载失败不影响功能
        }
    }

    private void Results_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Results.SelectedItem is SteamItem item) ShowDetail(item);
    }

    private void ShowDetail(SteamItem item)
    {
        DetailName.Text = item.Name;
        DetailId.Text = "AppID " + item.AppId;
        // 绑定到列表项，封面异步加载完成后会自动刷新（避免首次打开空白）
        DetailImage.DataContext = item;
        UiAnim.ExpandIn(DetailPanel, 8, 220);
        DetailPrice.Text = item.PriceText;
        DetailLow.Text = "查询中…";
        DetailSource.Text = "";
        DetailStatus.Text = "";
        EditionList.ItemsSource = null;
        _ = LoadDetailsAsync(item);
    }

    private async Task LoadDetailsAsync(SteamItem item)
    {
        _detailCts?.Cancel();
        _detailCts = new CancellationTokenSource();
        var token = _detailCts.Token;
        try
        {
            var priceTask = SteamService.GetPriceAsync(item.AppId, token);
            var lowTask = SteamService.GetPriceHistoryAsync(item.AppId, token);
            var editionsTask = SteamService.GetEditionsAsync(item.AppId, token);
            await Task.WhenAll(priceTask, lowTask, editionsTask);
            if (token.IsCancellationRequested) return;

            DetailPrice.Text = priceTask.Result;
            var history = lowTask.Result;
            DetailLow.Text = history.Cny == null
                ? "暂无史低数据"
                : $"国区史低：¥{history.Cny:0.00}" +
                  (history.Date != null ? $"（{history.Date:yyyy-MM-dd}）" : "");
            DetailSource.Text = "数据来源：" + history.Source +
                                (history.Usd is decimal usd ? $"　国际史低：${usd:0.00}" : "");

            EditionList.ItemsSource = editionsTask.Result.Count > 0
                ? editionsTask.Result
                : new List<SteamEdition> { new("暂无套餐信息", "") };

            DetailStatus.Text = $"已更新（{DateTime.Now:HH:mm:ss}）";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            DetailStatus.Text = "查询失败：" + ex.Message;
        }
    }

    private void OpenStore_Click(object sender, RoutedEventArgs e)
    {
        if (Results.SelectedItem is not SteamItem item) return;
        try
        {
            Process.Start(new ProcessStartInfo(item.Game.StoreUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "打开失败");
        }
    }

    private void RefreshPrice_Click(object sender, RoutedEventArgs e)
    {
        if (Results.SelectedItem is not SteamItem item) return;
        _ = LoadDetailsAsync(item);
    }
}
