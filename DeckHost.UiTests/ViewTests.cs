using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using DeckHost.App.Views;

namespace DeckHost.UiTests;

public sealed class ViewTests
{
    private static IEnumerable<Type> ViewTypes() =>
        typeof(MainWindow).Assembly.GetTypes()
            .Where(static type => type is { IsAbstract: false, IsPublic: true } && type.Namespace == "DeckHost.App.Views" &&
                                  typeof(Control).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(static type => type.Name);

    [AvaloniaTest]
    public void Every_view_loads_and_draws_a_frame_without_a_view_model()
    {
        var failures = new List<string>();
        Assert.That(ViewTypes().Count(), Is.GreaterThan(15), "the views were found");
        foreach (var type in ViewTypes())
        {
            try
            {
                var control = (Control)Activator.CreateInstance(type)!;
                var window = control as Window ?? new Window { Width = 900, Height = 600, Content = control };
                window.Show();
                using var frame = window.CaptureRenderedFrame();
                window.Close();
            }
            catch (Exception ex)
            {
                failures.Add($"{type.Name}: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
        }

        Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
    }
}
