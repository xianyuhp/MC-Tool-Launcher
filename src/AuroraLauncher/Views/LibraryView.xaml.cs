using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using AuroraLauncher.Core;

namespace AuroraLauncher.Views;

public partial class LibraryView : UserControl, IRefreshable
{
    public LibraryView()
    {
        InitializeComponent();
        Refresh();
    }

    public void Refresh()
    {
        AppsPanel.Children.Clear();
        foreach (var entry in AppLibrary.Items.ToList())
            AppsPanel.Children.Add(BuildCard(entry));
        AppsPanel.Children.Add(BuildAddCard());
    }

    private Border BuildCard(AppEntry entry)
    {
        var card = new Border
        {
            Style = (Style)FindResource("Card"),
            Width = 272,
            Height = 172,
            Margin = new Thickness(0, 0, 14, 14),
            Padding = new Thickness(16)
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = AppLibrary.GetIcon(entry.Path);
        if (icon != null)
        {
            header.Children.Add(new Border
            {
                Width = 44, Height = 44, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)),
                Child = new Image { Source = icon, Stretch = Stretch.Uniform, Margin = new Thickness(6) }
            });
        }
        else
        {
            header.Children.Add(new Border
            {
                Width = 44, Height = 44, CornerRadius = new CornerRadius(10),
                Background = (Brush)FindResource("AccentSoftBrush"),
                Child = new TextBlock
                {
                    Text = string.IsNullOrEmpty(entry.Name) ? "?" : entry.Name[..1].ToUpperInvariant(),
                    FontSize = 20, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
        }

        var titlePanel = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titlePanel.Children.Add(new TextBlock
        {
            Text = entry.Name, FontSize = 14, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 170
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = Path.GetFileName(entry.Path), FontSize = 10,
            Foreground = (Brush)FindResource("SubTextBrush"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 170
        });
        header.Children.Add(titlePanel);
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        var pathText = new TextBlock
        {
            Text = entry.Path, FontSize = 10, Margin = new Thickness(0, 10, 0, 0),
            Foreground = (Brush)FindResource("SubTextBrush"), TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.Wrap, MaxHeight = 32, ToolTip = entry.Path
        };
        Grid.SetRow(pathText, 1);
        grid.Children.Add(pathText);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var launch = new Button { Content = "启动", Style = (Style)FindResource("PrimaryBtn"), Padding = new Thickness(16, 6, 16, 6), FontSize = 12 };
        launch.Click += (_, _) => AppLibrary.Launch(entry);
        var remove = new Button { Content = "移除", Style = (Style)FindResource("Btn"), Padding = new Thickness(12, 6, 12, 6), FontSize = 12, Margin = new Thickness(8, 0, 0, 0) };
        remove.Click += (_, _) =>
        {
            if (MessageBox.Show($"确定从库中移除「{entry.Name}」吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                AppLibrary.Remove(entry);
                Refresh();
            }
        };
        actions.Children.Add(launch);
        actions.Children.Add(remove);
        Grid.SetRow(actions, 2);
        grid.Children.Add(actions);

        card.Child = grid;
        return card;
    }

    private Border BuildAddCard()
    {
        var card = new Border
        {
            Width = 272, Height = 172, Margin = new Thickness(0, 0, 14, 14),
            CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 255, 255, 255)),
            Background = new SolidColorBrush(Color.FromArgb(0x10, 255, 255, 255)),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        card.Child = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "+", FontSize = 34, HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = (Brush)FindResource("SubTextBrush") },
                new TextBlock { Text = "添加程序", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = (Brush)FindResource("SubTextBrush") }
            }
        };
        card.MouseLeftButtonUp += (_, _) => AddApp();
        return card;
    }

    private void Add_Click(object sender, RoutedEventArgs e) => AddApp();

    private void AddApp()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要添加到游戏库的程序",
            Filter = "程序 (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;
        AppLibrary.Add(dialog.FileName);
        Logger.Info("已添加到游戏库：" + dialog.FileName);
        Refresh();
    }
}
