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
        VersionLabel.Text = "Version " + AppVersion;
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

    /// <summary>
    /// The installed version: ClickOnce's deployment version when launched through ClickOnce,
    /// otherwise the assembly version (the publish scripts set it to the full 4-part version).
    /// </summary>
    public static string AppVersion
    {
        get
        {
            var clickOnce = Environment.GetEnvironmentVariable("ClickOnce_CurrentVersion");
            if (!string.IsNullOrWhiteSpace(clickOnce)) return clickOnce.Trim();
            var assembly = typeof(CreditsWindow).Assembly;
            var info = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info)) return info.Split('+')[0];
            return assembly.GetName().Version?.ToString() ?? "unknown";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
