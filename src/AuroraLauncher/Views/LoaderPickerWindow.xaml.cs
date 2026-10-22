using System.Windows;
using System.Windows.Input;

namespace AuroraLauncher.Views;

public partial class LoaderPickerWindow : Window
{
    private readonly List<string> _values = new();

    public string SelectedVersion { get; private set; }

    public LoaderPickerWindow(string title, string hint, IEnumerable<(string Value, string Label)> items)
    {
        InitializeComponent();
        TitleText.Text = title;
        HintText.Text = hint;
        foreach (var (value, label) in items)
        {
            _values.Add(value);
            VersionList.Items.Add(label);
        }
        if (VersionList.Items.Count > 0) VersionList.SelectedIndex = 0;
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        DialogResult = false;
        Close();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Confirm();

    private void VersionList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Confirm();

    private void Confirm()
    {
        var index = VersionList.SelectedIndex;
        if (index < 0 || index >= _values.Count) return;
        SelectedVersion = _values[index];
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
