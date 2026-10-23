using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using MCTL.Core;
using MCTL.Core.Auth;

namespace MCTL.Views;

public partial class LoginWindow : Window
{
    private CancellationTokenSource _cts = new();
    private string _verificationUrl = "";

    public MinecraftAccount Account { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await StartAsync();
        Closed += (_, _) => _cts.Cancel();
    }

    private async Task StartAsync()
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        CodeText.Text = "······";
        StatusText.Text = "正在获取登录代码…";
        try
        {
            var code = await MicrosoftAuth.RequestDeviceCodeAsync(_cts.Token);
            CodeText.Text = code.UserCode;
            LinkText.Text = code.VerificationUri;
            _verificationUrl = code.VerificationUriComplete;
            StatusText.Text = "等待浏览器中完成登录…";

            // 自动复制登录代码，方便直接粘贴
            try
            {
                Clipboard.SetText(code.UserCode);
                StatusText.Text = "登录代码已复制到剪贴板，正在等待浏览器中完成登录…";
            }
            catch { }

            try { Process.Start(new ProcessStartInfo(_verificationUrl) { UseShellExecute = true }); }
            catch (Exception ex) { Logger.Warn("打开登录页面失败：" + ex.Message); }

            var progress = new Progress<string>(s => StatusText.Text = s);
            var account = await MicrosoftAuth.WaitForLoginAsync(code, progress, _cts.Token);
            AccountService.Apply(account);
            Account = account;
            Logger.Info("微软登录成功：" + account.Name);
            DialogResult = true;
            Close();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消登录。";
        }
        catch (Exception ex)
        {
            StatusText.Text = "登录失败：" + ex.Message;
            Logger.Error("微软登录失败：" + ex.Message);
        }
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var url = string.IsNullOrEmpty(_verificationUrl) ? "https://microsoft.com/link" : _verificationUrl;
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "打开失败");
        }
    }

    private async void Restart_Click(object sender, RoutedEventArgs e) => await StartAsync();

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(CodeText.Text) && CodeText.Text != "······")
            {
                Clipboard.SetText(CodeText.Text.Trim());
                StatusText.Text = "登录代码已复制到剪贴板。";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "复制失败：" + ex.Message;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        DialogResult = false;
        Close();
    }
}
