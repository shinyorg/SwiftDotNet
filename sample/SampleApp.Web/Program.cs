using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SwiftDotNet;
using WebSample;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<AppRoot>("#app");

// Render the Map control as a MapLibre GL map on Web (the host page loads MapLibre — see index.html).
MapsWeb.UseMapLibre();

await builder.Build().RunAsync();
