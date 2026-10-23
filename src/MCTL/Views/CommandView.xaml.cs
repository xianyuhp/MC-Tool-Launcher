using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MCTL.Core;
using MCTL.Core.Commands;

namespace MCTL.Views;

/// <summary>带图标的物品 / 生物条目。</summary>
public class IconEntry : INotifyPropertyChanged
{
    public string Id { get; init; } = "";
    /// <summary>中文名，显示在 ID 下方。</summary>
    public string Name { get; init; } = "";
    public bool IsMob { get; init; }

    private ImageSource _icon;
    public ImageSource Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    public override string ToString() => Id;
}

/// <summary>Minecraft Java 指令生成：版本适配 + give / summon / 全部指令。</summary>
public partial class CommandView : UserControl, IRefreshable
{
    private CommandVersion _version = CommandVersion.V1_21_5Plus;
    private bool _ready;

    private readonly List<IconEntry> _items = new();
    private readonly List<IconEntry> _mobs = new();
    private readonly List<(ComboBox Enchant, TextBox Level, Button Remove, Grid Row)> _enchantRows = new();
    private readonly List<(ComboBox Attr, TextBox Amount, Button Remove, Grid Row)> _giveAttributeRows = new();
    private readonly List<(ComboBox Attr, TextBox Amount, Button Remove, Grid Row)> _summonAttributeRows = new();
    private readonly Dictionary<string, TextBox> _catalogFields = new();
    private CommandSpec _catalogSpec;
    private readonly SemaphoreSlim _iconGate = new(4, 4);

    public CommandView()
    {
        InitializeComponent();
        HookInputBoxes();
        BuildVersions();
        BuildEntries();
        BuildCatalog();
        AddEnchantRow("sharpness", 5);
        _ready = true;
        GenerateGive();
        GenerateSummon();
    }

    /// <summary>数字 / 短参数输入框：聚焦即全选，输入的值会直接覆盖默认值。</summary>
    private void HookInputBoxes()
    {
        foreach (var box in new[] { GiveTarget, GiveCount, SummonPos, SummonHealth })
        {
            box.GotFocus += Value_GotFocus;
            box.PreviewMouseLeftButtonDown += Value_ClickSelect;
        }
    }

