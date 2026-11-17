using DeckHost.App.ViewModels;
using DeckHost.Contracts.Models;

namespace DeckHost.Tests;

/// <summary>The Background Studio's layer list: what editing it reports and keeps.</summary>
public sealed class BackgroundStudioTests
{
    private static BackgroundLayersViewModel Studio(out List<IReadOnlyList<BackgroundLayer>> reported, params BackgroundLayer[] layers)
    {
        var seen = new List<IReadOnlyList<BackgroundLayer>>();
        reported = seen;
        return new BackgroundLayersViewModel(layers, seen.Add);
    }

    [Test]
    public void Adding_editing_moving_and_removing_layers_each_report_the_new_list()
    {
        var studio = Studio(out var reported);
        Assert.That(studio.HasRows, Is.False);

        studio.AddSolidCommand.Execute(null);
        studio.AddGradientCommand.Execute(null);
        Assert.That(reported.Last().Select(static layer => layer.Kind), Is.EqualTo(new[] { BackgroundLayerKinds.Solid, BackgroundLayerKinds.Gradient }));
        Assert.That(studio.HasRows, Is.True);

        studio.Rows[0].Color = "#336699";
        studio.Rows[0].OpacityPercent = 40;
        studio.Rows[1].Angle = 90;
        var last = reported.Last();
        Assert.That(last[0].Color, Is.EqualTo("#336699"));
        Assert.That(last[0].Opacity, Is.EqualTo(0.4).Within(0.0001));
        Assert.That(last[1].Angle, Is.EqualTo(90));
        Assert.That(last[1].ToColor, Is.Not.Null, "a gradient keeps both colors");
        Assert.That(last[0].ToColor, Is.Null, "a solid layer has no end color");
        Assert.That(last[0].Angle, Is.Null, "and no direction");

        studio.Move(studio.Rows[0], 1);
        Assert.That(reported.Last().Select(static layer => layer.Kind), Is.EqualTo(new[] { BackgroundLayerKinds.Gradient, BackgroundLayerKinds.Solid }));
        studio.Move(studio.Rows[0], -1);
        Assert.That(reported.Last()[0].Kind, Is.EqualTo(BackgroundLayerKinds.Gradient), "moving past the top does nothing");

        studio.Remove(studio.Rows[0]);
        Assert.That(reported.Last().Select(static layer => layer.Kind), Is.EqualTo(new[] { BackgroundLayerKinds.Solid }));
    }

    [Test]
    public void Loading_another_list_shows_it_without_reporting_a_change()
    {
        var studio = Studio(out var reported);

        studio.Load([new BackgroundLayer(BackgroundLayerKinds.Image) { ImagePath = "C:\\pic.png" }]);

        Assert.That(studio.Rows, Has.Count.EqualTo(1));
        Assert.That(studio.Rows[0].ImagePath, Is.EqualTo("C:\\pic.png"));
        Assert.That(reported, Is.Empty);
    }

    [Test]
    public void A_variable_binding_is_written_with_its_numbers_and_colors_and_turned_off_by_the_checkbox()
    {
        var studio = Studio(out var reported, new BackgroundLayer(BackgroundLayerKinds.Solid) { Color = "#000000" });
        var row = studio.Rows[0];

        row.IsBound = true;
        row.Variable = " system.cpu ";
        row.Min = "10";
        row.Max = "90";
        row.FromColor = "#000000";
        row.BoundToColor = "#FF0000";

        var binding = reported.Last()[0].Binding!;
        Assert.That(binding.Variable, Is.EqualTo("system.cpu"));
        Assert.That((binding.Min, binding.Max), Is.EqualTo((10d, 90d)));
        Assert.That((binding.FromColor, binding.ToColor), Is.EqualTo(("#000000", "#FF0000")));

        row.Target = BackgroundLayerRowViewModel.TargetOptions.Single(static option => option.Value == BackgroundBindingTargets.Opacity);
        var opacity = reported.Last()[0].Binding!;
        Assert.That(opacity.Target, Is.EqualTo(BackgroundBindingTargets.Opacity));
        Assert.That(opacity.FromColor, Is.Null, "colors only belong to a color binding");

        row.Max = "not a number";
        Assert.That(reported.Last()[0].Binding!.Max, Is.EqualTo(100), "an unreadable number falls back to the default");

        row.IsBound = false;
        Assert.That(reported.Last()[0].Binding, Is.Null);
    }

    [Test]
    public void A_layer_of_an_unknown_kind_is_kept_exactly_and_can_only_be_hidden_or_removed()
    {
        var unknown = new BackgroundLayer("hologram", Opacity: 0.3) { Color = "#FFFFFF", Binding = new BackgroundBinding("vars.x") };
        var studio = Studio(out var reported, unknown);
        var row = studio.Rows[0];
        Assert.That(row.IsKnown, Is.False);
        Assert.That(row.KindLabel, Does.Contain("hologram"));

        row.OpacityPercent = 100;
        row.Visible = false;

        Assert.That(reported.Last()[0], Is.EqualTo(unknown with { Visible = false }), "nothing but Show changed");
    }

    [Test]
    public void The_cpu_glow_preset_is_a_layer_that_follows_the_cpu_and_a_profile_keeps_its_layers()
    {
        var studio = Studio(out var reported);
        studio.AddCpuGlowCommand.Execute(null);

        var glow = reported.Last().Single();
        Assert.That(glow.Binding, Is.EqualTo(new BackgroundBinding("system.cpu", BackgroundBindingTargets.Opacity, 0, 100)));

        var profile = new ProfileItemViewModel("p", "Studio", "dev", null, new ProfileBackground("#101010") { Layers = [glow] });
        Assert.That(profile.BackgroundLayers, Has.Count.EqualTo(1));
        Assert.That(profile.ToModel().Background!.Layers, Is.EqualTo(new[] { glow }));
        profile.BackgroundLayers = [];
        profile.BackgroundColor = string.Empty;
        Assert.That(profile.ToModel().Background, Is.Null, "no color, image or layers: no background");
    }
}
