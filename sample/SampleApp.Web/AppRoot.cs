using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using SwiftDotNet;
using SwiftDotNet.Sample;

namespace WebSample;

/// <summary>Root Blazor component — hosts the SAME shared ContentView every other platform renders.</summary>
public sealed class AppRoot : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<SwiftDotNetView>(0);
        builder.AddComponentParameter(1, nameof(SwiftDotNetView.Root), new ContentView());
        builder.CloseComponent();
    }
}
