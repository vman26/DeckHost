using DeckHost.App.ViewModels;
using DeckHost.Contracts.Models;

namespace DeckHost.Tests;

/// <summary>The Background Studio's layer list: what editing it reports and keeps.</summary>
public sealed class BackgroundStudioTests
{
    private const string SolidColor = "#336699";
    private const int OpacityPercent = 40;
    private const double OpacityFraction = 0.4;
    private const double Tolerance = 0.0001;
    private const int GradientAngle = 90;
    private const string ImagePath = "C:\\pic.png";
    private const string CpuVariable = "system.cpu";
    private const string UnknownKind = "hologram";
    private const double UnknownOpacity = 0.3;
    private const int FullOpacityPercent = 100;
    private const double BindingMin = 10;
    private const double BindingMax = 90;
    private const double DefaultBindingMax = 100;
    private const string Black = "#000000";
    private const string Red = "#FF0000";
    private const string Dark = "#101010";
    private const string LayoutDevice = "dev";
    private const double GlowFullAt = 60;

    private static BackgroundLayersViewModel Studio(out List<IReadOnlyList<BackgroundLayer>> reported, params BackgroundLayer[] layers)
    {
        var seen = new List<IReadOnlyList<BackgroundLayer>>();
        reported = seen;
        return new BackgroundLayersViewModel(layers, seen.Add);
    }

    [Test]
    public void AddSolid_ThenAddGradient_ReportsBothLayersInOrder()
    {
        // Arrange
        var studio = Studio(out var reported);

        // Act
        studio.AddSolidCommand.Execute(null);
        studio.AddGradientCommand.Execute(null);

        // Assert
        Assert.That(reported.Last().Select(static layer => layer.Kind), Is.EqualTo(new[] { BackgroundLayerKinds.Solid, BackgroundLayerKinds.Gradient }));
        Assert.That(studio.HasRows, Is.True);
    }

    [Test]
    public void EditRow_ColorAndOpacityOfASolidLayer_AreReported()
    {
        // Arrange
        var studio = Studio(out var reported, new BackgroundLayer(BackgroundLayerKinds.Solid));

        // Act
        studio.Rows[0].Color = SolidColor;
        studio.Rows[0].OpacityPercent = OpacityPercent;

        // Assert
        var layer = reported.Last()[0];
        Assert.That(layer.Color, Is.EqualTo(SolidColor));
        Assert.That(layer.Opacity, Is.EqualTo(OpacityFraction).Within(Tolerance));
    }

    [Test]
    public void EditRow_AngleOfAGradientLayer_IsReportedWithBothColors()
    {
        // Arrange
        var studio = Studio(out var reported, new BackgroundLayer(BackgroundLayerKinds.Gradient) { Color = Black, ToColor = Red });

        // Act
        studio.Rows[0].Angle = GradientAngle;

        // Assert
        var layer = reported.Last()[0];
        Assert.That(layer.Angle, Is.EqualTo(GradientAngle));
        Assert.That(layer.ToColor, Is.Not.Null, "a gradient keeps both colors");
    }

    [Test]
    public void EditRow_ASolidLayer_HasNoEndColorOrDirection()
    {
        // Arrange
        var studio = Studio(out var reported, new BackgroundLayer(BackgroundLayerKinds.Solid));

        // Act
        studio.Rows[0].Color = SolidColor;

        // Assert
        var layer = reported.Last()[0];
        Assert.That(layer.ToColor, Is.Null, "a solid layer has no end color");
        Assert.That(layer.Angle, Is.Null, "and no direction");
    }

    [Test]
    public void Move_ALayerDownThenUp_ReordersAndStopsAtTheTop()
    {
        // Arrange
        var studio = Studio(out var reported, new BackgroundLayer(BackgroundLayerKinds.Solid), new BackgroundLayer(BackgroundLayerKinds.Gradient));
        var first = studio.Rows[0];

        // Act
        studio.Move(first, 1);
        var afterMovingDown = reported.Last().Select(static layer => layer.Kind).ToList();
        studio.Move(studio.Rows[0], -1);

        // Assert
        Assert.That(afterMovingDown, Is.EqualTo(new[] { BackgroundLayerKinds.Gradient, BackgroundLayerKinds.Solid }));
        Assert.That(reported.Last()[0].Kind, Is.EqualTo(BackgroundLayerKinds.Gradient), "moving past the top does nothing");
    }

    [Test]
    public void Remove_TheFirstLayer_ReportsTheRest()
    {
        // Arrange
        var studio = Studio(out var reported, new BackgroundLayer(BackgroundLayerKinds.Gradient), new BackgroundLayer(BackgroundLayerKinds.Solid));

        // Act
        studio.Remove(studio.Rows[0]);

        // Assert
        Assert.That(reported.Last().Select(static layer => layer.Kind), Is.EqualTo(new[] { BackgroundLayerKinds.Solid }));
    }

