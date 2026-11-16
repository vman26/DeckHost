# Writing a plugin

A DeckHost plugin adds **actions**: things a key, knob or touch zone can do and show. It's a .NET
library built against `DeckHost.Contracts` (MIT, so your plugin can use any license, free or paid),
packaged as a `.zip` with a `plugin.json`, and listed in the
[plugin catalog](plugin-store.md) so people find it in **Plugins → Store**.

Plugins for new **hardware** are different: see [Device plugins](device-plugins.md). Stream Deck and
OpenAction plugins (`.sdPlugin`) also run in DeckHost as they are; this guide is for DeckHost's own format.

## Start from the template

`plugins/DeckHost.Plugin.Obs` in the DeckHost repository is a complete plugin (OBS Studio over
obs-websocket). Copy it and rename. The project file is what matters:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>
  <ItemGroup>
    <!-- DeckHost.Contracts comes from DeckHost at run time: never ship a copy. -->
    <ProjectReference Include="..\..\DeckHost.Contracts\DeckHost.Contracts.csproj">
      <Private>false</Private>
      <ExcludeAssets>runtime</ExcludeAssets>
    </ProjectReference>
  </ItemGroup>
  <ItemGroup>
    <None Include="plugin.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

The template's `PackPluginPackage` target zips the build output into `artifacts/plugins/<id>.zip`
after every build.

## plugin.json

```json
{
  "id": "you.plugin.example",
  "name": "Example",
  "version": "1.0.0",
  "author": "You",
  "description": "What it does, in a sentence.",
  "assembly": "You.Plugin.Example.dll",
  "hostApiVersion": 1,
  "icon": "icon.png",
  "permissions": ["network"],
  "url": "https://example.com/docs",
  "repository": "https://github.com/you/plugin"
}
```

| Field | |
|---|---|
| `id` | 3–64 characters: lowercase letters, digits, `.`, `-`, `_`. Must match `IApplicationPlugin.Id`. |
| `version` | `1.2.0` style; the store and updates compare it. |
| `assembly` | The `.dll` at the package root with your `IApplicationPlugin`. |
| `hostApiVersion` | The plugin API version you built against (now 1). |
| `icon` | Optional image in the package, shown in the Plugins page and the store. |
| `url`, `repository` | Optional: your website or documentation, and your source code. They're on the plugin's documentation page in DeckHost, which also shows a `README.md` from your package (pictures in the package work) or else the README of a GitHub `repository`. |
| `permissions` | What the plugin does beyond drawing and reacting to input: `network`, `processes` (starts programs), `files`, `input` (presses keys, moves the mouse), `system` (volume, power, displays), `clipboard`. Shown to the user; not a sandbox. |

## The plugin and its actions

```csharp
public sealed class ExamplePlugin : IApplicationPlugin
{
    public string Id => "you.plugin.example";
    public string Name => "Example";
    public string Version => "1.0.0";
    public string? Author => "You";
    public IReadOnlyCollection<IDeckAction> Actions { get; } = [new HelloAction()];
}
```

DeckHost creates it with its public parameterless constructor. If it implements `IDisposable`,
`Dispose` runs when the plugin is disabled, updated or removed.

An action:

```csharp
public sealed class HelloAction : IDeckAction
{
    public string Id => "hello";                                   // unique within the plugin
    public string Name => "Say hello";                             // shown in the action list
    public string? Description => "Shows a greeting and counts presses.";
    public InputCapabilities RequiredInputs => InputCapabilities.Press;
    public OutputCapabilities RequiredOutputs => OutputCapabilities.None;

    public IReadOnlyList<ActionSettingDefinition> Settings { get; } =
    [
        new("name", "Name", Placeholder: "World"),
    ];

    public void Execute(ActionContext context)
    {
        if (context.EventType != InputCapabilities.Press) return;
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
```

`RequiredInputs` / `RequiredOutputs` decide which controls the action fits: `Press` for keys,
`Rotate` for knobs, `Swipe` for touch strips; `OutputCapabilities.None` for actions that work
without a screen, `ColorImage` when they need one of their own.

Everything else on `IDeckAction` has a default: `Settings`, `ShowInPalette`, `ListIcon` (SVG path
data for the action list), `CanBeMultiActionStep`, `RefreshInterval` (redraw every so often, for
clocks and readings), `OnAppear`/`OnDisappear` (the action came on or left a visible page) and a
settings page (`HasSettingsPage`, `GetSettingsPageUri`) for a web page instead of generated fields.

## Execute: fast, and slow work elsewhere

`Execute` (and `Render`) run on DeckHost's worker, one call at a time, and must return quickly.
Network calls, process starts and anything that waits go on a background task, which then asks for
a redraw:

