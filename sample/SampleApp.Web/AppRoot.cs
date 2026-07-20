using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using SwiftDotNet;
using SwiftDotNet.Hosting;
using SwiftDotNet.Sample;

namespace WebSample;

/// <summary>
/// Root Blazor component — hosts the SAME shared root every other platform renders. The view is built
/// once and run through the shared lifecycle dispatch (which fills its [Inject] members), using Blazor's
/// own service provider.
/// </summary>
public sealed class AppRoot : ComponentBase
{
    // Fully qualified on purpose: SwiftDotNet.InjectAttribute and Blazor's InjectAttribute are both
    // named [Inject], so any file importing both namespaces must disambiguate.
    [Microsoft.AspNetCore.Components.Inject]
    public IServiceProvider Services { get; set; } = default!;

    View? _root;

    protected override void OnInitialized()
    {
        _root = new SampleRootView();
        ViewLifecycleDispatcher.Created(_root, Services);
        ViewLifecycleDispatcher.Appearing(_root, Services);
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<SwiftDotNetView>(0);
        builder.AddComponentParameter(1, nameof(SwiftDotNetView.Root), _root!);
        builder.CloseComponent();
    }
}
