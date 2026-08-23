namespace SwiftDotNet;

/// <summary>
/// Reads the Windows "apps" light/dark setting.
/// </summary>
/// <remarks>
/// WinForms exposes no theme API of its own (unlike WinUI's <c>ActualTheme</c> or MAUI's <c>AppTheme</c>), so
/// the setting is read from the same registry value the shell itself uses. It is <c>internal</c> because
/// the sibling WPF host declares the same helper, and a consumer referencing both assemblies would
/// otherwise get an ambiguous <c>WindowsTheme</c> in the shared <c>SwiftDotNet</c> namespace.
/// </remarks>
static class WindowsTheme
{
    const string Key = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>True when Windows is in dark app mode. Falls back to light if the value is unreadable.</summary>
    public static bool IsDark()
    {
        try
        {
            // 0 = dark, 1 = light. Absent on older builds, where light is the only mode.
            return Microsoft.Win32.Registry.GetValue(Key, "AppsUseLightTheme", 1) is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }
}
