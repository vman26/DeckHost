using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using DeckHost.UiTests;

[assembly: AvaloniaTestApplication(typeof(UiTestAppBuilder))]

namespace DeckHost.UiTests;

/// <summary>The application the headless tests run in: DeckHost's colors and the Fluent theme, without DeckHost's start-up.</summary>
public sealed class UiTestApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://DeckHost.App/")) { Source = new Uri("avares://DeckHost.App/Themes/Colors.axaml") });
        Styles.Add(new FluentTheme());
    }
}

public static class UiTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<UiTestApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
