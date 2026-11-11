using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;

namespace DeckHost.Core.Devices;

/// <summary>
/// Represents a render job for a single control on the device.
/// </summary>
internal sealed record ControlRenderJob(
    string ControlId,
    ControlType ControlType,
    int Width,
    int Height,
    IDeckAction RenderAction);

internal sealed class ProfilePreviewAction(DeviceDefinition device, DeviceProfile profile) : IDeckAction
{
    public string Id => "deckhost.runtime.profile-preview";

    public string Name => "Profile Preview";

    public InputCapabilities RequiredInputs => InputCapabilities.None;

    public OutputCapabilities RequiredOutputs => OutputCapabilities.FullDisplay;

    public void Execute(ActionContext context)
    {
    }

    public void Render(IRenderContext context)
    {
        // Legacy full-display rendering (deprecated in favor of per-control)
        RenderFullDisplay(context);
    }

    /// <summary>
    /// Gets per-control render jobs for the current profile.
    /// Each control gets its own render job with the appropriate dimensions.
    /// </summary>
    public IReadOnlyList<ControlRenderJob> GetControlRenderJobs()
    {
        var jobs = new List<ControlRenderJob>();

        foreach (var control in device.Controls)
        {
            if (control.Outputs == OutputCapabilities.None)
                continue;

            // Determine render dimensions based on control type
            var (width, height) = control.Type switch
            {
                ControlType.TouchSurface => (device.ScreenWidth > 0 ? device.ScreenWidth : 800, 
                                            device.ScreenHeight > 0 ? device.ScreenHeight : 480),
                ControlType.Key => (device.Capabilities?.KeyWidth ?? 72, 
                                   device.Capabilities?.KeyHeight ?? 72),
                ControlType.Encoder => (64, 64),
                _ => (72, 72)
            };

            var renderAction = new ControlSingleRenderAction(device, profile, control);
            jobs.Add(new ControlRenderJob(control.Id, control.Type, width, height, renderAction));
        }

        return jobs;
    }

    private void RenderFullDisplay(IRenderContext context)
    {
        var layoutBounds = GetLayoutBounds(device);
        var scaleX = layoutBounds.Width <= 0 ? 1d : context.Width / layoutBounds.Width;
        var scaleY = layoutBounds.Height <= 0 ? 1d : context.Height / layoutBounds.Height;
        var scale = Math.Min(scaleX, scaleY);
        var offsetX = (context.Width - (layoutBounds.Width * scale)) / 2d;
        var offsetY = (context.Height - (layoutBounds.Height * scale)) / 2d;

        foreach (var control in device.Controls.Where(static control => control.Outputs != OutputCapabilities.None))
        {
            var binding = profile.Bindings.FirstOrDefault(candidate =>
                string.Equals(candidate.ControlId, control.Id, StringComparison.OrdinalIgnoreCase));

            var text = binding?.Appearance.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                text = binding?.Action.ActionName ?? control.DisplayName;
            }

            if (context.Variables.TryGetValue<string>($"devices.{profile.DeviceId}.{control.Id}.lastAction", out var lastAction) &&
                !string.IsNullOrWhiteSpace(lastAction))
            {
                text = text == lastAction ? $"{text} ✓" : $"{text} · {lastAction}";
            }

            if (context.Variables.TryGetValue<int>($"devices.{profile.DeviceId}.{control.Id}.executionCount", out var executionCount) && executionCount > 0)
            {
                text = $"{text} ({executionCount})";
            }

            if (!string.IsNullOrWhiteSpace(binding?.Appearance.ImagePath))
            {
                text = string.IsNullOrWhiteSpace(text)
                    ? $"[img] {Path.GetFileName(binding.Appearance.ImagePath)}"
                    : $"{text} · {Path.GetFileName(binding.Appearance.ImagePath)}";
            }

            var scaledX = offsetX + ((control.Bounds.X - layoutBounds.X) * scale);
            var scaledY = offsetY + ((control.Bounds.Y - layoutBounds.Y) * scale);
            var scaledWidth = Math.Max(44d, control.Bounds.Width * scale);
            var scaledHeight = Math.Max(44d, control.Bounds.Height * scale);
            var radius = Math.Min(scaledWidth, scaledHeight) * (control.Type == ControlType.Encoder ? 0.5d : 0.18d);

            context.FillRoundedRect(
                scaledX,
                scaledY,
                scaledWidth,
                scaledHeight,
                radius,
                control.Type == ControlType.TouchSurface ? 0xFF0F766Eu : 0xFF1D4ED8u);

            if (control.Type == ControlType.TouchSurface)
            {
                var lineOne = text;
                var lineTwo = context.Variables.TryGetValue<string>($"devices.{profile.DeviceId}.{control.Id}.lastActionAt", out var lastActionAt) && !string.IsNullOrWhiteSpace(lastActionAt)
                    ? lastActionAt
                    : control.DisplayName;

                context.DrawCenteredText(lineOne, scaledX + (scaledWidth / 2d), scaledY + (scaledHeight * 0.42d), (float)Math.Max(24d, Math.Min(scaledHeight * 0.16d, 42d)));
                context.DrawCenteredText(lineTwo, scaledX + (scaledWidth / 2d), scaledY + (scaledHeight * 0.72d), (float)Math.Max(18d, Math.Min(scaledHeight * 0.10d, 28d)));
                continue;
            }

            var keyLabel = text.Length > 10 ? text[..10] : text;
            context.DrawCenteredText(
                keyLabel,
                scaledX + (scaledWidth / 2d),
                scaledY + (scaledHeight / 2d),
                (float)Math.Max(18d, Math.Min(scaledHeight * 0.24d, 30d)));
        }
    }

    private static Rect GetLayoutBounds(DeviceDefinition definition)
    {
        var minX = definition.Controls.Min(static control => control.Bounds.X);
        var minY = definition.Controls.Min(static control => control.Bounds.Y);
        var maxX = definition.Controls.Max(static control => control.Bounds.X + control.Bounds.Width);
        var maxY = definition.Controls.Max(static control => control.Bounds.Y + control.Bounds.Height);
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }
}

