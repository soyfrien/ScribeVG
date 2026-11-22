using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Navigation;

namespace ScribeVG;

public partial class CreditsWindow : Window
{
    private readonly bool _reduceMotion;

    public CreditsWindow(bool reduceMotion = false)
    {
        _reduceMotion = reduceMotion;
        InitializeComponent();
        if (!_reduceMotion)
        {
            Opacity = 0;
            Loaded += FadeIn;
        }
    }

    private void FadeIn(object sender, RoutedEventArgs e)
    {
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
