using System.Globalization;

namespace SwiftDotNet;

/// <summary>A continuous value slider with two-way binding.</summary>
public sealed class Slider : View
{
    readonly State<double> _value;
    readonly double _min;
    readonly double _max;

    public Slider(State<double> value, double min = 0, double max = 1)
    {
        _value = value;
        _min = min;
        _max = max;
    }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("Slider", path);
        node.Props["value"] = _value.Value;
        node.Props["min"] = _min;
        node.Props["max"] = _max;
        ctx.RegisterAction(node.Id, v => _value.Value = double.Parse(v ?? "0", CultureInfo.InvariantCulture));
        return node;
    }
}

/// <summary>An increment/decrement stepper with two-way binding.</summary>
public sealed class Stepper : View
{
    readonly string _label;
    readonly State<int> _value;
    readonly int _min;
    readonly int _max;

    public Stepper(string label, State<int> value, int min = int.MinValue, int max = int.MaxValue)
    {
        _label = label;
        _value = value;
        _min = min;
        _max = max;
    }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("Stepper", path);
        node.Props["label"] = _label;
        node.Props["value"] = (double)_value.Value;
        node.Props["min"] = (double)_min;
        node.Props["max"] = (double)_max;
        ctx.RegisterAction(node.Id, v => _value.Value = (int)double.Parse(v ?? "0", CultureInfo.InvariantCulture));
        return node;
    }
}

/// <summary>A selection picker bound to the selected index.</summary>
public sealed class Picker : View
{
    readonly string _label;
    readonly State<int> _selection;
    readonly string[] _options;

    public Picker(string label, State<int> selection, params string[] options)
    {
        _label = label;
        _selection = selection;
        _options = options;
    }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("Picker", path);
        node.Props["label"] = _label;
        node.Props["selection"] = (double)_selection.Value;
        for (var i = 0; i < _options.Length; i++)
            node.Children.Add(new Text(_options[i]).ToNode(ctx, path + "." + i));
        ctx.RegisterAction(node.Id, v => _selection.Value = (int)double.Parse(v ?? "0", CultureInfo.InvariantCulture));
        return node;
    }
}

/// <summary>A date picker with two-way binding (dates cross the bridge as Unix epoch seconds).</summary>
public sealed class DatePicker : View
{
    static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    readonly string _label;
    readonly State<DateTime> _date;

    public DatePicker(string label, State<DateTime> date)
    {
        _label = label;
        _date = date;
    }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("DatePicker", path);
        node.Props["label"] = _label;
        node.Props["value"] = (_date.Value.ToUniversalTime() - Epoch).TotalSeconds;
        ctx.RegisterAction(node.Id, v =>
        {
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var secs))
                _date.Value = Epoch.AddSeconds(secs).ToLocalTime();
        });
        return node;
    }
}

/// <summary>A masked text field with two-way binding.</summary>
public sealed class SecureField : View
{
    readonly string _placeholder;
    readonly State<string> _text;

    public SecureField(string placeholder, State<string> text)
    {
        _placeholder = placeholder;
        _text = text;
    }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("SecureField", path);
        node.Props["placeholder"] = _placeholder;
        node.Props["text"] = _text.Value;
        ctx.RegisterAction(node.Id, v => _text.Value = v ?? "");
        return node;
    }
}

/// <summary>A multi-line text editor with two-way binding.</summary>
public sealed class TextEditor : View
{
    readonly State<string> _text;

    public TextEditor(State<string> text) => _text = text;

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("TextEditor", path);
        node.Props["text"] = _text.Value;
        ctx.RegisterAction(node.Id, v => _text.Value = v ?? "");
        return node;
    }
}

/// <summary>A color picker bound to a hex string (e.g. "#FF3B30").</summary>
public sealed class ColorPicker : View
{
    readonly string _label;
    readonly State<string> _hex;

    public ColorPicker(string label, State<string> hex)
    {
        _label = label;
        _hex = hex;
    }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("ColorPicker", path);
        node.Props["label"] = _label;
        node.Props["value"] = _hex.Value;
        ctx.RegisterAction(node.Id, v => _hex.Value = v ?? "#000000");
        return node;
    }
}

/// <summary>A pull-down menu of actions. Children are typically <see cref="Button"/>s.</summary>
public sealed class Menu : View
{
    readonly string _label;
    readonly View[] _children;

    public Menu(string label, params View[] children)
    {
        _label = label;
        _children = children;
    }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("Menu", path);
        node.Props["label"] = _label;
        NodeBuilder.AddChildren(node, _children, ctx, path);
        return node;
    }
}
