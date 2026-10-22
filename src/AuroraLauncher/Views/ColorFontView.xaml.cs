using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using AuroraLauncher.Core;

namespace AuroraLauncher.Views;

/// <summary>Minecraft 颜色代码 / 渐变字体生成器（§ 与 Java 的 \u00A7 两种输出）。</summary>
public partial class ColorFontView : UserControl, IRefreshable
{
    private static readonly (string Code, string Hex)[] Palette =
    {
        ("0", "#000000"), ("1", "#0000AA"), ("2", "#00AA00"), ("3", "#00AAAA"),
        ("4", "#AA0000"), ("5", "#AA00AA"), ("6", "#FFAA00"), ("7", "#AAAAAA"),
        ("8", "#555555"), ("9", "#5555FF"), ("a", "#55FF55"), ("b", "#55FFFF"),
        ("c", "#FF5555"), ("d", "#FF55FF"), ("e", "#FFFF55"), ("f", "#FFFFFF")
    };

    private string _solid = "f";
    /// <summary>渐变颜色节点（第一个为起始色，最后一个为结束色，中间可插入多段）。</summary>
    private readonly List<string> _stops = new() { "b", "d" };
    private bool _gradientMode;
    private bool _javaOutput;

    public ColorFontView()
    {
        InitializeComponent();
        BuildPalettes();
        Generate();
    }

    public void Refresh() => Generate();

