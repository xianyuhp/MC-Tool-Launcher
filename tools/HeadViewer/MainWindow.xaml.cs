using System.Drawing;
using System.IO;
using System.Windows;

namespace TALHead;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Web.DefaultBackgroundColor = Color.FromArgb(255, 13, 17, 25);
        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            await Web.EnsureCoreWebView2Async();
            Web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            Web.CoreWebView2.Settings.IsStatusBarEnabled = false;

            using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("TALHead.HeadPage.html");
            if (stream == null)
            {
                MessageBox.Show("内置页面缺失，无法启动。", "TAL 3D 头部", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            using var reader = new StreamReader(stream);
            Web.NavigateToString(await reader.ReadToEndAsync());
        }
        catch (Exception ex)
        {
            MessageBox.Show("WebView2 初始化失败：" + ex.Message +
                            "\n\n请安装 Microsoft Edge WebView2 Runtime 后重试。",
                "TAL 3D 头部", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
