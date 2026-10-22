using System.Windows;
using System.Windows.Threading;

namespace AuroraLauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Core.Logger.Error("未处理异常: " + args.Exception);
            MessageBox.Show(args.Exception.Message, "MCTL 发生错误", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Core.Logger.Error("致命异常: " + args.ExceptionObject);
        };
    }
}
