using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using WebSample;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<AppRoot>("#app");
await builder.Build().RunAsync();
