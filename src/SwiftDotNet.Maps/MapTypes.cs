namespace SwiftDotNet;

/// <summary>A WGS-84 geographic coordinate.</summary>
public readonly record struct MapCoordinate(double Latitude, double Longitude);

/// <summary>The map's viewport — where it's centered and how far it's zoomed (0 = world … ~20 = building).</summary>
public sealed record MapCamera(MapCoordinate Center, double Zoom);

/// <summary>A map annotation (marker). <paramref name="Id"/> lets the app correlate taps/drags back to a pin.</summary>
public sealed record MapPin(
    MapCoordinate Coordinate,
    string? Title = null,
    SwiftColor? Tint = null,
    bool Draggable = false,
    string? Id = null);

/// <summary>A line drawn through an ordered list of coordinates.</summary>
public sealed record MapPolyline(
    IReadOnlyList<MapCoordinate> Points,
    SwiftColor? Color = null,
    double Width = 3);
