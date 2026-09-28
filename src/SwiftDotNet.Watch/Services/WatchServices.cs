using System.Globalization;
using System.Text.Json;

namespace SwiftDotNet.Watch;

/// <summary>Taptic Engine patterns (<c>WKHapticType</c>).</summary>
public enum HapticType { Notification, DirectionUp, DirectionDown, Success, Failure, Retry, Start, Stop, Click }

/// <summary>Taptic Engine feedback (<c>WKInterfaceDevice.play</c>).</summary>
public static class Haptics
{
    public static void Play(HapticType type) =>
        WatchHost.CallNative("haptic", new Dictionary<string, object?> { ["type"] = type.ToString() });
}

/// <summary>A snapshot of the watch the app is running on.</summary>
/// <param name="BatteryLevel">0…1, or -1 when unknown (the simulator).</param>
public sealed record WatchDeviceInfo(
    string Name,
    string Model,
    string SystemName,
    string SystemVersion,
    double ScreenWidth,
    double ScreenHeight,
    double ScreenScale,
    double BatteryLevel,
    string BatteryState,
    bool IsSimulator);

/// <summary>
/// Information about the watch (<c>WKInterfaceDevice</c>). Use this rather than <c>OperatingSystem</c> /
/// <c>RuntimeInformation</c>: the runtime was compiled for iOS, so those report iOS on the watch.
/// </summary>
public static class WatchDevice
{
    public static WatchDeviceInfo Current
    {
        get
        {
            var e = WatchHost.CallNative("device") ?? throw new InvalidOperationException("Watch device info is unavailable.");
            return new WatchDeviceInfo(
                e.GetProperty("name").GetString() ?? "",
                e.GetProperty("model").GetString() ?? "",
                e.GetProperty("systemName").GetString() ?? "",
                e.GetProperty("systemVersion").GetString() ?? "",
                e.GetProperty("screenWidth").GetDouble(),
                e.GetProperty("screenHeight").GetDouble(),
                e.GetProperty("screenScale").GetDouble(),
                e.GetProperty("batteryLevel").GetDouble(),
                e.GetProperty("batteryState").GetString() ?? "unknown",
                e.GetProperty("isSimulator").GetBoolean());
        }
    }
}

/// <summary>Small persistent key/value settings (<c>UserDefaults</c>).</summary>
public static class Preferences
{
    public static string? Get(string key) =>
        WatchHost.CallNative("prefs.get", new Dictionary<string, object?> { ["key"] = key }) is { ValueKind: JsonValueKind.String } s
            ? s.GetString()
            : null;

    public static string Get(string key, string defaultValue) => Get(key) ?? defaultValue;

    public static int Get(string key, int defaultValue) =>
        int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : defaultValue;

    public static double Get(string key, double defaultValue) =>
        double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : defaultValue;

    public static bool Get(string key, bool defaultValue) => bool.TryParse(Get(key), out var v) ? v : defaultValue;

    public static void Set(string key, string? value) =>
        WatchHost.CallNative(value is null ? "prefs.remove" : "prefs.set",
            new Dictionary<string, object?> { ["key"] = key, ["value"] = value });

    public static void Set(string key, int value) => Set(key, value.ToString(CultureInfo.InvariantCulture));

    public static void Set(string key, double value) => Set(key, value.ToString("R", CultureInfo.InvariantCulture));

    public static void Set(string key, bool value) => Set(key, value.ToString());

    public static void Remove(string key) => Set(key, null);
}