/// <summary>
/// Renders a single control within the device profile.
/// Used per control for per-control rendering strategy.
/// </summary>
internal sealed class ControlSingleRenderAction(DeviceDefinition device, DeviceProfile profile, PhysicalControl control) : IDeckAction
{
    public string Id => "deckhost.runtime.control-render";
    public string Name => "Control Render";
    public InputCapabilities RequiredInputs => InputCapabilities.None;
    public OutputCapabilities RequiredOutputs => control.Outputs;

    public void Execute(ActionContext context) { }

    public void Render(IRenderContext context)
    {
        var binding = profile.Bindings.FirstOrDefault(candidate =>
            string.Equals(candidate.ControlId, control.Id, StringComparison.OrdinalIgnoreCase));

        var text = binding?.Appearance.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            text = binding?.Action.ActionName ?? control.DisplayName;
        }

        if (context.Variables.TryGetValue<string>($"devices.{profile.DeviceId}.{control.Id}.lastAction", out var lastAction) &&
            !string.IsNullOrWhiteSpace(lastAction))
        {
            text = text == lastAction ? $"{text} ✓" : $"{text} · {lastAction}";
        }

        if (context.Variables.TryGetValue<int>($"devices.{profile.DeviceId}.{control.Id}.executionCount", out var executionCount) && executionCount > 0)
        {
            text = $"{text} ({executionCount})";
        }

        if (!string.IsNullOrWhiteSpace(binding?.Appearance.ImagePath))
        {
            text = string.IsNullOrWhiteSpace(text)
                ? $"[img] {Path.GetFileName(binding.Appearance.ImagePath)}"
                : $"{text} · {Path.GetFileName(binding.Appearance.ImagePath)}";
        }

        // Clear background
        context.FillRoundedRect(0, 0, context.Width, context.Height, 4, 0xFF1D4ED8u);

        if (control.Type == ControlType.TouchSurface)
        {
            var lineOne = text;
            var lineTwo = context.Variables.TryGetValue<string>($"devices.{profile.DeviceId}.{control.Id}.lastActionAt", out var lastActionAt) && !string.IsNullOrWhiteSpace(lastActionAt)
                ? lastActionAt
                : control.DisplayName;

            context.DrawCenteredText(lineOne, context.Width / 2d, context.Height * 0.42d, (float)Math.Max(24d, Math.Min(context.Height * 0.16d, 42d)));
            context.DrawCenteredText(lineTwo, context.Width / 2d, context.Height * 0.72d, (float)Math.Max(18d, Math.Min(context.Height * 0.10d, 28d)));
            return;
        }

        var keyLabel = text.Length > 10 ? text[..10] : text;
        context.DrawCenteredText(
            keyLabel,
            context.Width / 2d,
            context.Height / 2d,
            (float)Math.Max(18d, Math.Min(context.Height * 0.24d, 30d)));
    }
}
