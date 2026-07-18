using SwiftDotNet;

namespace SwiftDotNet.Sample;

/// <summary>
/// A user-authored <b>composite</b> custom control — just a subclass of <see cref="View"/> that composes
/// existing views. No native code, no framework changes; renders on every backend (SwiftUI, Compose,
/// GTK, WinUI) automatically because it decomposes into primitives the interpreters already know.
/// </summary>
public sealed class Rating : View
{
    readonly State<int> _stars;
    readonly int _max;

    public Rating(State<int> stars, int max = 5)
    {
        _stars = stars;
        _max = max;
    }

    public override View Body
    {
        get
        {
            var items = new View[_max];
            for (var i = 0; i < _max; i++)
            {
                var n = i + 1; // capture
                items[i] = new Button(n <= _stars.Value ? "★" : "☆", () => _stars.Value = n)
                    .Font(Font.Title)
                    .ForegroundColor(n <= _stars.Value ? Color.Accent : Color.Secondary);
            }
            return new HStack(items).Spacing(4);
        }
    }
}
