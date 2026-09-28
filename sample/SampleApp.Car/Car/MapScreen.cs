using SampleApp.Car.Services;
using SwiftDotNet;
using SwiftDotNet.Graphics;
using GColor = SwiftDotNet.Graphics.Color;
using GPoint = SwiftDotNet.Graphics.Point;

namespace SampleApp.Car;

/// <summary>
/// App-drawn map. The same <see cref="StationMap"/> renders into CarPlay's CPWindow and Android Auto's
/// Surface; pan/zoom gestures and map buttons mutate it and invalidate.
/// </summary>
public sealed class MapScreen(StationService stations) : StationScreen(stations)
{
    readonly StationMap map = new(stations);

    public override CarTemplate Build()
    {
        map.Stations = Stations.Filtered();
        return new CarMapTemplate
        {
            Title = "Map",
            Draw = map.Draw,
            MapButtons =
            [
                new CarAction("Zoom in", () => SetState(() => map.Zoom(1.5f))) { Image = CarIcons.ZoomIn },
                new CarAction("Zoom out", () => SetState(() => map.Zoom(1 / 1.5f))) { Image = CarIcons.ZoomOut },
                new CarAction("Recenter", () => SetState(map.Recenter)) { Image = CarIcons.Recenter }
            ],
            OnPan = (dx, dy) => SetState(() => map.Pan(dx, dy)),
            OnZoom = (factor, _) => SetState(() => map.Zoom(factor)),
            OnTap = point =>
            {
                if (map.HitTest(point) is { } station)
                    ShowDetails(station);
            },
            // Android Auto: shown beside the map. CarPlay: a "Nearest" bar button that pushes this list.
            Content = new CarListTemplate
            {
                Title = "Nearest",
                Sections = [new CarListSection { Items = map.Stations.Take(4).Select(StationRow).ToList() }],
                EmptyMessage = "No stations match your filters"
            }
        };
    }
}

/// <summary>
/// A tiny vector "map": street grid, water, station pins and the car. Pure <see cref="ICanvas"/> — the same
/// renderer seam the self-drawing engine paints through — so it draws identically into CarPlay's window and
/// Android Auto's surface.
/// </summary>
public sealed class StationMap(StationService service)
{
    static readonly (double Lat, double Lon) Home = (43.6480, -79.3850);
    const double KmPerDegreeLat = 111.0;
    static readonly double KmPerDegreeLon = 111.0 * Math.Cos(Home.Lat * Math.PI / 180);

    static readonly GColor Land = Hex("#111827"), Water = Hex("#0B3B5C"), Street = Hex("#1F2937"),
        Arterial = Hex("#374151"), Car = Hex("#3B82F6"), White = Hex("#FFFFFF"), Black = Hex("#000000");

    (double Lat, double Lon) center = Home;
    float pixelsPerKm = 70;
    Rect lastBounds;
    readonly List<(Station Station, GPoint Point)> pins = [];

    public IReadOnlyList<Station> Stations { get; set; } = [];

    public void Zoom(float factor) => pixelsPerKm = Math.Clamp(pixelsPerKm * factor, 15, 600);

    public void Pan(float dx, float dy)
    {
        center.Lon -= dx / pixelsPerKm / KmPerDegreeLon;
        center.Lat += dy / pixelsPerKm / KmPerDegreeLat;
    }

    public void Recenter()
    {
        center = Home;
        pixelsPerKm = 70;
    }

    public Station? HitTest(GPoint point) =>
        pins.Where(p => Distance(p.Point, point) < 28).OrderBy(p => Distance(p.Point, point)).Select(p => p.Station).FirstOrDefault();

    static float Distance(GPoint a, GPoint b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    GPoint Project(double lat, double lon) => new(
        lastBounds.MidX + (float)((lon - center.Lon) * KmPerDegreeLon * pixelsPerKm),
        lastBounds.MidY - (float)((lat - center.Lat) * KmPerDegreeLat * pixelsPerKm));

    public void Draw(CarMapDrawContext ctx)
    {
        var canvas = ctx.Canvas;
        var bounds = ctx.Bounds;
        lastBounds = bounds;
        canvas.DrawRect(bounds, Paint.Fill(Land));

        // lake (everything south of ~43.638)
        var shore = Project(43.638, center.Lon).Y;
        if (shore < bounds.Bottom)
            canvas.DrawRect(new Rect(bounds.Left, shore, bounds.Right, bounds.Bottom), Paint.Fill(Water));

        // street grid every ~400 m
        var street = Paint.Stroke(Street, Math.Max(1, pixelsPerKm / 40));
        var step = 0.4f * pixelsPerKm;
        var origin = Project(Home.Lat, Home.Lon);
        var landBottom = Math.Min(shore, bounds.Bottom);
        for (var x = Mod(origin.X, step); x < bounds.Right; x += step)
            canvas.DrawLine(x, bounds.Top, x, landBottom, street);
        for (var y = Mod(origin.Y, step); y < landBottom; y += step)
            canvas.DrawLine(bounds.Left, y, bounds.Right, y, street);

        // arterial
        var a = Project(43.6405, -79.43);
        var b = Project(43.6455, -79.35);
        canvas.DrawLine(a.X, a.Y, b.X, b.Y, Paint.Stroke(Arterial, Math.Max(3, pixelsPerKm / 14)) with { StrokeCap = StrokeCap.Round });

        // pins
        pins.Clear();
        var font = ctx.Fonts.Get(12, bold: false);
        var bold = ctx.Fonts.Get(12, bold: true);
        foreach (var station in Stations)
        {
            var p = Project(station.Latitude, station.Longitude);
            pins.Add((station, p));
            var available = service.Available(station);
            canvas.DrawCircle(p.X, p.Y, 12, Paint.Fill(CarColors.Resolve(CarIcons.AvailabilityColor(available, station.Total))));
            canvas.DrawCircle(p.X, p.Y, 12, Paint.Stroke(White, 2));
            var count = available.ToString();
            canvas.DrawText(count, p.X - ctx.Fonts.Measure(count, bold) / 2, p.Y + 4, bold, Black);
            canvas.DrawText(station.Name, p.X + 16, p.Y + 4, font, White);
        }

        // the car
        var car = Project(Home.Lat, Home.Lon);
        canvas.DrawCircle(car.X, car.Y, 22, Paint.Fill(Car.WithAlpha(64)));
        canvas.DrawCircle(car.X, car.Y, 8, Paint.Fill(Car));

        // scale bar
        var left = bounds.Left + 16;
        var bottom = bounds.Bottom - 16;
        canvas.DrawLine(left, bottom, left + pixelsPerKm, bottom, Paint.Stroke(White, 2));
        const string label = "1 km";
        canvas.DrawText(label, left + (pixelsPerKm - ctx.Fonts.Measure(label, font)) / 2, bottom - 6, font, White);
    }

    static float Mod(float value, float step) => ((value % step) + step) % step;

    static GColor Hex(string hex) => GColor.TryParse(hex, out var c) ? c : default;
}
