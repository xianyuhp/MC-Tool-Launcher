using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using MCTL.Core;

namespace MCTL.Views;

/// <summary>开源版本的 API 凭据填写窗口：微软客户端 ID / CurseForge API Key。</summary>
public partial class ApiKeysWindow : Window
{
    public ApiKeysWindow()
    {
        InitializeComponent();
        MsBox.Text = ApiKeys.MicrosoftClientId;
        CfBox.Text = ApiKeys.CurseForgeKey;
        PathText.Text = "凭据文件：" + ApiKeys.FilePath;

        if (!ApiKeys.MicrosoftReady) StatusText.Text = "当前是开源版本：请先填写自己申请的凭据。";
        else if (!ApiKeys.CurseForgeReady) StatusText.Text = "尚未配置 CurseForge API Key，将只使用社区镜像。";
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApiKeys.Save(MsBox.Text, CfBox.Text);

            // CurseForge Key 同时写进设置，保证「设置」页的输入框与这里一致
            var key = (CfBox.Text ?? "").Trim();
            if (key.Length > 0 && Settings.Current.CurseForgeApiKey != key)
            {
                Settings.Current.CurseForgeApiKey = key;
                Settings.Save();
            }

            StatusText.Text = ApiKeys.NeedsSetup
                ? "已保存，但仍有项未填写：微软登录或 CurseForge 官方接口暂不可用。"
                : "已保存并立即生效，可以开始使用了。";
        }
        catch (Exception ex)
        {
            StatusText.Text = "保存失败：" + ex.Message;
        }
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApiKeys.EnsureTemplate();
            Process.Start(new ProcessStartInfo(ApiKeys.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = "打开失败：" + ex.Message;
        }
    }

    private void OpenAzure_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade");

    private void OpenCurseForge_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://console.curseforge.com/");

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { StatusText.Text = "打开失败：" + ex.Message; }
    }
}
