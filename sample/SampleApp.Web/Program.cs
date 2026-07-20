using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SwiftDotNet;
using SwiftDotNet.Sample;
using WebSample;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<AppRoot>("#app");

// Blazor already owns a container, so SwiftDotNet reuses it rather than building a second one
// (DI plan §13.2): the shared registrations go straight into Blazor's collection.
SwiftProgram.AddSharedServices(builder.Services);

// Render the Map control as a MapLibre GL map on Web (the host page loads MapLibre — see index.html).
MapsWeb.UseMapLibre();

var host = builder.Build();

// Publish Blazor's provider as the app's ambient container, so [Inject] and Service<T>() resolve.
SwiftHost.Services = host.Services;

await host.RunAsync();