```csharp
public void Execute(ActionContext context)
{
    _ = Task.Run(async () =>
    {
        try
        {
            await DoTheSlowThingAsync(context.Settings);
            context.Variables.Publish(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.LastError), "");
        }
        catch (Exception ex)
        {
            // Errors show on the control.
            context.Variables.Publish(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.LastError), ex.Message);
        }

        context.Host.RequestRender(context.DeviceId);
    });
}
```

`context.EventType` is what happened: `Press`, `Release`, `LongPress`, `Rotate` (with
`context.Value` the steps, negative to the left) or `Swipe` (`Value` -1 left, 1 right).
`context.Host` (`IActionHost`) also switches profiles and pages and opens folders.

## Settings

`ActionSettingDefinition(key, label, kind, defaultValue, description, options, placeholder)` adds a
field to the control's editor; the user's values come back in `context.Settings`
(`Get`, `GetInt`, `GetDouble`, `GetBool`).

| Kind | |
|---|---|
| `Text`, `Secret` | A text box; `Secret` hides what's typed (passwords, tokens). |
| `Number` | Give it a `Range` (`new NumberRange(0, 100) { Unit = "%" }`): a slider and a number box. |
| `Toggle` | A check box (`"true"`/`"false"`). |
| `Choice` | A drop-down of `ActionSettingOption(value, label)`; an option's `Requires` hides it on controls without that input. |
| `FilePath` | A text box with **Browse…**. |
| `Hotkey` | Records a key combination. |

- `ShowWhen = new SettingCondition("mode", "custom")` shows a field only when another field has one of those values.
- `PerInput = true` lets a field differ per input on a control that has several (press, hold, turn left/right, swipe): `context.Settings` then holds the value for the input that happened. `InputDefaults` gives per-input defaults.

## Render: draw through the context

Draw only with the `IRenderContext` you get; it's sized to the control (`Width`, `Height`). Useful calls:

- `DrawLabel(text)`: the title, in the user's text style (`TextStyle`). Use it for anything that is the action's name.
- `DrawCenteredText`, `DrawText`, `DrawRightAlignedText`, `FillRoundedRect`, `FillGradientRect`, `Clear`.
- `DrawIcon(svgPathData, x, y, size, color)`: icons as SVG path data on a 24×24 grid (`DeckHost.Contracts.Icons` has DeckHost's own).
- `DrawImageFile(path)`, `DrawEncodedImage(bytes)` (PNG, JPEG, GIF), `DrawImage(rgba, w, h)`.
- From `RenderContextExtensions`: `DrawTile` (an icon on a colored tile, like DeckHost's own actions) and `DrawReading` (a label, a value and a bar, laid out for keys and touch strips).
- `IsCompact` is true when the action is drawn as one line on a strip (a knob's action on the touch strip above it): draw a single line then.

The user's own text, image and background for the control are in the variable store under
`VariableKeys.AppearanceText`, `AppearanceImage` and `AppearanceBackground`; respect them.

## Variables

`context.Variables` is DeckHost's shared store. Build keys with `VariableKeys`, never by hand:
`VariableKeys.For(deviceId, controlId, suffix)` for a control (`LastError`, `Status`, `Counter`, …)
and `VariableKeys.UserVariable(name)` for the variables workflows set and check.

## Your plugin runs in its own process

DeckHost runs each plugin in a process of its own ([decision 0007](adr/0007-in-process-plugins.md)).
You don't need to do anything for it, but:

- `Render` is recorded and replayed in DeckHost. It's asked for again when the settings, the size or
  the control's variables change, when you call `RequestRender`, and at your `RefreshInterval`. Don't
  keep the context.
- Variables cross to DeckHost as strings, numbers, booleans and `DateTimeOffset`s; other objects stay
  in your process.
- A crash or an unhandled exception on a background thread ends your process; DeckHost restarts it
  and calls `OnAppear` again for what's on the devices. Don't rely on state only `Execute` built.
- Console output goes to DeckHost's log.

## Check, test and publish

```
DeckHost --check-plugin artifacts/plugins/you.plugin.example.zip
```

checks `plugin.json`, the files it names, the version and permissions, then loads the plugin in a
scratch folder (nothing is installed) and lists its actions. It exits with 1 on errors, so put it
in your plugin's CI. To try it for real: **Plugins → Install plugin…** and pick the zip; the Plugins
page shows warnings and lets you go **Back** to the previous version after an update.

To publish: release the zip (a GitHub release of your repository is best) and add it to the
[DeckHost plugin catalog](plugin-store.md#for-plugin-authors).

## Compatibility

A plugin built for host API version 1 keeps loading in every DeckHost that supports version 1: the
plugin API only grows ([decision 0003](adr/0003-plugin-api.md)). When it can't, DeckHost raises the
version and refuses older plugins with a clear message. The full surface is in the
[API reference](../reference/api.md).
