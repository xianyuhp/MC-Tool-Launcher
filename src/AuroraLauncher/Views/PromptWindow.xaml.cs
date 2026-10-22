using System.Windows;
using System.Windows.Input;

namespace AuroraLauncher.Views;

public partial class PromptWindow : Window
{
    public string Value => InputBox.Text.Trim();

    public PromptWindow(string title, string hint, string initial = "")
    {
        InitializeComponent();
        TitleText.Text = title;
        HintText.Text = hint;
        InputBox.Text = initial;
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { DialogResult = true; Close(); }
            if (e.Key == Key.Escape) { DialogResult = false; Close(); }
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