    [Test]
    public void Load_AnotherList_ShowsItWithoutReportingAChange()
    {
        // Arrange
        var studio = Studio(out var reported);

        // Act
        studio.Load([new BackgroundLayer(BackgroundLayerKinds.Image) { ImagePath = ImagePath }]);

        // Assert
        Assert.That(studio.Rows, Has.Count.EqualTo(1));
        Assert.That(studio.Rows[0].ImagePath, Is.EqualTo(ImagePath));
        Assert.That(reported, Is.Empty);
    }

    [Test]
    public void Bind_AVariableWithNumbersAndColors_WritesTheBinding()
    {
        // Arrange
        var studio = Studio(out var reported, new BackgroundLayer(BackgroundLayerKinds.Solid) { Color = Black });
        var row = studio.Rows[0];

        // Act
        row.IsBound = true;
        row.Variable = $" {CpuVariable} ";
        row.Min = BindingMin.ToString();
        row.Max = BindingMax.ToString();
        row.FromColor = Black;
        row.BoundToColor = Red;

        // Assert
        var binding = reported.Last()[0].Binding!;
        Assert.That(binding.Variable, Is.EqualTo(CpuVariable));
        Assert.That((binding.Min, binding.Max), Is.EqualTo((BindingMin, BindingMax)));
        Assert.That((binding.FromColor, binding.ToColor), Is.EqualTo((Black, Red)));
    }

    [Test]
    public void ChangeTarget_FromColorToOpacity_DropsTheColors()
    {
        // Arrange
        var studio = Studio(out var reported, new BackgroundLayer(BackgroundLayerKinds.Solid) { Color = Black });
        var row = studio.Rows[0];
        row.IsBound = true;
        row.Variable = CpuVariable;
        row.FromColor = Black;
        row.BoundToColor = Red;

        // Act
        row.Target = BackgroundLayerRowViewModel.TargetOptions.Single(static option => option.Value == BackgroundBindingTargets.Opacity);

        // Assert
        var binding = reported.Last()[0].Binding!;
        Assert.That(binding.Target, Is.EqualTo(BackgroundBindingTargets.Opacity));
        Assert.That(binding.FromColor, Is.Null, "colors only belong to a color binding");
    }

    [Test]
    public void SetMax_AnUnreadableNumber_FallsBackToTheDefault()
    {
        // Arrange
        var studio = Studio(out var reported, new BackgroundLayer(BackgroundLayerKinds.Solid));
        var row = studio.Rows[0];
        row.IsBound = true;
        row.Variable = CpuVariable;

        // Act
        row.Max = "not a number";

        // Assert
        Assert.That(reported.Last()[0].Binding!.Max, Is.EqualTo(DefaultBindingMax));
    }

    [Test]
    public void UntickBound_ABoundLayer_RemovesTheBinding()
    {
        // Arrange
        var studio = Studio(out var reported, new BackgroundLayer(BackgroundLayerKinds.Solid));
        var row = studio.Rows[0];
        row.IsBound = true;
        row.Variable = CpuVariable;

        // Act
        row.IsBound = false;

        // Assert
        Assert.That(reported.Last()[0].Binding, Is.Null);
    }

    [Test]
    public void EditRow_ALayerOfAnUnknownKind_KeepsItExactlyAndOnlyHides()
    {
        // Arrange
        var unknown = new BackgroundLayer(UnknownKind, Opacity: UnknownOpacity) { Color = "#FFFFFF", Binding = new BackgroundBinding("vars.x") };
        var studio = Studio(out var reported, unknown);
        var row = studio.Rows[0];

        // Act
        row.OpacityPercent = FullOpacityPercent;
        row.Visible = false;

        // Assert
        Assert.That(row.IsKnown, Is.False);
        Assert.That(row.KindLabel, Does.Contain(UnknownKind));
        Assert.That(reported.Last()[0], Is.EqualTo(unknown with { Visible = false }), "nothing but Show changed");
    }

    [Test]
    public void AddCpuGlow_AddsALayerThatFollowsTheCpu()
    {
        // Arrange
        var studio = Studio(out var reported);

        // Act
        studio.AddCpuGlowCommand.Execute(null);

        // Assert
        var glow = reported.Last().Single();
        Assert.That(glow.Binding, Is.EqualTo(new BackgroundBinding(CpuVariable, BackgroundBindingTargets.Opacity, 0, GlowFullAt)));
    }

    [Test]
    public void ToModel_AProfileWithLayers_KeepsThemAndWithoutAnyHasNoBackground()
    {
        // Arrange
        var glow = new BackgroundLayer(BackgroundLayerKinds.Solid) { Binding = new BackgroundBinding(CpuVariable) };
        var profile = new ProfileItemViewModel("p", "Studio", LayoutDevice, null, new ProfileBackground(Dark) { Layers = [glow] });

        // Act
        var kept = profile.ToModel().Background!.Layers;
        profile.BackgroundLayers = [];
        profile.BackgroundColor = string.Empty;

        // Assert
        Assert.That(profile.BackgroundLayers, Is.Empty);
        Assert.That(kept, Is.EqualTo(new[] { glow }));
        Assert.That(profile.ToModel().Background, Is.Null, "no color, image or layers: no background");
    }
}
