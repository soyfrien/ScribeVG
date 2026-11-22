using System.Windows;

namespace ScribeVG;

public partial class MainWindow
{
    private void Credits_Click(object sender, RoutedEventArgs e)
    {
        var window = new CreditsWindow(_settings.ReduceMotion) { Owner = this };
        window.ShowDialog();
    }
}
