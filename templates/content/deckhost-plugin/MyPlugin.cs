using DeckHost.Contracts;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;

namespace MyPlugin;

/// <summary>The plugin: DeckHost creates it with this constructor and lists its actions.</summary>
public sealed class MyPluginPlugin : IApplicationPlugin
{
    public string Id => "you.plugin.example";

    public string Name => "MyPlugin";

    public string Version => "1.0.0";

    public string? Author => "PLUGIN_AUTHOR";

    public IReadOnlyCollection<IDeckAction> Actions { get; } = [new HelloAction()];
}

/// <summary>An action: greets someone and counts the presses. Put it on a key and press it.</summary>
public sealed class HelloAction : IDeckAction
{
    public string Id => "hello";

    public string Name => "Say hello";

    public string? Description => "Shows a greeting and counts presses.";

    public InputCapabilities RequiredInputs => InputCapabilities.Press;

    public OutputCapabilities RequiredOutputs => OutputCapabilities.None;

    public IReadOnlyList<ActionSettingDefinition> Settings { get; } =
    [
        new("name", "Name", Placeholder: "World"),
    ];

    // Execute and Render run on DeckHost's worker: keep them fast; slow work goes to a Task.
    public void Execute(ActionContext context)
    {
        if (context.EventType != InputCapabilities.Press)
        {
            return;
        }

        var key = VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.Counter);
        context.Variables.TryGetValue<int>(key, out var count);
        context.Variables.Publish(key, count + 1);
        context.Host.RequestRender(context.DeviceId);
    }

    public void Render(IRenderContext context)
    {
        context.Variables.TryGetValue<int>(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.Counter), out var count);
        context.DrawLabel($"Hello {context.Settings.Get("name", "World")}\n{count}");
    }
}
