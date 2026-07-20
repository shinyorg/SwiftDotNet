using Microsoft.UI.Xaml;
using SwiftDotNet;
using SwiftDotNet.Hosting;
using SwiftDotNet.Sample;

namespace SampleApp;

public static class Program
{
    [STAThread]
    static void Main() => Application.Start(_ => _ = new App());
}

public sealed class App : SwiftDotNetApplication
{
    protected override SwiftDotNetApp CreateSwiftApp() => SwiftProgram.CreateSwiftApp();
}
