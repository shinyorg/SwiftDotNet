using System;
using SwiftDotNet;
using SwiftDotNet.Controls;

namespace SwiftDotNet.Sample;

/// <summary>Inputs &amp; pickers: sliders, PIN, entries, color/country/duration pickers.</summary>
public sealed class InputsSample : View
{
    readonly State<double> _slider = State(0.4);
    readonly State<double> _rangeLo = State(0.25);
    readonly State<double> _rangeHi = State(0.75);
    readonly State<string> _pin = State("12");
    readonly State<string> _email = State("");
    readonly State<string> _fruit = State("");
    readonly State<double> _hue = State(200.0);
    readonly State<string> _picked = State("#00AAFF");
    readonly State<TimeSpan> _duration = State(TimeSpan.FromMinutes(90));

    static readonly string[] Fruits =
        { "Apple", "Apricot", "Banana", "Blueberry", "Cherry", "Grape", "Mango", "Orange", "Peach", "Pear" };

    public override View Body =>
        new ScrollView(new VStack(
            new Text("Sliders").Font(Font.Headline),
            new SwiftDotNet.Controls.Slider(_slider),
            new RangeSlider(_rangeLo, _rangeHi),

            new Text("PIN & Entry").Font(Font.Headline),
            new SecurityPin(_pin, length: 4),
            new TextEntry("Email", _email).Keyboard(KeyboardType.Email),
            new AutoCompleteEntry(_fruit, Fruits).Placeholder("Type a fruit…"),

            new Text("Color Picker").Font(Font.Headline),
            new SwiftDotNet.Controls.ColorPicker(_hue).OnColorChanged(hex => _picked.Value = hex),
            new Text(_picked.Value).Font(Font.Caption),

            new Text("Duration").Font(Font.Headline),
            new DurationPicker(_duration, seconds: true)
        ).Spacing(14).Alignment(HorizontalAlignment.Leading)
        ).Padding(20).NavigationTitle("Inputs");
}
