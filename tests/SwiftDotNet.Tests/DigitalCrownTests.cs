using System.Text.Json;
using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// Wire contract for <c>.DigitalCrownRotation(…)</c> — the rotary modifier every host (watchOS Digital Crown,
/// Wear OS rotating button/bezel) interprets identically.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class DigitalCrownTests
{
    [Fact]
    public void DigitalCrown_SerializesValueRangeStepAndEvent()
    {
        var bridge = new CapturingCrownBridge();
        SwiftApp.Run(new CrownHost(new VStack().DigitalCrownRotation(42, _ => { }, from: 0, through: 100, by: 1)), bridge);

        var op = JsonDocument.Parse(bridge.LastJson!).RootElement.GetProperty("ops").EnumerateArray().First();
        var mod = op.GetProperty("node").GetProperty("modifiers").EnumerateArray()
            .First(m => m.GetProperty("type").GetString() == "digitalCrown");

        Assert.Equal(42, mod.GetProperty("value").GetDouble());
        Assert.Equal(0, mod.GetProperty("from").GetDouble());
        Assert.Equal(100, mod.GetProperty("through").GetDouble());
        Assert.Equal(1, mod.GetProperty("by").GetDouble());
        Assert.Equal("0$0", mod.GetProperty("event").GetString());
    }

    [Fact]
    public void DigitalCrown_ParsesInvariantCultureValue()
    {
        double? got = null;
        var bridge = new FiringCrownBridge();
        SwiftApp.Run(new CrownHost(new VStack().DigitalCrownRotation(0, v => got = v)), bridge);

        bridge.Fire("0$0", "0.75");
        Assert.Equal(0.75, got);

        got = null;
        bridge.Fire("0$0", "not-a-number");
        Assert.Null(got);
    }
}

file sealed class CrownHost(View child) : View
{
    public override View Body => child;
}

file sealed class CapturingCrownBridge : IBridge
{
    public string? LastJson { get; private set; }
    public void SetEventHandler(Action<string, string?> handler) { }
    public void Render(string json) => LastJson = json;
}

file sealed class FiringCrownBridge : IBridge
{
    Action<string, string?>? _handler;
    public void SetEventHandler(Action<string, string?> handler) => _handler = handler;
    public void Render(string json) { }
    public void Fire(string id, string? value) => _handler!(id, value);
}
