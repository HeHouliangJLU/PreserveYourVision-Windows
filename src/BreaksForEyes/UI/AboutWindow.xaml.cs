using System.Reflection;
using System.Windows;

namespace BreaksForEyes.UI;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null
            ? "Windows 版"
            : $"Windows 版 v{version.Major}.{version.Minor}.{version.Build} · .NET 10 / WPF";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
