using System.Reflection;
using SwiftDotNet.Preview;
using SwiftDotNet.Hosting;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// The parts of the preview host that can fail on their own: deciding *what* to preview, and parsing the
/// command line the IDE builds.
///
/// Root discovery is where a preview is won or lost. When it guesses wrong the developer sees someone
/// else's screen; when it fails it has to say why, in a sentence they can act on, because "could not
/// preview" is indistinguishable from a broken tool.
/// </summary>
public class PreviewHostTests
{
    static readonly Assembly Self = typeof(PreviewHostTests).Assembly;

    [Fact]
    public void Resolve_UsesTheNamedViewWhenOneIsGiven()
    {
        var result = PreviewRoot.Resolve(Self, typeof(PreviewProbeView).FullName);

        Assert.IsType<PreviewProbeView>(result.Root);
        Assert.Contains(nameof(PreviewProbeView), result.Description);
    }

    [Fact]
    public void Resolve_AcceptsTheShortTypeName()
    {
        // The IDE offers a list of views by simple name; making the developer paste a namespace to
        // preview their own screen would be a poor trade for a few lines of lookup.
        var result = PreviewRoot.Resolve(Self, nameof(PreviewProbeView));

        Assert.IsType<PreviewProbeView>(result.Root);
    }

    [Fact]
    public void Resolve_ExplainsItselfWhenTheTypeIsNotAView()
    {
        var ex = Assert.Throws<PreviewException>(
            () => PreviewRoot.Resolve(Self, typeof(PreviewHostTests).FullName));

        Assert.Contains("does not derive from View", ex.Message);
    }

    [Fact]
    public void Resolve_ExplainsItselfWhenTheViewNeedsConstructorArguments()
    {
        var ex = Assert.Throws<PreviewException>(
            () => PreviewRoot.Resolve(Self, typeof(PreviewNeedsServicesView).FullName));

        // Naming the way out matters more than naming the problem: this view *can* be previewed, just
        // through the DI front door rather than by construction.
        Assert.Contains("CreateSwiftApp", ex.Message);
    }

    [Fact]
    public void Resolve_ReportsAMissingType()
    {
        var ex = Assert.Throws<PreviewException>(() => PreviewRoot.Resolve(Self, "No.Such.View"));

        Assert.Contains("No.Such.View", ex.Message);
    }

    [Fact]
    public void Resolve_PrefersSwiftProgramOverScanningForViews()
    {
        // This assembly has several View subclasses *and* a SwiftProgram. The front door wins, because
        // going through it is what gives the preview the app's real container.
        var result = PreviewRoot.Resolve(Self, viewTypeName: null);

        Assert.Contains("CreateSwiftApp", result.Description);
        Assert.NotNull(result.Services);
        Assert.IsType<PreviewProbeView>(result.Root);
    }

    // ---- the command line ----------------------------------------------------------------------

    [Fact]
    public void Options_ParseTheFullCommandLine()
    {
        var assembly = Self.Location;

        var options = PreviewOptions.Parse([
            "--assembly", assembly,
            "--view", "ContentView",
            "--init", "Sample.Renderers.RegisterAll",
            "--port", "51234",
            "--width", "320",
            "--height", "640",
            "--dark",
            "--no-watch",
        ]);

        Assert.NotNull(options);
        Assert.Equal(Path.GetFullPath(assembly), options!.AssemblyPath);
        Assert.Equal("ContentView", options.ViewTypeName);
        Assert.Equal("Sample.Renderers.RegisterAll", options.Initializer);
        Assert.Equal(51234, options.Port);
        Assert.Equal(320, options.Width);
        Assert.Equal(640, options.Height);
        Assert.True(options.Dark);
        Assert.False(options.Watch);
    }

    [Fact]
    public void Options_DefaultToAPhoneSizedSurfaceAndAnEphemeralPort()
    {
        var options = PreviewOptions.Parse(["--assembly", Self.Location]);

        Assert.NotNull(options);
        Assert.Equal(0, options!.Port);
        Assert.Equal(390, options.Width);
        Assert.Equal(844, options.Height);
        Assert.True(options.Watch);
        Assert.Null(options.ViewTypeName);
    }

    [Fact]
    public void Options_RejectAnAssemblyThatIsNotThere()
        => Assert.Null(PreviewOptions.Parse(["--assembly", "/nope/missing.dll"]));

    [Fact]
    public void Options_RejectAnUnknownSwitch()
        => Assert.Null(PreviewOptions.Parse(["--assembly", Self.Location, "--wat"]));

    [Fact]
    public void Options_RejectASwitchWithNoValue()
        => Assert.Null(PreviewOptions.Parse(["--assembly"]));
}

/// <summary>A view the preview host can construct.</summary>
public sealed class PreviewProbeView : View
{
    public override View Body => new Text("preview probe");
}

/// <summary>A view it cannot — its dependencies have to come from somewhere.</summary>
public sealed class PreviewNeedsServicesView(string label) : View
{
    public override View Body => new Text(label);
}

/// <summary>
/// Stands in for an app's front door. Matched by shape (a static <c>CreateSwiftApp</c> returning a
/// <see cref="Hosting.SwiftDotNetApp"/>), which is exactly how the convention is documented.
/// </summary>
public static class SwiftProgram
{
    public static Hosting.SwiftDotNetApp CreateSwiftApp()
    {
        var builder = Hosting.SwiftDotNetApp.CreateBuilder();
        builder.UseSwiftApp(_ => new PreviewProbeView());
        return builder.Build();
    }
}
