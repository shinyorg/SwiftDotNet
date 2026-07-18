namespace SampleApp.Skia.Maui;

public class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
        => new(new MainPage()) { Title = "SwiftDotNet · Skia + Shiny" };
}
