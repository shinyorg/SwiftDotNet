using Microsoft.UI.Xaml;
using SwiftDotNet;

namespace SampleApp;

public static class Program
{
    [STAThread]
    static void Main() => Application.Start(_ => _ = new App());
}

public sealed class App : SwiftDotNetApplication
{
    protected override View CreateRoot() => AppRoot.Create();
}