    private bool Gradient => _gradientMode;

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (PaletteArea == null) return;
        _gradientMode = ReferenceEquals(sender, ModeGradient);
        BuildPalettes();
        Generate();
    }

    private void OutputRaw_Checked(object sender, RoutedEventArgs e)
    {
        _javaOutput = false;
        if (IsLoaded) Generate();
    }

    private void OutputJava_Checked(object sender, RoutedEventArgs e)
    {
        _javaOutput = true;
        if (IsLoaded) Generate();
    }

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (PreviewText == null || !IsLoaded) return;
        Generate();
    }

    private void BuildPalettes()
    {
        PaletteArea.Children.Clear();
        if (!Gradient)
        {
            PaletteArea.Children.Add(PaletteRow("颜色", _solid, code =>
            {
                _solid = code;
                BuildPalettes();
                Generate();
            }));
            return;
        }

        // 多段渐变：起始色 → …中间色… → 结束色
        var header = new Grid { Margin = new Thickness(0, 2, 0, 4) };
        header.Children.Add(new TextBlock
        {
            Text = $"渐变颜色（{_stops.Count} 段，可插入中间色）",
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("SubTextBrush")
        });
        var add = new Button
        {
            Content = "+ 添加颜色",
            Style = (Style)FindResource("Btn"),
            Padding = new Thickness(8, 2, 8, 2),
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        add.Click += (_, _) =>
        {
            _stops.Insert(_stops.Count - 1, _stops[^1]);
            BuildPalettes();
            Generate();
        };
        header.Children.Add(add);
        PaletteArea.Children.Add(header);

        for (var i = 0; i < _stops.Count; i++)
        {
            var index = i;
            var title = _stops.Count <= 2
                ? (i == 0 ? "起始色" : "结束色")
                : i == 0 ? "起始色"
                : i == _stops.Count - 1 ? "结束色"
                : $"颜色 {i + 1}";

            var block = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            var head = new Grid();
            head.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 10,
                Margin = new Thickness(0, 0, 0, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("SubTextBrush")
            });
            if (i > 0 && i < _stops.Count - 1)
            {
                var remove = new Button
                {
                    Content = "×",
                    Style = (Style)FindResource("Btn"),
                    Padding = new Thickness(7, 0, 7, 0),
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    ToolTip = "移除这个中间色"
                };
                remove.Click += (_, _) =>
                {
                    _stops.RemoveAt(index);
                    BuildPalettes();
                    Generate();
                };
                head.Children.Add(remove);
            }
            block.Children.Add(head);
            block.Children.Add(PaletteRow("", _stops[i], code =>
            {
                _stops[index] = code;
                BuildPalettes();
                Generate();
            }));
            PaletteArea.Children.Add(block);
        }
    }

    private FrameworkElement PaletteRow(string title, string selected, Action<string> onPick)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        if (!string.IsNullOrEmpty(title))
        {
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 10,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = (Brush)FindResource("SubTextBrush")
            });
        }

        var wrap = new WrapPanel();
        foreach (var (code, hex) in Palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var isSelected = string.Equals(code, selected, StringComparison.OrdinalIgnoreCase);
            var swatch = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 5, 5),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                BorderBrush = new SolidColorBrush(isSelected ? Colors.White : Color.FromArgb(0x40, 0, 0, 0)),
                Cursor = Cursors.Hand,
                ToolTip = $"§{code}  {hex}",
                Child = new TextBlock
                {
                    Text = code,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brightness(color) > 140 ? Brushes.Black : Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            var captured = code;
            swatch.MouseLeftButtonUp += (_, _) => onPick(captured);
            wrap.Children.Add(swatch);
        }
        panel.Children.Add(wrap);
        return panel;
    }

    private static int Brightness(Color color) => (color.R * 299 + color.G * 587 + color.B * 114) / 1000;

    private void Generate_Click(object sender, RoutedEventArgs e) => Generate();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        InputBox.Clear();
        Generate();
        InputBox.Focus();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(OutputBox.Text ?? "");
            CodeHint.Text = "已复制到剪贴板";
        }
        catch (Exception ex)
        {
            CodeHint.Text = "复制失败：" + ex.Message;
        }
    }

    private void Generate()
    {
        if (PreviewText == null || OutputBox == null || InputBox == null) return;

        var text = InputBox.Text ?? "";
        PreviewText.Inlines.Clear();
        if (text.Length == 0)
        {
            OutputBox.Text = "";
            CodeHint.Text = "";
            return;
        }

        var bold = BoldChk.IsChecked == true;
        var italic = ItalicChk.IsChecked == true;
        var underline = UnderlineChk.IsChecked == true;
        var strike = StrikeChk.IsChecked == true;
        var obfuscated = ObfuscatedChk.IsChecked == true;

        var builder = new StringBuilder();
        if (!Gradient)
        {
            var color = PaletteColor(_solid);
            builder.Append('§').Append(_solid);
            AppendStyles(builder, bold, italic, underline, strike, obfuscated);
            builder.Append(text);

            var run = new Run(text) { Foreground = new SolidColorBrush(color) };
            ApplyStyle(run, bold, italic, underline, strike);
            PreviewText.Inlines.Add(run);
        }
        else
        {
            var stops = _stops.Select(PaletteColor).ToList();
            for (var i = 0; i < text.Length; i++)
            {
                var t = text.Length == 1 ? 0 : (double)i / (text.Length - 1);
                var color = SampleGradient(stops, t);
                var hex = $"{color.R:x2}{color.G:x2}{color.B:x2}";
                builder.Append("§x");
                foreach (var ch in hex) builder.Append('§').Append(ch);
                AppendStyles(builder, bold, italic, underline, strike, obfuscated);
                builder.Append(text[i]);

                var run = new Run(text[i].ToString()) { Foreground = new SolidColorBrush(color) };
                ApplyStyle(run, bold, italic, underline, strike);
                PreviewText.Inlines.Add(run);
            }
        }

        var code = builder.ToString();
        var java = _javaOutput;
        OutputBox.Text = java ? code.Replace("§", "\\u00A7") : code;
        CodeHint.Text = $"{text.Length} 字符 · " + (java ? "Java 转义" : "§ 原样") +
                        (Gradient ? $" · 渐变 {_stops.Count} 段" : "");
    }

    private static void AppendStyles(StringBuilder builder, bool bold, bool italic,
        bool underline, bool strike, bool obfuscated)
    {
        if (obfuscated) builder.Append("§k");
        if (bold) builder.Append("§l");
        if (italic) builder.Append("§o");
        if (underline) builder.Append("§n");
        if (strike) builder.Append("§m");
    }

    private static void ApplyStyle(Run run, bool bold, bool italic, bool underline, bool strike)
    {
        run.FontWeight = bold ? FontWeights.Bold : FontWeights.Normal;
        run.FontStyle = italic ? FontStyles.Italic : FontStyles.Normal;
        if (underline || strike)
        {
            var decorations = new TextDecorationCollection();
            if (underline) decorations.Add(TextDecorations.Underline[0]);
            if (strike) decorations.Add(TextDecorations.Strikethrough[0]);
            run.TextDecorations = decorations;
        }
    }

    private static Color PaletteColor(string code)
    {
        foreach (var (c, hex) in Palette)
            if (string.Equals(c, code, StringComparison.OrdinalIgnoreCase))
                return (Color)ColorConverter.ConvertFromString(hex);
        return Colors.White;
    }

    private static Color Lerp(Color from, Color to, double t) => Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * t),
        (byte)Math.Round(from.G + (to.G - from.G) * t),
        (byte)Math.Round(from.B + (to.B - from.B) * t));

    /// <summary>在多段颜色节点之间平滑取样（支持中间插入任意多种颜色）。</summary>
    private static Color SampleGradient(IReadOnlyList<Color> stops, double t)
    {
        if (stops.Count == 0) return Colors.White;
        if (stops.Count == 1) return stops[0];

        var scaled = Math.Clamp(t, 0, 1) * (stops.Count - 1);
        var index = Math.Min((int)Math.Floor(scaled), stops.Count - 2);
        return Lerp(stops[index], stops[index + 1], scaled - index);
    }
}
