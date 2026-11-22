using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace ScribeVG;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        TrySetInstalledAppsIcon();
        base.OnStartup(e);
    }

    /// <summary>
    /// ClickOnce puts the icon in the application manifest (used for the Start menu),
    /// but Windows Installed Apps often keeps a generic icon unless DisplayIcon is set
    /// on the user's Uninstall registry key. No-op if the icon or key isn't there.
    /// </summary>
    private static void TrySetInstalledAppsIcon()
    {
        try
        {
            var icon = Path.Combine(AppContext.BaseDirectory, "ScribeVG.ico");
            if (!File.Exists(icon))
            {
                icon = Path.Combine(AppContext.BaseDirectory, "Assets", "ScribeVG.ico");
            }
            if (!File.Exists(icon)) return;

            using var uninstall = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) return;

            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var key = uninstall.OpenSubKey(name, writable: true);
                if (key is null) continue;
                if (key.GetValue("DisplayName") is not string display) continue;
                if (!display.Equals("ScribeVG", StringComparison.OrdinalIgnoreCase) &&
                    !display.StartsWith("ScribeVG ", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (key.GetValue("DisplayIcon") is string current &&
                    string.Equals(current, icon, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                key.SetValue("DisplayIcon", icon);
                return;
            }
        }
        catch (Exception)
        {
            // Keep starting even if Installed Apps still shows the generic icon.
        }
    }
}