    private void Value_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box) box.SelectAll();
    }

    /// <summary>点击输入框时先拿到焦点再全选，避免首次点击被窗口激活吞掉。</summary>
    private void Value_ClickSelect(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox box || box.IsKeyboardFocusWithin) return;
        box.Focus();
        box.SelectAll();
    }

    public void Refresh()
    {
        if (!_ready) return;
        GenerateGive();
        GenerateSummon();
        GenerateCatalog();
    }

    private void BuildVersions()
    {
        foreach (var (version, label, _) in CommandVersionExtensions.All)
            VersionCombo.Items.Add(label);
        VersionCombo.SelectedIndex = CommandVersionExtensions.All.Length - 1;
        UpdateVersionUi();
    }

    private void UpdateVersionUi()
    {
        var info = CommandVersionExtensions.All.First(x => x.Version == _version);
        VersionHint.Text = info.Hint + "　" + CommandSyntax.ComponentHint(_version);
        EquipmentLabel.Text = _version.UsesEquipmentComponent()
            ? "装备（1.21.5+ 统一使用 equipment 组件）"
            : "装备（HandItems / ArmorItems）";
    }

    private void BuildEntries()
    {
        foreach (var id in GameData.Items)
            _items.Add(new IconEntry { Id = "minecraft:" + id, Name = GameData.ItemName(id) });
        foreach (var id in GameData.Mobs)
            _mobs.Add(new IconEntry { Id = "minecraft:" + id, Name = GameData.MobName(id), IsMob = true });

        FillItemList("");
        FillMobList("");
        _ = LoadIconsAsync(_items, false);
        _ = LoadIconsAsync(_mobs, true);
    }

    private async Task LoadIconsAsync(List<IconEntry> entries, bool mobs)
    {
        foreach (var entry in entries)
        {
            await _iconGate.WaitAsync();
            try
            {
                // 本地 jar 读取放到后台线程，避免一次性加载上百个图标时卡住界面
                entry.Icon = await Task.Run(async () => mobs
                    ? await GameIconService.GetSpawnEggAsync(entry.Id, allowNetwork: false)
                    : await GameIconService.GetItemAsync(entry.Id, allowNetwork: false));
            }
            catch { }
            finally
            {
                _iconGate.Release();
            }
        }
    }

    private void FillItemList(string keyword)
    {
        var selected = (ItemList.SelectedItem as IconEntry)?.Id;
        ItemList.Items.Clear();
        foreach (var entry in _items.Where(e =>
                     keyword.Length == 0 ||
                     e.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                     e.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            ItemList.Items.Add(entry);
        if (ItemList.Items.Count > 0)
        {
            var index = ItemList.Items.OfType<IconEntry>().ToList().FindIndex(e => e.Id == selected);
            ItemList.SelectedIndex = index >= 0 ? index : 0;
        }
    }

    private void FillMobList(string keyword)
    {
        var selected = (MobList.SelectedItem as IconEntry)?.Id;
        MobList.Items.Clear();
        foreach (var entry in _mobs.Where(e =>
                     keyword.Length == 0 ||
                     e.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                     e.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            MobList.Items.Add(entry);
        if (MobList.Items.Count > 0)
        {
            var index = MobList.Items.OfType<IconEntry>().ToList().FindIndex(e => e.Id == selected);
            MobList.SelectedIndex = index >= 0 ? index : 0;
        }
    }

    private void BuildCatalog()
    {
        CatalogList.Items.Clear();
        foreach (var spec in CommandCatalog.All.OrderBy(s => s.Name))
            CatalogList.Items.Add(spec);
        if (CatalogList.Items.Count > 0) CatalogList.SelectedIndex = 0;
    }


    private void Version_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (VersionCombo.SelectedIndex < 0) return;
        _version = CommandVersionExtensions.All[VersionCombo.SelectedIndex].Version;
        UpdateVersionUi();
        if (!_ready) return;
        GenerateGive();
        GenerateSummon();
        GenerateCatalog();
    }

    private void Tab_Changed(object sender, RoutedEventArgs e)
    {
        if (GivePanel == null || SummonPanel == null || CatalogPanel == null) return;
        GivePanel.Visibility = TabGive.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SummonPanel.Visibility = TabSummon.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        CatalogPanel.Visibility = TabCatalog.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        var target = TabGive.IsChecked == true ? GivePanel
            : TabSummon.IsChecked == true ? SummonPanel : CatalogPanel;
        UiAnim.FadeIn(target, 160);
    }

    private void ItemSearch_Changed(object sender, TextChangedEventArgs e) =>
        FillItemList((ItemSearch.Text ?? "").Trim());

    private void MobSearch_Changed(object sender, TextChangedEventArgs e) =>
        FillMobList((MobSearch.Text ?? "").Trim());

    private void Item_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (ItemList.SelectedItem is not IconEntry entry) return;
        GiveSelected.Text = entry.Id.Replace("minecraft:", "");
        GiveIcon.Source = entry.Icon;
        _ = LoadPreviewIconAsync(entry);
        GenerateGive();
    }

    private void Mob_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (MobList.SelectedItem is not IconEntry entry) return;
        MobSelected.Text = entry.Id.Replace("minecraft:", "");
        MobIcon.Source = entry.Icon;
        _ = LoadPreviewIconAsync(entry);
        GenerateSummon();
    }

    /// <summary>选中项再联网补一次高清图标（本机 jar 没有时才需要）。</summary>
    private async Task LoadPreviewIconAsync(IconEntry entry)
    {
        try
        {
            var icon = entry.IsMob
                ? await GameIconService.GetSpawnEggAsync(entry.Id)
                : await GameIconService.GetItemAsync(entry.Id);
            if (icon == null) return;
            entry.Icon = icon;
            if (entry.IsMob && (MobList.SelectedItem as IconEntry)?.Id == entry.Id) MobIcon.Source = icon;
            if (!entry.IsMob && (ItemList.SelectedItem as IconEntry)?.Id == entry.Id) GiveIcon.Source = icon;
        }
        catch { }
    }

    private void SummonOption_Changed(object sender, RoutedEventArgs e) => GenerateSummon();

    private void Option_Changed(object sender, RoutedEventArgs e) => GenerateGive();

    private void OptionText_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        GenerateGive();
        GenerateSummon();
    }

    private void AddEnchant_Click(object sender, RoutedEventArgs e) => AddEnchantRow("unbreaking", 3);

    private void AddEnchantRow(string enchant, int level)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var combo = new ComboBox { FontSize = 10, Height = 28 };
        foreach (var (id, label, max) in GameData.Enchantments)
            combo.Items.Add($"{label} ({id}) 1-{max}");
        var index = Array.FindIndex(GameData.Enchantments, e => e.Id == enchant);
        combo.SelectedIndex = Math.Max(0, index);
        Grid.SetColumn(combo, 0);
        row.Children.Add(combo);

        var levelBox = new TextBox
        {
            Style = (Style)FindResource("Input"),
            Height = 32,
            FontSize = 10,
            Text = level.ToString(),
            Margin = new Thickness(6, 0, 6, 0)
        };
        levelBox.GotFocus += Value_GotFocus;
        levelBox.PreviewMouseLeftButtonDown += Value_ClickSelect;
        Grid.SetColumn(levelBox, 1);
        row.Children.Add(levelBox);

        var remove = new Button
        {
            Content = "×",
            Style = (Style)FindResource("Btn"),
            Padding = new Thickness(8, 2, 8, 2),
            FontSize = 10,
            ToolTip = "移除这条附魔"
        };
        Grid.SetColumn(remove, 2);
        row.Children.Add(remove);

        void Refresh() => GenerateGive();
        combo.SelectionChanged += (_, _) => Refresh();
        levelBox.TextChanged += (_, _) => Refresh();

        var record = (combo, levelBox, remove, row);
        remove.Click += (_, _) =>
        {
            EnchantPanel.Children.Remove(row);
            _enchantRows.Remove(record);
            GenerateGive();
        };

        _enchantRows.Add(record);
        EnchantPanel.Children.Add(row);
        GenerateGive();
    }


    private void AddGiveAttribute_Click(object sender, RoutedEventArgs e) =>
        AddAttributeRow(GiveAttributePanel, _giveAttributeRows, "generic.armor", 20, GenerateGive);

    private void AddSummonAttribute_Click(object sender, RoutedEventArgs e) =>
        AddAttributeRow(SummonAttributePanel, _summonAttributeRows, "generic.armor", 20, GenerateSummon);

    /// <summary>添加一行「属性 + 数值」，例如盔甲值 20、攻击力 10。</summary>
    private void AddAttributeRow(StackPanel panel,
        List<(ComboBox Attr, TextBox Amount, Button Remove, Grid Row)> rows,
        string attributeId, double amount, Action refresh)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var combo = new ComboBox { FontSize = 10, Height = 32 };
        foreach (var (id, label) in GameData.Attributes) combo.Items.Add($"{label}（{id}）");
        var index = Array.FindIndex(GameData.Attributes, a => a.Id == attributeId);
        combo.SelectedIndex = Math.Max(0, index);
        Grid.SetColumn(combo, 0);
        row.Children.Add(combo);

        var amountBox = new TextBox
        {
            Style = (Style)FindResource("Input"),
            Height = 32,
            FontSize = 10,
            Text = amount.ToString("0.##"),
            Margin = new Thickness(6, 0, 6, 0)
        };
        amountBox.GotFocus += Value_GotFocus;
        amountBox.PreviewMouseLeftButtonDown += Value_ClickSelect;
        Grid.SetColumn(amountBox, 1);
        row.Children.Add(amountBox);

        var remove = new Button
        {
            Content = "×",
            Style = (Style)FindResource("Btn"),
            Padding = new Thickness(8, 2, 8, 2),
            FontSize = 10,
            ToolTip = "移除这条属性"
        };
        Grid.SetColumn(remove, 2);
        row.Children.Add(remove);

        combo.SelectionChanged += (_, _) => refresh();
        amountBox.TextChanged += (_, _) => refresh();

        var record = (combo, amountBox, remove, row);
        remove.Click += (_, _) =>
        {
            panel.Children.Remove(row);
            rows.Remove(record);
            refresh();
        };

        rows.Add(record);
        panel.Children.Add(row);
        refresh();
    }

    private static List<AttributeEntry> ReadAttributes(
        List<(ComboBox Attr, TextBox Amount, Button Remove, Grid Row)> rows)
    {
        var result = new List<AttributeEntry>();
        foreach (var (combo, amountBox, _, _) in rows)
        {
            if (combo.SelectedIndex < 0) continue;
            var id = GameData.Attributes[combo.SelectedIndex].Id;
            if (!double.TryParse(amountBox.Text, out var amount) || Math.Abs(amount) < 0.001) continue;
            result.Add(new AttributeEntry(id, amount));
        }
        return result;
    }

    private void GenerateGive()
    {
        if (!_ready || GiveOutput == null) return;

        var enchantments = new List<EnchantEntry>();
        foreach (var (combo, levelBox, _, _) in _enchantRows)
        {
            if (combo.SelectedIndex < 0) continue;
            var id = GameData.Enchantments[combo.SelectedIndex].Id;
            var level = int.TryParse(levelBox.Text, out var value) ? Math.Clamp(value, 1, 255) : 1;
            enchantments.Add(new EnchantEntry(id, level));
        }

        var item = (ItemList.SelectedItem as IconEntry)?.Id ?? "minecraft:stone";
        var options = new GiveOptions
        {
            Target = string.IsNullOrWhiteSpace(GiveTarget.Text) ? "@p" : GiveTarget.Text.Trim(),
            ItemId = item,
            Count = int.TryParse(GiveCount.Text, out var count) ? count : 1,
            Name = GiveName.Text ?? "",
            Lore = GiveLore.Text ?? "",
            Unbreakable = UnbreakableChk.IsChecked == true,
            Enchantments = enchantments,
            Attributes = ReadAttributes(_giveAttributeRows)
        };
        GiveOutput.Text = CommandSyntax.BuildGive(_version, options);
    }

    private void GenerateSummon()
    {
        if (!_ready || SummonOutput == null) return;

        var options = new SummonOptions
        {
            Target = string.IsNullOrWhiteSpace(SummonPos.Text) ? "~ ~ ~" : SummonPos.Text.Trim(),
            EntityId = (MobList.SelectedItem as IconEntry)?.Id ?? "minecraft:zombie",
            Name = SummonName.Text ?? "",
            Health = double.TryParse(SummonHealth.Text, out var health) && health > 0 ? health : null,
            Persistence = PersistenceChk.IsChecked == true,
            MainHand = MainHandBox.Text ?? "",
            OffHand = OffHandBox.Text ?? "",
            Head = HeadBox.Text ?? "",
            Chest = ChestBox.Text ?? "",
            Legs = LegsBox.Text ?? "",
            Feet = FeetBox.Text ?? "",
            Attributes = ReadAttributes(_summonAttributeRows)
        };
        SummonOutput.Text = CommandSyntax.BuildSummon(_version, options);
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyText(GiveOutput.Text);
    private void CopySummon_Click(object sender, RoutedEventArgs e) => CopyText(SummonOutput.Text);
    private void CopyCatalog_Click(object sender, RoutedEventArgs e) => CopyText(CatalogOutput.Text);

    private static void CopyText(string text)
    {
        try { Clipboard.SetText(text ?? ""); } catch { }
    }


    private void CatalogSearch_Changed(object sender, TextChangedEventArgs e)
    {
        var keyword = (CatalogSearch.Text ?? "").Trim();
        var selected = (CatalogList.SelectedItem as CommandSpec)?.Name;
        CatalogList.Items.Clear();
        foreach (var spec in CommandCatalog.All
                     .Where(s => keyword.Length == 0 ||
                                 s.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                                 s.Summary.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(s => s.Name))
            CatalogList.Items.Add(spec);
        if (CatalogList.Items.Count == 0) return;
        var index = CatalogList.Items.OfType<CommandSpec>().ToList().FindIndex(s => s.Name == selected);
        CatalogList.SelectedIndex = index >= 0 ? index : 0;
    }

    private void Catalog_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (CatalogList.SelectedItem is not CommandSpec spec) return;
        _catalogSpec = spec;
        CatalogTitle.Text = "/" + spec.Name;
        CatalogHint.Text = spec.Summary;
        CatalogFields.Children.Clear();
        _catalogFields.Clear();

        if (spec.Fields.Length == 0)
        {
            CatalogFields.Children.Add(new TextBlock
            {
                Text = "该指令不需要额外参数。",
                FontSize = 11,
                Foreground = (Brush)FindResource("SubTextBrush")
            });
        }

        foreach (var field in spec.Fields)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            panel.Children.Add(new TextBlock
            {
                Text = field.Label + (field.Hint.Length > 0 ? "（" + field.Hint + "）" : ""),
                FontSize = 10,
                Foreground = (Brush)FindResource("SubTextBrush")
            });
            var box = new TextBox
            {
                Style = (Style)FindResource("Input"),
                Height = 32,
                FontSize = 11,
                Text = field.Default
            };
            box.GotFocus += Value_GotFocus;
            box.PreviewMouseLeftButtonDown += Value_ClickSelect;
            box.TextChanged += (_, _) => GenerateCatalog();
            _catalogFields[field.Key] = box;
            panel.Children.Add(box);
            CatalogFields.Children.Add(panel);
        }
        GenerateCatalog();
    }

    private void GenerateCatalog()
    {
        if (!_ready || _catalogSpec == null || CatalogOutput == null) return;
        var values = _catalogFields.ToDictionary(pair => pair.Key, pair => pair.Value.Text ?? "");
        try
        {
            CatalogOutput.Text = _catalogSpec.Build(_version, values);
        }
        catch (Exception ex)
        {
            CatalogOutput.Text = "生成失败：" + ex.Message;
        }
    }
}
