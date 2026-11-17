# Plugin API reference

Everything in `DeckHost.Contracts` (MIT), the library plugins are built against. Start with [Writing a plugin](../maintainers/plugins.md) or [Device plugins](../maintainers/device-plugins.md).

*Generated from the code and its XML docs (`ReferenceDocsTests`); don't edit by hand.*

## DeckHost.Contracts

### Icons

*static class* — DeckHost's own icons as SVG path data on a 24×24 grid, filled with the even-odd rule, for `DrawIcon` and `DrawTile`. Plugins may use them, or pass their own path data on the same grid.

| Member | |
|---|---|
| `const string Apps` |  |
| `const string Back` | An arrow back (out of a folder). |
| `const string Bolt` | A lightning bolt (a workflow trigger). |
| `const string Branch` | A decision diamond. |
| `const string Chip` |  |
| `const string Clock` | A clock face with hands. |
| `const string Counter` |  |
| `const string Folder` | A folder. |
| `const string KeyLogic` | A key with one, two and a long press, for Key Logic. |
| `const string Keyboard` |  |
| `const string Lock` |  |
| `const string Muted` |  |
| `const string Next` |  |
| `const string OpenLink` |  |
| `const string PageNext` | Pages with an arrow to the next one. |
| `const string PagePrevious` | Pages with an arrow to the previous one. |
| `const string Play` |  |
| `const string PlayPause` |  |
| `const string Previous` |  |
| `const string Repeat` |  |
| `const string Speaker` |  |
| `const string Stack` |  |
| `const string Stop` |  |
| `const string Swipe` | A hand swiping left and right. |
| `const string SwitchProfile` |  |
| `const string Text` |  |
| `const string Timer` | An hourglass. |
| `const string Toggle` | A switch, for a Multi Action Switch. |
| `const string Variable` | Brackets around an x. |
| `const string VolumeDown` |  |
| `const string VolumeUp` |  |
| `const string Workflow` | Two nodes and a link. |

### RenderContextExtensions

*static class* — Ready-made layouts for actions, built on `IRenderContext`.

| Member | |
|---|---|
| `static void DrawReading(IRenderContext context, string label, string value, double? fraction = null, uint barColor = 0xFF93C5FD)` | Draws a live reading (a volume, a CPU load, a dial's value): a small label, a large value and, with a `fraction` from 0 to 1, a level bar. In a strip (`IsCompact`) it becomes one line. |
| `static void DrawReading(IRenderContext context, string label, string value, double? fraction, uint barColor, string icon)` | `DrawReading` with an icon (`Icons`). On a touch zone it is laid out like a Stream Deck + dial: the label small at the top left, the icon on the left, the value on the right with a thin bar under it. |
| `static void DrawTile(IRenderContext context, string icon, uint fromArgb, uint toArgb, string caption = null)` | Draws an action as an icon. On a key: a rounded tile with a diagonal gradient from `fromArgb` to `toArgb` and the icon in white, the `caption` (if any) small at the bottom in the binding's text style. With both colors 0 there is no tile (the control's own background shows). On a touch zone the strip stays one surface: no tile, the caption at the top left and the icon in the middle. In a strip (`IsCompact`) the icon and the caption on one line. |
| `static void DrawTile(IRenderContext context, string icon, uint fromArgb, uint toArgb, string caption, uint? iconArgb)` | `DrawTile` with the icon's color: without a tile (both colors 0) a colored icon reads better than a white one. |
| `static uint TextColorOr(IRenderContext context, uint fallback)` | The binding's text color (Customize → Text style) as ARGB, else `fallback`. |

### VariableKeys

*static class* — Shared variable-store key conventions. Actions and the renderer talk to each other through these keys, so always build them here instead of with string literals.

| Member | |
|---|---|
| `const string AppearanceBackground` |  |
| `const string AppearanceIcon` |  |
| `const string AppearanceImage` |  |
| `const string AppearanceText` |  |
| `const string AppearanceTile` |  |
| `const string AppearanceTileStyle` |  |
| `const string Counter` |  |
| `const string ExecutionCount` |  |
| `const string ForegroundApp` | The name of the app in the foreground (where the platform reports it). |
| `const string HardwareId` |  |
| `const string LastAction` |  |
| `const string LastActionAt` |  |
| `const string LastControl` |  |
| `const string LastError` |  |
| `const string LastEvent` |  |
| `const string LastValue` |  |
| `const string Status` |  |
| `const string SwitchState` | A Multi Action Switch's set that the next press runs (`First` or `Second`). |
| `const string WorkflowState` | Prefix of a workflow's own state under a control (e.g. which output a Toggle node takes next). |
| `static string Control(string deviceId, string controlId)` | `devices.{deviceId}.{controlId}.` |
| `static string Device(string deviceId)` | `devices.{deviceId}.` |
| `static string For(string deviceId, string controlId, string suffix)` |  |
| `static string UserVariable(string name)` | `vars.{name}`: a variable that workflows set and check (names ignore case). |

## DeckHost.Contracts.Enums

### InputCapabilities

*enum*

| Member | |
|---|---|
| `None` |  |
| `Press` |  |
| `LongPress` |  |
| `Release` |  |
| `Rotate` |  |
| `Touch` |  |
| `Swipe` |  |
| `All` |  |

### OutputCapabilities

*enum*

| Member | |
|---|---|
| `None` |  |
| `ColorImage` |  |
| `Text` |  |
| `SegmentLed` |  |
| `FullDisplay` |  |
| `All` |  |

## DeckHost.Contracts.Interfaces

### DeviceSettingAction

*record* — A button on a device's settings page.

| Member | |
|---|---|
| `DeviceSettingAction(string Id, string Label, string Description = null, string Confirm = null)` | A button on a device's settings page. |
| `string Confirm { get; init; }` | When set, DeckHost asks this question before running it. |
| `string Description { get; init; }` | One line under the button: what it does. |
| `string Id { get; init; }` |  |
| `string Label { get; init; }` |  |

### IActionHost

*interface* — Services the host offers to actions. Thread-safe: actions may call these from background work (for example after an async request finished).

| Member | |
|---|---|
| `void ActivatePage(string deviceId, int number)` | Shows page `number` (from 1) of the active profile. |
| `void ActivateProfile(string deviceId, string profileIdOrName)` | Activates a profile of the device by id or (case-insensitive) name. |
| `void ActivateRelativePage(string deviceId, int offset)` | Shows the next (+1) or previous (-1) page of the active profile, wrapping around. |
| `void ActivateRelativeProfile(string deviceId, int offset)` | Activates the next (+1) or previous (-1) profile of the device, wrapping around. |
| `void CloseFolder(string deviceId)` | Leaves the open folder: back to the layout whose key opened it. |
| `void OpenFolder(string deviceId, string folderId)` | Opens a folder (a layout of the active profile) on the device. |
| `void RequestRender(string deviceId)` | Re-renders the device's controls (only changed controls are pushed). |

### IApplicationPlugin

*interface* — A set of actions. Installed plugins are loaded from a package (see the README) and may implement `IDisposable`; they are disposed when disabled or uninstalled.

| Member | |
|---|---|
| `IReadOnlyCollection<IDeckAction> Actions` |  |
| `string Author` |  |
| `string Description` |  |
| `string Id` |  |
| `string Name` |  |
| `string Version` |  |

### IConfigurableDevicePlugin

*interface* — Optional: a device plugin with per-device settings (vibration, brightness, idle screen,...). DeckHost shows an editor generated from the definitions; the plugin stores and applies the values itself (also again when the device reconnects).

| Member | |
|---|---|
| `string ApplyDeviceSettings(string deviceId, ActionSettings settings)` | Saves and applies the settings. Returns an error message, or null on success. |
| `IReadOnlyList<ActionSettingDefinition> GetDeviceSettingDefinitions(string deviceId)` |  |
| `ActionSettings GetDeviceSettings(string deviceId)` |  |

### IDeckAction

*interface* — An action that can be bound to a control. `Execute` and `Render` are called on the host's worker thread, never the UI thread, and must return quickly: start long work in the background and call `RequestRender` when done.

| Member | |
|---|---|
| `bool CanBeMultiActionStep` | Whether the action can be a step of a Multi Action. |
| `string Description` | One line shown in the action list and the Customize window. |
| `bool HasSettingsPage` | Whether the action has its own settings page (for example a Stream Deck property inspector). |
| `string Id` |  |
| `string ListIcon` | The action's icon in DeckHost's action list (and while it's dragged): SVG path data on the 24×24 grid (see `Icons`) or the absolute path of an image file. Null: a generic icon. |
| `string Name` |  |
| `TimeSpan? RefreshInterval` | Set for actions whose image changes over time (clocks, system info); the host redraws them this often. |
| `InputCapabilities RequiredInputs` |  |
| `OutputCapabilities RequiredOutputs` |  |
| `IReadOnlyList<ActionSettingDefinition> Settings` | Settings the user fills in per binding; values arrive in `Settings`. |
| `bool ShowInPalette` | False for actions the editor uses internally and that shouldn't be dragged onto controls. |
| `bool ShowsAppearanceOnly` | True for an action that does nothing but show the control's own text, image and background (DeckHost's hidden "Display Content"). A control bound to it with only a background color counts as free: a linked control (a knob under a touch zone) may then use its display. |
| `void Execute(ActionContext context)` |  |
| `Uri GetSettingsPageUri(ActionContext context)` | Address of the action's own settings page for one binding; DeckHost shows it in its built-in browser. |
| `void OnAppear(ActionContext context)` | Called (on the host worker) when the action becomes visible on a device: its profile became active. |
| `void OnDisappear(ActionContext context)` | Called when the action is no longer visible (profile switched, binding removed, plugin disabled). |
| `void Render(IRenderContext context)` | Draws the action's content on top of the control's background and appearance image. |

### IDeviceBackdropOutput

*interface* — Optional addition to `IDeviceOutput` for devices whose screen is larger than the control images: the area between and around the controls (the "backdrop"). Without it that area keeps showing whatever the device stores itself, for example its idle screen. The host calls it from the same worker thread as `IDeviceOutput`.

| Member | |
|---|---|
| `bool TrySetBackdrop(string deviceId, byte red, byte green, byte blue, string error)` | Fills the screen behind the control images with one color. It stays until the next call or `Clear`. The host sends every control image again afterwards. |

### IDeviceFirmware

*interface* — Optional, on an `IDevicePlugin`: the device's firmware. A plugin that can only read the version implements `GetFirmwareInfo` (with `CanUpdate` false); one that knows the maker's update protocol also installs firmware.

| Member | |
|---|---|
| `Task<FirmwareRelease> CheckForFirmwareAsync(string deviceId, CancellationToken cancellationToken)` | The newest firmware the maker offers for this device, when newer than what it runs; null when none (or the plugin doesn't look). |
| `DeviceFirmwareInfo GetFirmwareInfo(string deviceId)` | What the device runs now and what DeckHost can do about it; null when unknown (unplugged, not read yet). |
| `Task<FirmwareUpdateResult> InstallFirmwareAsync(string deviceId, FirmwareImage image, IProgress<FirmwareProgress> progress, CancellationToken cancellationToken)` | Writes `image` to the device. Cancelling is only honored before writing starts. Never throws for device errors: the result says what happened. |

### IDeviceLightBatchOutput

*interface* — Optional, next to `IDeviceOutput`: sets several LEDs in one go. The host uses it for animated light effects, which change every LED many times a second; a device that sets all its LEDs in one command (the N4 Pro) then sends one command instead of one per LED.

| Member | |
|---|---|
| `bool TrySetLights(string deviceId, IReadOnlyList<LightColor> lights, string error)` |  |

### IDeviceModelCatalog

*interface* — Optional for an `IDevicePlugin`: every model the plugin can drive, each described as an unplugged device (its controls, displays, capabilities). DeckHost's device simulator (`DeckHost --simulator`) offers these models and behaves like the hardware: it shows the controls, sends their input and shows the images and lights DeckHost sends. A plugin that describes a device it can't see yet (a model table, a saved layout) is a good fit.

| Member | |
|---|---|
| `IReadOnlyList<DeviceDefinition> DescribeModels()` | The models, as unplugged devices. Ids only need to be unique within the plugin. |

### IDeviceOutput

*interface* — Device output. The host calls these from a single worker thread, never the UI thread.

| Member | |
|---|---|
| `void Clear(string deviceId)` | Blanks all images and lights on the device, for example on shutdown. |
| `bool TryPushImage(string deviceId, string controlId, ReadOnlySpan<byte> rgba, int width, int height, string error)` | Pushes an RGBA8888 image sized `RenderWidth x RenderHeight` to one control. |
| `bool TrySetLight(string deviceId, string controlId, byte red, byte green, byte blue, string error)` | Sets the RGB LED tied to a control (see `PhysicalControl.LedIndex`). |

### IDevicePlugin

*interface* — Hardware support. Everything device-specific (SDK ids, native calls, image sizes, LED layout) stays behind this interface and `IDeviceOutput`.

| Member | |
|---|---|
| `IReadOnlyCollection<DeviceDefinition> Devices` | Current device definitions. Replaced (never mutated) when devices change. |
| `string Id` |  |
| `string Name` |  |
| `IDeviceOutput Output` | Output side of the plugin, used by the host to draw on and light up controls. |
| `event EventHandler DevicesChanged` | Raised on any thread when `Devices` changed (hot-plug, capabilities read,...). |
| `Task InitializeAsync(IDevicePluginContext context, CancellationToken cancellationToken)` |  |

### IDevicePluginContext

*interface* — Given to device plugins by the host. All methods are thread-safe and return quickly, so they can be called straight from a native SDK callback.

| Member | |
|---|---|
| `void Log(string level, string message)` |  |
| `void ReportConnectionStatus(string deviceId, string state, string message)` |  |
| `void ReportInputEvent(string deviceId, string controlId, InputCapabilities eventType, int value)` |  |
| `void RequestRedraw(string deviceId)` | Asks the host to send every control image again, e.g. after the plugin drew something over them (a full-screen background). Thread-safe; the redraw happens on the host's worker. |

### IDeviceSettingActions

*interface* — Optional, next to `IConfigurableDevicePlugin`: buttons on the device's settings page for things that aren't a value to save (make a new pairing code, reset the device, …).

| Member | |
|---|---|
| `IReadOnlyList<DeviceSettingAction> GetSettingActions(string deviceId)` | The buttons for this device. |
| `string RunSettingAction(string deviceId, string actionId)` | Does what the button says (DeckHost calls it off the UI thread). Returns a line for the user; DeckHost reloads the device's settings afterwards, so changed values show. |

### IRenderContext

*interface* — Drawing surface for one control. Coordinates are device pixels of that control.

| Member | |
|---|---|
| `string ControlId` |  |
| `string DeviceId` |  |
| `int Height` |  |
| `bool IsCompact` | True when the action only has a low, wide strip to draw in (a knob's status line under the action of the touch zone it shares): draw one short line instead of the full layout. |
| `string ProfileId` | The profile being drawn. |
| `ActionSettings Settings` | The settings of the action being drawn. |
| `TextStyle TextStyle` | The binding's text style (Customize → Text style), so an action can draw more than the label in it: DeckHost's actions draw their icon in its color. Null when none is set. |
| `IVariableStore Variables` |  |
| `int Width` |  |
| `void Clear(uint argbColor)` |  |
| `void DrawCenteredText(string text, double centerX, double centerY, float size, uint argbColor = 0xFFFFFFFF)` | Draws text centered on a point, like `DrawText`. The size shrinks when needed so the text fits the control's width. |
| `bool DrawEncodedImage(byte[] encodedImage)` | Draws an encoded image (PNG, JPG, GIF, WebP bytes) scaled to cover the control. |
| `bool DrawEncodedImage(byte[] encodedImage, bool clearEdgeBlack)` | Draws an encoded image (PNG, JPG,...) like `DrawEncodedImage`; with `clearEdgeBlack` the black background around it (black that touches the image's edge) becomes transparent, so the control's background shows. For images made for a Stream Deck, whose keys are black where nothing is drawn. |
| `void DrawIcon(string svgPathData, double centerX, double centerY, double size, uint argbColor = 0xFFFFFFFF)` | Draws an icon: SVG path data on a 24×24 grid (see `Icons`), filled with the even-odd rule, centered on the point and scaled to `size` pixels. An absolute file path (an icon from the library) draws that image fitted into the same square; a single-color SVG (one that uses `currentColor`) takes `argbColor`. (Default members: plugins built against earlier versions keep loading.) |
| `void DrawImage(byte[] rgbaPixels, int width, int height)` | Draws a raw RGBA8888 buffer, scaled to fill the control. |
| `bool DrawImageFile(string path)` | Draws an image file scaled to cover the control. Returns false when the file can't be decoded. |
| `void DrawLabel(string text, TextVerticalAlignment defaultVertical = TextVerticalAlignment.Middle, float? defaultSize = null, uint defaultColor = 0xFFFFFFFF)` | Draws the control's label (multi-line, wrapped to the control's width) using the user's text style from Customize: color, font, size, bold/italic and alignment. Values the user didn't set fall back to the arguments, which describe how the action would draw it by default. |
| `void DrawRightAlignedText(string text, double right, double baselineY, float size, uint argbColor = 0xFFFFFFFF)` | Draws text with its right edge at `right` (`baselineY` is the baseline). |
| `void DrawText(string text, double x, double y, float size, uint argbColor = 0xFFFFFFFF)` | Draws text at a position the action chooses, in the user's font, bold and italic (the size, color and place are the action's; use `DrawLabel` for the control's label). |
| `void FillGradientRect(double x, double y, double width, double height, double radius, uint fromArgb, uint toArgb)` | A rounded rectangle with a diagonal gradient, top left to bottom right. |
| `void FillRoundedRect(double x, double y, double width, double height, double radius, uint argbColor)` |  |

### IVariableStore

*interface*

| Member | |
|---|---|
| `IObservable<T> Observe(string key)` |  |
| `void Publish(string key, T value)` |  |
| `bool TryGetValue(string key, T value)` |  |

### NullActionHost

*class*

| Member | |
|---|---|
| `NullActionHost Instance` |  |
| `void ActivateProfile(string deviceId, string profileIdOrName)` |  |
| `void ActivateRelativeProfile(string deviceId, int offset)` |  |
| `void RequestRender(string deviceId)` |  |

## DeckHost.Contracts.Models

### ActionContext

*record*

| Member | |
|---|---|
| `ActionContext(string DeviceId, string ControlId, IVariableStore Variables, InputCapabilities EventType = InputCapabilities.None, int Value = 0)` | Creates it with the positional properties. |
| `string ControlId { get; init; }` |  |
| `string DeviceId { get; init; }` |  |
| `InputCapabilities EventType { get; init; }` | The input that triggered the action. |
| `IActionHost Host { get; init; }` |  |
| `string ProfileId { get; init; }` | The profile the binding belongs to (empty when unknown). |
| `ActionSettings Settings { get; init; }` | The user's settings for this binding (or Multi Action step). |
| `int Value { get; init; }` | Event value: 1 press, 0 release, -1/+1 per rotation or swipe step. |
| `IVariableStore Variables { get; init; }` |  |

### ActionReference

*record* — A bound action plus the settings the user gave it.

| Member | |
|---|---|
| `ActionReference(string PluginId, string PluginName, string ActionId, string ActionName, InputCapabilities RequiredInputs, OutputCapabilities RequiredOutputs, ActionSettings Settings = null)` | A bound action plus the settings the user gave it. |
| `string ActionId { get; init; }` |  |
| `string ActionName { get; init; }` |  |
| `ActionSettings EffectiveSettings` |  |
| `string PluginId { get; init; }` |  |
| `string PluginName { get; init; }` |  |
| `InputCapabilities RequiredInputs { get; init; }` |  |
| `OutputCapabilities RequiredOutputs { get; init; }` |  |
| `ActionSettings Settings { get; init; }` |  |

### ActionSettingDefinition

*record* — Describes one setting an action needs (an app to launch, a scene name,...). The editor in the Customize window is generated from these definitions.

| Member | |
|---|---|
| `ActionSettingDefinition(string Key, string Label, ActionSettingKind Kind = ActionSettingKind.Text, string DefaultValue = "", string Description = null, IReadOnlyList<ActionSettingOption> Options = null, string Placeholder = null)` | Describes one setting an action needs (an app to launch, a scene name,...). The editor in the Customize window is generated from these definitions. |
| `string DefaultValue { get; init; }` |  |
| `string Description { get; init; }` |  |
| `IReadOnlyDictionary<string, string> InputDefaults { get; init; }` | For a `PerInput` setting: defaults that differ per input, by `ControlInputs` id (e.g. turning left: previous, turning right: next). An input without one uses the binding's plain value, then `DefaultValue`. |
| `string Key { get; init; }` |  |
| `ActionSettingKind Kind { get; init; }` |  |
| `string Label { get; init; }` |  |
| `IReadOnlyList<ActionSettingOption> Options { get; init; }` |  |
| `bool PerInput { get; init; }` | The setting can have its own value for each input of the control: on a knob, one for pressing, one for turning left and one for turning right (see `ControlInputs`). The editor then shows the setting once per input, and the host hands the action the value for the input that happened, under the plain `Key`, so the action reads it as usual. Stored as `key@input` (`SettingKey`). |
| `string Placeholder { get; init; }` |  |
| `NumberRange Range { get; init; }` | For a `Number` setting: its range. With a range the editor shows a slider (and, unless `ShowInput` is off, a number box for exact values); without one only a number box. A property rather than a constructor parameter, so plugins built against the earlier constructor keep working. |
| `SettingCondition ShowWhen { get; init; }` | Shows the setting only while another setting has one of the given values (e.g. a profile name only for "a specific profile"). For a `PerInput` setting, the other setting of the same input. |

### ActionSettingKind

*enum*

| Member | |
|---|---|
| `Text` |  |
| `Secret` | Hidden while typing; still stored in plain text in the profiles file. |
| `Number` |  |
| `Toggle` |  |
| `Choice` |  |
| `FilePath` |  |
| `Hotkey` | A key combination such as "Ctrl+Shift+M"; the editor can record it from a key press. |

### ActionSettingOption

*record*

| Member | |
|---|---|
| `ActionSettingOption(string Value, string Label)` | Creates it with the positional properties. |
| `string Label { get; init; }` |  |
| `InputCapabilities Requires { get; init; }` | The input a control needs for this option to make sense (e.g. Rotate for "turned left"). The editor hides the option on controls without that input. None shows it everywhere. |
| `string Value { get; init; }` |  |

### ActionSettings

*class* — The values a user entered for an action's settings (see `Settings`). Immutable, compares by value and serializes as a plain JSON object.

| Member | |
|---|---|
| `ActionSettings(IEnumerable<KeyValuePair<string, string>> values)` |  |
| `int Count` |  |
| `ActionSettings Empty` |  |
| `string Item` |  |
| `IEnumerable<string> Keys` |  |
| `IEnumerable<string> Values` |  |
| `bool ContainsKey(string key)` |  |
| `ActionSettings ForInput(IEnumerable<ActionSettingDefinition> definitions, string input)` | The settings as the action sees them for one input (`ControlInputs`): each `PerInput` setting gets its value for that input under its plain key. Order: the value stored for the input, the definition's default for the input, then the plain value as stored (and the action's own default). |
| `string Get(string key, string fallback = "")` | The value, or `fallback` when it is missing or blank. |
| `bool GetBool(string key, bool fallback = false)` |  |
| `double GetDouble(string key, double fallback)` | A number with decimals (stored with a '.'), for Number settings with a fractional step. |
| `IEnumerator<KeyValuePair<string, string>> GetEnumerator()` |  |
| `int GetInt(string key, int fallback)` |  |
| `bool TryGetValue(string key, string value)` |  |
| `ActionSettings With(string key, string value)` |  |

### ControlAppearance

*record* — Visual appearance of a control, drawn under the action's own content.

| Member | |
|---|---|
| `ControlAppearance(string Text = null, string ImagePath = null, RgbFeedback RgbFeedback = null, string BackgroundColor = null, TextStyle TextStyle = null)` | Visual appearance of a control, drawn under the action's own content. |
| `string BackgroundColor { get; init; }` | Solid fill (#RRGGBB) drawn over the profile background. |
| `string IconPath { get; init; }` | An icon file (SVG, PNG,...) from the icon library that takes the place of the action's own icon on its tile. Unlike `ImagePath` it keeps the tile and the caption; a single-color SVG is drawn in white. An init property, so the constructor stays binary-compatible. |
| `string ImagePath { get; init; }` | PNG, JPG, WebP or (animated) GIF, scaled to cover the control. |
| `RgbFeedback RgbFeedback { get; init; }` | LED feedback for controls with a light. |
| `bool ShowOnLinkedDisplay { get; init; }` | For a control that borrows another control's display (`DisplayControlId`, a knob and the touch zone above it): whether its action is shown there. It fills the display when that control has no action of its own, and gets a strip along the edge when it has. A property rather than a constructor parameter, so plugins built against the earlier constructor keep working. |
| `bool ShowTile { get; init; }` | Draw the action's colored tile behind its icon. Off by default: controls are transparent, so the profile background shows, unless the user picks the tile or a `BackgroundColor`. |
| `string Text { get; init; }` | Label text; replaces the action's own title. |
| `TextStyle TextStyle { get; init; }` | Color, font and alignment of the label (see `DrawLabel`). |
| `TileStyle Tile { get; init; }` | How the tile looks when `ShowTile` is on; null for the action's own colors and corners. |

### ControlBinding

*record*

| Member | |
|---|---|
| `ControlBinding(string ControlId, ProfileActionKind ActionKind, ActionReference Action, ControlAppearance Appearance, IReadOnlyList<MultiActionStep> Steps)` | Creates it with the positional properties. |
| `ActionReference Action { get; init; }` |  |
| `ProfileActionKind ActionKind { get; init; }` |  |
| `ControlAppearance Appearance { get; init; }` |  |
| `string ControlId { get; init; }` |  |
| `IEnumerable<ActionReference> StepActions` | Every action the binding runs besides its own: its steps, or its workflow's action nodes (in node order). |
| `IReadOnlyList<MultiActionStep> Steps { get; init; }` |  |
| `Workflow Workflow { get; init; }` | For `Workflow`: the nodes and links. An init property, so the record's constructor stays binary-compatible. |

### ControlInputs

*static class* — The inputs a control can produce, by direction: the ids used by the "Run when" trigger and by `PerInput` settings.

| Member | |
|---|---|
| `const string LongPress` |  |
| `const string Press` |  |
| `const string Release` |  |
| `const string RotateLeft` |  |
| `const string RotateRight` |  |
| `const string SwipeLeft` |  |
| `const string SwipeRight` |  |
| `static string Describe(string input)` | How the editor names an input ("When pressed", "When turned left",...). |
| `static string FromEvent(InputCapabilities eventType, int value)` | The input an event is: its type, and for turning and swiping its direction. |
| `static IReadOnlyList<string> Of(InputCapabilities inputs)` | The inputs a control with these capabilities can be set up for, in the order the editor shows them. A release isn't one: it only ends a press. |
| `static string SettingKey(string key, string input)` | Where a `PerInput` setting's value for one input is stored. |

### ControlShape

*enum* — The outline of a control, for drawing it like the hardware (master plan 7.1.2).

| Member | |
|---|---|
| `Rectangle` |  |
| `Round` |  |

### ControlType

*enum*

| Member | |
|---|---|
| `Key` |  |
| `Encoder` |  |
| `TouchSurface` |  |
| `Display` |  |
| `Slider` |  |
| `Button` | A physical button without a display of its own. |
| `Joystick` | A joystick or directional pad. |
| `Led` | A light only (an indicator or a lighting zone), no input. |

### DeviceCapabilities

*enum* — What a device can do (master plan 7.1.4). Query before using a feature.

| Member | |
|---|---|
| `None` |  |
| `Images` | Shows pictures on its controls. |
| `AnimatedImages` | Plays animated pictures by itself (else the host sends frames). |
| `Touch` | Reports touches on a surface. |
| `MultiTouch` | Reports several touches at once. |
| `Brightness` | Its screen brightness can be set. |
| `RgbLights` | Has RGB lights the host can set. |
| `Haptics` | Can vibrate. |
| `Backdrop` | Shows a background behind all controls (`IDeviceBackdropOutput`). |
| `Firmware` | Reports its firmware (`IDeviceFirmware`). |
| `Settings` | Has its own settings page (`IConfigurableDevicePlugin`). |

### DeviceDefinition

*record* — A device a plugin can drive: its descriptor (master plan 7.1). Plugins also expose definitions for devices that are not plugged in, so profiles can be edited offline; `IsConnected` tells them apart. The control topology is `Controls`; identity, displays, capabilities and limits are optional properties, so plugins built before them keep working. When the topology changes, the plugin raises `DevicesChanged` with the new definition; bindings are kept by control id, so a control that disappears doesn't move others.

| Member | |
|---|---|
| `DeviceDefinition(string Id, string Name, IReadOnlyList<PhysicalControl> Controls, bool IsConnected = false, string Detail = null)` | A device a plugin can drive: its descriptor (master plan 7.1). Plugins also expose definitions for devices that are not plugged in, so profiles can be edited offline; `IsConnected` tells them apart. The control topology is `Controls`; identity, displays, capabilities and limits are optional properties, so plugins built before them keep working. When the topology changes, the plugin raises `DevicesChanged` with the new definition; bindings are kept by control id, so a control that disappears doesn't move others. |
| `DeviceCapabilities Capabilities { get; init; }` | What the device can do. |
| `IReadOnlyList<PhysicalControl> Controls { get; init; }` |  |
| `int DescriptorVersion { get; init; }` | The descriptor format version; plugins built for an older one say so. |
| `string Detail { get; init; }` |  |
| `IReadOnlyList<DeviceDisplay> Displays { get; init; }` | The displays or display regions; empty when the device has none or doesn't say. |
| `string Id { get; init; }` |  |
| `DeviceIdentity Identity { get; init; }` | Manufacturer, model, serial, revision, firmware and connection; null when unknown. |
| `bool IsConnected { get; init; }` |  |
| `DeviceLimits Limits { get; init; }` | What it can't do, or not fully; null when nothing is known. |
| `string Name { get; init; }` |  |
| `PhysicalSize PhysicalSize { get; init; }` | The device's outer size in millimetres; null when unknown. |
| `const int CurrentDescriptorVersion` | The descriptor format version this definition follows. |
| `PhysicalControl FindControl(string controlId)` |  |
| `IReadOnlyList<string> FindDescriptorProblems()` | What's inconsistent in this descriptor, in words (empty when it's fine): duplicate control ids, controls with images but no display (when the device lists displays), displays that list unknown controls or none, links to unknown controls. Device plugin authors can test with it. |
| `DeviceDisplay FindDisplay(string displayId)` |  |
| `bool Has(DeviceCapabilities capability)` | Whether the device declares `capability` (all of the given flags). |

### DeviceDisplay

*record* — A display or a region of one (master plan 7.1.3): its pixel size and the controls whose images are shown on it. Controls point back with `DisplayId`.

| Member | |
|---|---|
| `DeviceDisplay(string Id, int Width, int Height)` | A display or a region of one (master plan 7.1.3): its pixel size and the controls whose images are shown on it. Controls point back with `DisplayId`. |
| `IReadOnlyList<string> ControlIds { get; init; }` | The controls on this display, in reading order. |
| `int Height { get; init; }` | Height in pixels. |
| `string Id { get; init; }` | Stable id within the device: "main", "strip", "keys". |
| `int? MaxFramesPerSecond { get; init; }` | The most frames a second the display takes; null when it doesn't say. |
| `int RotationDegrees { get; init; }` | How the device turns what it gets (0, 90, 180, 270); device plugins apply it. |
| `int Width { get; init; }` | Width in pixels, as the device draws it (before `RotationDegrees`). |

### DeviceFirmwareInfo

*record* — A device's firmware as its plugin knows it.

| Member | |
|---|---|
| `DeviceFirmwareInfo(string Version, bool CanUpdate, string Detail = null)` | A device's firmware as its plugin knows it. |
| `bool CanUpdate { get; init; }` | Whether the plugin can install firmware on this device. |
| `string Detail { get; init; }` | A line for the user: where updates come from, or why there are none here. |
| `IReadOnlyList<string> FileExtensions { get; init; }` | File types the plugin installs from a file the user picks (".bin", ".dfu"); empty for none. |
| `string Version { get; init; }` | The version the device reports. |

### DeviceIdentity

*record* — Who made a device and which one it is (master plan 7.1.1); every field is optional.

| Member | |
|---|---|
| `DeviceIdentity(string Manufacturer = null, string Model = null, string SerialNumber = null, string HardwareRevision = null, string FirmwareVersion = null, string Connection = null)` | Who made a device and which one it is (master plan 7.1.1); every field is optional. |
| `string Connection { get; init; }` | How it's connected: "USB", "Network", "Virtual", … |
| `string FirmwareVersion { get; init; }` | The firmware version, when the device reports one. |
| `string HardwareRevision { get; init; }` | A hardware revision, when the device reports one. |
| `string Manufacturer { get; init; }` | "MiraBox", "Elgato", … |
| `string Model { get; init; }` | "StreamDock N4 Pro", "Stream Deck +", … |
| `string SerialNumber { get; init; }` | The device's serial number, when it reports one. |

### DeviceLimits

*record* — What a device can't do, or not fully (master plan 7.1.1 "limitations"); null fields: no limit known.

| Member | |
|---|---|
| `DeviceLimits()` | Creates it with the positional properties. |
| `int? ColorDepthBits { get; init; }` | Bits per pixel of its screens (16 for RGB565), when fewer than 24. |
| `int? MaxFramesPerSecond { get; init; }` | The most image updates a second the device keeps up with, for all its controls. |
| `IReadOnlyList<string> Notes { get; init; }` | Anything else worth knowing, in words: "no knob release events", … |
| `bool? Transparency { get; init; }` | False when transparent pixels can't be shown (the host flattens them). |

### DeviceProfile

*record*

| Member | |
|---|---|
| `DeviceProfile(string Id, string Name, string DeviceId, IReadOnlyList<ControlBinding> Bindings, IReadOnlyList<string> AutoSwitchApps = null, ProfileBackground Background = null)` | Creates it with the positional properties. |
| `IReadOnlyList<string> AutoSwitchApps { get; init; }` | Process names (for example "obs64" or "chrome.exe") that activate this profile automatically while their window has focus. Empty or null: never switched to automatically. |
| `ProfileBackground Background { get; init; }` | Optional background spanning all controls. |
| `IReadOnlyList<ControlBinding> Bindings { get; init; }` |  |
| `string DeviceId { get; init; }` |  |
| `string Id { get; init; }` |  |
| `bool IsRoot` |  |
| `string Kind { get; init; }` | What this layout is: null for a profile, `Page` or `Folder`. |
| `string Name { get; init; }` |  |
| `string ParentId { get; init; }` | For a page or folder: the layout it belongs to. A page belongs to its profile; a folder to the page (or folder) whose key opens it. Null for a profile (its own first page). |

### DeviceSide

*enum* — Which face of the device a control is on (master plan 7.1.3). Most are on the front; a wheel on the side of the device is drawn on that edge, not as part of the front.

| Member | |
|---|---|
| `Front` |  |
| `Left` |  |
| `Right` |  |
| `Top` |  |
| `Bottom` |  |
| `Back` |  |

### FirmwareImage

*record* — A firmware file on this computer, downloaded and checked by DeckHost or picked by the user.

| Member | |
|---|---|
| `FirmwareImage(string Path, string Version = null)` | A firmware file on this computer, downloaded and checked by DeckHost or picked by the user. |
| `string Path { get; init; }` |  |
| `string Version { get; init; }` | The version, when known (from a release; null for a picked file). |

### FirmwareProgress

*record* — How far an update is: 0–1 and the current step ("Erasing", "Writing", "Restarting").

| Member | |
|---|---|
| `FirmwareProgress(double Fraction, string Step)` | How far an update is: 0–1 and the current step ("Erasing", "Writing", "Restarting"). |
| `double Fraction { get; init; }` |  |
| `string Step { get; init; }` |  |

### FirmwareRelease

*record* — Firmware offered for download by the device's maker.

| Member | |
|---|---|
| `FirmwareRelease(string Version, Uri DownloadUri, string Sha256, string Notes = null)` | Firmware offered for download by the device's maker. |
| `Uri DownloadUri { get; init; }` |  |
| `string Notes { get; init; }` |  |
| `string Sha256 { get; init; }` | The file's SHA-256 (hex). DeckHost refuses a download without one or that doesn't match. |
| `string Version { get; init; }` |  |

### FirmwareUpdateResult

*record* — How an update ended.

| Member | |
|---|---|
| `FirmwareUpdateResult(bool Success, string Message)` | How an update ended. |
| `string Message { get; init; }` |  |
| `FirmwareUpdateResult NotSupported` |  |
| `bool Success { get; init; }` |  |

### LightColor

*record* — One LED's color, for `IDeviceLightBatchOutput`.

| Member | |
|---|---|
| `LightColor(string ControlId, byte Red, byte Green, byte Blue)` | One LED's color, for `IDeviceLightBatchOutput`. |
| `byte Blue { get; init; }` |  |
| `string ControlId { get; init; }` |  |
| `byte Green { get; init; }` |  |
| `byte Red { get; init; }` |  |

### LightEffects

*static class* — The effects a control's LED can show while idle.

| Member | |
|---|---|
| `IReadOnlyList<string> All` | All effects, in the order the editors list them. |
| `const string Blink` | On and off (with soft edges). |
| `const string Breathe` | The color fading in and out. |
| `const string Candle` | A flickering flame. |
| `const string Chase` | A light with a fading tail running from LED to LED. |
| `const string ColorCycle` | Fading smoothly from one color to the next (`Colors`). |
| `const string Custom` | The user's own sequence of colors (`Steps`). |
| `const string Off` | Dark. |
| `const string Pulse` | Two quick beats, then a rest, like a heartbeat. |
| `const string Rainbow` | Cycling through all colors, each LED a little ahead of the previous one. |
| `const string Solid` | One steady color. |
| `const string Sparkle` | Random twinkles that flash and fade out. |
| `const string Wave` | A wave of brightness running across the LEDs. |
| `static bool IsAnimated(string effect)` |  |
| `static bool UsesColor(string effect)` | Effects drawn in the style's own color (the others pick their colors themselves or use a list). |
| `static bool UsesColors(string effect)` | Effects that use a list of colors (`Colors`). |
| `static bool UsesSpread(string effect)` | Effects where the LEDs differ from each other, so the spread between them matters. |

### LightStep

*record* — One step of a custom light effect (`Custom`): fade to `Color` in `FadeSeconds`, then keep it for `HoldSeconds`.

| Member | |
|---|---|
| `LightStep(string Color, double FadeSeconds = 0.5, double HoldSeconds = 0.5)` | One step of a custom light effect (`Custom`): fade to `Color` in `FadeSeconds`, then keep it for `HoldSeconds`. |
| `string Color { get; init; }` |  |
| `double FadeSeconds { get; init; }` |  |
| `double HoldSeconds { get; init; }` |  |
| `const double MaxSeconds` |  |

### LightStyle

*record* — How an LED looks while idle: a color, an effect (`LightEffects`) and how long one cycle of the effect takes. Used as the default for all of a device's lights; a control can override it (`IdleEffect`, `IdleLight`).

| Member | |
|---|---|
| `LightStyle(string Color = "0000FF", string Effect = "solid", double PeriodSeconds = 3)` | How an LED looks while idle: a color, an effect (`LightEffects`) and how long one cycle of the effect takes. Used as the default for all of a device's lights; a control can override it (`IdleEffect`, `IdleLight`). |
| `string Color { get; init; }` |  |
| `IReadOnlyList<string> Colors { get; init; }` | The colors of `ColorCycle` (#RRGGBB); empty: the style's color and its opposite. |
| `string Effect { get; init; }` |  |
| `IReadOnlyList<LightStyle> Leds { get; init; }` | Each LED its own light, in the order of the device's LEDs (a null entry, or a missing one, uses this style). For the device's default light (Device Settings → Knob lights). |
| `double PeriodSeconds { get; init; }` |  |
| `double? Spread { get; init; }` | How far each LED runs ahead of the previous one, as a part of the cycle (0: all the same, 0.25: a quarter). Null for the effect's own default. |
| `IReadOnlyList<LightStep> Steps { get; init; }` | The steps of `Custom`, played in order and then from the start. |
| `const double DefaultPeriodSeconds` |  |
| `const double MaxPeriodSeconds` |  |
| `const double MinPeriodSeconds` |  |
| `LightStyle ForLed(int index)` | The light of LED `index`: its own when it has one, else this style. |

### MultiActionStep

*record*

| Member | |
|---|---|
| `MultiActionStep(int Order, ActionReference Action)` | Creates it with the positional properties. |
| `ActionReference Action { get; init; }` |  |
| `int DelayMs { get; init; }` | How long to wait before this step runs, in milliseconds (0: right after the previous one). |
| `string Group { get; init; }` | Which set the step belongs to: for a Multi Action Switch `First` or `Second`, for Key Logic `Press`, `DoublePress` or `Hold`. Null for a Multi Action. |
| `int Order { get; init; }` |  |

### NumberRange

*record* — The values a `Number` setting can take: from `Minimum` to `Maximum` in steps of `Step`. Values are stored as invariant-culture text, whole numbers when the step is whole.

| Member | |
|---|---|
| `NumberRange(double Minimum, double Maximum, double Step = 1)` | The values a `Number` setting can take: from `Minimum` to `Maximum` in steps of `Step`. Values are stored as invariant-culture text, whole numbers when the step is whole. |
| `bool IsWholeNumber` |  |
| `double Maximum { get; init; }` |  |
| `double Minimum { get; init; }` |  |
| `bool ShowInput { get; init; }` | Whether a number box is shown next to the slider, to type an exact value. |
| `double Step { get; init; }` |  |
| `string Unit { get; init; }` | Shown after the value, for example "%" or "ms". |
| `double? UnsetValue { get; init; }` | For a setting that is empty by default (the action or device then keeps its own default): that default value. The slider rests there until it is moved, and the editor shows it. |
| `bool Contains(double value)` |  |
| `string Describe(double value)` | The value with its unit, for display ("75 %"). |
| `string Format(double value)` | The value as it is stored: invariant culture, no decimals for whole steps. |
| `double Snap(double value)` | Clamps to the range and rounds to the nearest step, counted from `Minimum`. |

### PhysicalControl

*record* — One physical control on a device.

| Member | |
|---|---|
| `PhysicalControl(string Id, string DisplayName, ControlType Type, Rect Bounds, InputCapabilities Inputs, OutputCapabilities Outputs, int RenderWidth = 0, int RenderHeight = 0, string HardwareId = null, int? LedIndex = null, string DisplayControlId = null)` | One physical control on a device. |
| `Rect Bounds { get; init; }` | Position in the designer's layout coordinates (not device pixels). |
| `string DisplayControlId { get; init; }` | For a control without a display of its own: the id of the control whose display belongs to it (the touch zone above a knob). The host shows this control's action there; see `ShowOnLinkedDisplay`. |
| `string DisplayId { get; init; }` | The display (or region) its image is on (`Displays`); null when it has none or the device doesn't say. |
| `string DisplayName { get; init; }` |  |
| `string HardwareId { get; init; }` | The id the hardware/SDK uses for this control, for diagnostics. |
| `bool HasImageOutput` |  |
| `bool HasLight` |  |
| `bool HasLinkedDisplay` |  |
| `string Id { get; init; }` | Stable logical id, for example `key.0`, `encoder.2` or `touch.1`. |
| `InputCapabilities Inputs { get; init; }` |  |
| `int? LedIndex { get; init; }` | Index of the RGB LED tied to this control, when there is one. |
| `OutputCapabilities Outputs { get; init; }` |  |
| `Rect? PhysicalBounds { get; init; }` | Where the control is on the device, in millimetres from its top left (physical coordinates); null when unknown. |
| `int RenderHeight { get; init; }` | Pixel height of the control's own image on the device; 0 when it has none. |
| `int RenderWidth { get; init; }` | Pixel width of the control's own image on the device; 0 when it has none. |
| `ControlShape Shape { get; init; }` | Its outline, for drawing it like the hardware. |
| `DeviceSide Side { get; init; }` | The face it is on; `Front` unless the device says otherwise. Its `Bounds` still place it in the layout, next to the front controls on that side. |
| `ControlType Type { get; init; }` |  |

### PhysicalSize

*record* — A size in millimetres (physical dimensions, master plan 7.1.3).

| Member | |
|---|---|
| `PhysicalSize(double WidthMillimetres, double HeightMillimetres)` | A size in millimetres (physical dimensions, master plan 7.1.3). |
| `double HeightMillimetres { get; init; }` |  |
| `double WidthMillimetres { get; init; }` |  |

### ProfileActionKind

*enum*

| Member | |
|---|---|
| `Single` |  |
| `MultiAction` |  |
| `MultiActionSwitch` | Two sets of steps; each press runs the next set (Elgato's Multi Action Switch). |
| `KeyLogic` | Steps for a press, a double press and holding the key down (Elgato's Key Logic). |
| `Workflow` | A visual workflow of connected nodes (`Workflow`). |

### ProfileBackground

*record*

| Member | |
|---|---|
| `ProfileBackground(string Color = null, string ImagePath = null)` | Creates it with the positional properties. |
| `string Color { get; init; }` |  |
| `string ImagePath { get; init; }` |  |
| `bool IsEmpty` |  |

### ProfileKinds

*static class* — The kinds of layouts besides a profile.

| Member | |
|---|---|
| `const string Folder` | A layout a key opens, with a Back key. |
| `const string Page` | Another page of a profile (Next/Previous Page, swiping the touch strip). |

### Rect

*struct*

| Member | |
|---|---|
| `Rect(double X, double Y, double Width, double Height)` |  |
| `double Height { get; init; }` |  |
| `double Width { get; init; }` |  |
| `double X { get; init; }` |  |
| `double Y { get; init; }` |  |

### RgbFeedback

*record* — RGB feedback configuration for a control's action response.

| Member | |
|---|---|
| `RgbFeedback(string ActiveColor = "00FF00", string IdleColor = "0000FF", int ActiveDuration = 200, bool PulseOnActive = false)` | RGB feedback configuration for a control's action response. |
| `string ActiveColor { get; init; }` |  |
| `int ActiveDuration { get; init; }` |  |
| `double? EffectPeriodSeconds { get; init; }` | How long one cycle of `IdleEffect` takes; null for the default. |
| `string IdleColor { get; init; }` |  |
| `string IdleEffect { get; init; }` | The idle effect (`LightEffects`) of this control's light; null for a steady `IdleColor`. Overrides the device's default lights (`LightStyle`). |
| `LightStyle IdleLight { get; init; }` | The whole idle light (effect, colors, custom steps, spread). Set by DeckHost's light editor; without it the light is `IdleColor` with `IdleEffect`. |
| `LightStyle IdleStyle` | The idle light as a `LightStyle`. |
| `bool PulseOnActive { get; init; }` |  |
| `ValueTuple<byte, byte, byte> GetActiveRgb()` | Get the active color as (R, G, B) tuple. |
| `ValueTuple<byte, byte, byte> GetIdleRgb()` | Get the idle color as (R, G, B) tuple. |
| `static ValueTuple<byte, byte, byte> ParseColor(string hexColor)` | Parse RGB hex string (e.g., "FF0000") to (R, G, B) tuple. |

### SettingCondition

*record* — A condition on another setting's value: `Key` is one of `Values`.

| Member | |
|---|---|
| `SettingCondition(string Key, string[] Values)` | A condition on another setting's value: `Key` is one of `Values`. |
| `string Key { get; init; }` |  |
| `string[] Values { get; init; }` |  |
| `bool IsMetBy(string value)` |  |

### StepGroups

*static class* — The step sets of a Multi Action Switch and of Key Logic.

| Member | |
|---|---|
| `const string DoublePress` |  |
| `const string First` |  |
| `const string Hold` |  |
| `const int MaxDelayMs` | The longest wait a step can have. |
| `const string Press` |  |
| `const string Second` |  |
| `static IReadOnlyList<string> For(ProfileActionKind kind)` | The sets of a binding kind, in order (empty for a Multi Action). |
| `static string Of(MultiActionStep step, ProfileActionKind kind)` | A step's set, or the kind's first set when it has none (a converted Multi Action). |

### TextHorizontalAlignment

*enum*

| Member | |
|---|---|
| `Left` |  |
| `Center` |  |
| `Right` |  |

### TextStyle

*record* — How a control's label is drawn. Every field is optional: null means "use what the action would normally do". Applied by `DrawLabel`.

| Member | |
|---|---|
| `TextStyle(string Color = null, string FontFamily = null, float? Size = null, bool Bold = false, bool Italic = false, TextHorizontalAlignment? Horizontal = null, TextVerticalAlignment? Vertical = null, bool Wrap = true)` | How a control's label is drawn. Every field is optional: null means "use what the action would normally do". Applied by `DrawLabel`. |
| `bool Bold { get; init; }` |  |
| `string Color { get; init; }` | #RRGGBB or #AARRGGBB. |
| `string FontFamily { get; init; }` | Installed font family name, e.g. "Segoe UI". |
| `TextHorizontalAlignment? Horizontal { get; init; }` |  |
| `bool Italic { get; init; }` |  |
| `float? Size { get; init; }` | Font size in device pixels. |
| `TextVerticalAlignment? Vertical { get; init; }` |  |
| `bool Wrap { get; init; }` | Wrap long lines at word boundaries (line breaks in the text always work). |

### TextVerticalAlignment

*enum*

| Member | |
|---|---|
| `Top` |  |
| `Middle` |  |
| `Bottom` |  |

### TileStyle

*record* — A background for a whole profile. The image spans the device layout, so every control shows its own slice of it (one picture across all keys). GIFs animate.

| Member | |
|---|---|
| `TileStyle(string From = null, string To = null, double? CornerPercent = null)` | A background for a whole profile. The image spans the device layout, so every control shows its own slice of it (one picture across all keys). GIFs animate. |
| `double? CornerPercent { get; init; }` |  |
| `string From { get; init; }` |  |
| `string To { get; init; }` |  |
| `const double DefaultCornerPercent` | The corner rounding of the action's own tile. |

### Workflow

*record* — A visual workflow on a control (`Workflow`): nodes connected by links. A run starts at a trigger node whose input happened and follows the links from each node's output ports; several links from one port run side by side.

| Member | |
|---|---|
| `Workflow()` | Creates it with the positional properties. |
| `IReadOnlyList<WorkflowLink> Links { get; init; }` |  |
| `IReadOnlyList<WorkflowNode> Nodes { get; init; }` |  |
| `string WhileRunning { get; init; }` | What a trigger does while its previous run is still going (`WorkflowConcurrency`). |
| `WorkflowNode Find(string nodeId)` |  |
| `IEnumerable<WorkflowLink> LinksFrom(string nodeId, string port)` | The links leaving a node's output port, in the order they were made. |
| `bool WouldCycle(string from, string to)` | Whether a link from `from` to `to` would close a loop. |

### WorkflowConcurrency

*static class* — What a trigger does while its previous run is still going (waits).

| Member | |
|---|---|
| `const string Ignore` | Ignore the input until the run is finished. |
| `const string Parallel` | Start another run next to it. |
| `const string Restart` | Stop the run and start again. |
| `const string Stop` | Stop the run (the input works as a stop button). |

### WorkflowKeys

*static class* — Setting keys of workflow nodes (`Settings`) and their values.

| Member | |
|---|---|
| `const string Add` |  |
| `const string App` | If (app): part of the focused app's name. |
| `const string Check` | If: what to check (`CheckVariable`, `CheckApp`, `CheckTime`). |
| `const string CheckApp` |  |
| `const string CheckTime` |  |
| `const string CheckVariable` |  |
| `const string Clear` |  |
| `const string Contains` |  |
| `const string Count` | Repeat: how many times (`ModeCount`). |
| `const string Flip` |  |
| `const string From` | If (time): from and to, "HH:mm" (may wrap past midnight). |
| `const string Interval` | Repeat: how long to wait between rounds, in milliseconds (0: none). |
| `const string IsEmpty` |  |
| `const string IsEqual` |  |
| `const string IsGreater` |  |
| `const string IsLess` |  |
| `const string IsNotEqual` |  |
| `const string IsSet` |  |
| `const int MaxConditionRounds` | The most rounds a while / until Repeat runs (a condition that never changes can't run forever). |
| `const int MaxRepeat` | The most times a Repeat node runs. |
| `const int MaxWaitMs` | The longest wait a node can have (ten minutes). |
| `const string Milliseconds` | Wait: how long, in milliseconds. |
| `const string Mode` | Repeat: how the rounds are decided (`ModeCount`, `ModeVariable`, `ModeWhile`, `ModeUntil`). |
| `const string ModeCount` | A fixed number of rounds (`Count`); also what a Repeat node without a mode does. |
| `const string ModeUntil` | Rounds until a variable condition holds, checked before each round. |
| `const string ModeVariable` | As many rounds as a variable's value (`Times`). |
| `const string ModeWhile` | Rounds as long as a variable condition holds (`Name`, `Operation`, `Value`), checked before each round. |
| `const string Name` | If / Set variable: the variable's name. |
| `const string On` | Trigger: which input (`WorkflowTriggers`). |
| `const string Operation` | If: the comparison; Set variable: the operation. |
| `const string Set` |  |
| `const string Subtract` |  |
| `const string Times` | Repeat (`ModeVariable`): the variable that holds the number of rounds. |
| `const string To` |  |
| `const string Value` |  |

### WorkflowLink

*record* — A connection from output `Port` of node `From` to node `To`.

| Member | |
|---|---|
| `WorkflowLink(string From, string Port, string To)` | A connection from output `Port` of node `From` to node `To`. |
| `string From { get; init; }` |  |
| `string Port { get; init; }` |  |
| `string To { get; init; }` |  |

### WorkflowNode

*record* — One node of a `Workflow`. `Kind` is one of `WorkflowNodeKinds`; `X`/`Y` place it on the editor's canvas.

| Member | |
|---|---|
| `WorkflowNode(string Id, string Kind, double X = 0, double Y = 0)` | One node of a `Workflow`. `Kind` is one of `WorkflowNodeKinds`; `X`/`Y` place it on the editor's canvas. |
| `ActionReference Action { get; init; }` | For an `Action` node: the action it runs, with its settings. |
| `ActionSettings EffectiveSettings` |  |
| `string Id { get; init; }` |  |
| `string Kind { get; init; }` |  |
| `ActionSettings Settings { get; init; }` | The node's own parameters (a wait's length, a condition), by `WorkflowKeys`. |
| `string Title { get; init; }` | A name the user gave the node (shown instead of the generated one). |
| `double X { get; init; }` |  |
| `double Y { get; init; }` |  |

### WorkflowNodeKinds

*static class* — The kinds of workflow nodes.

| Member | |
|---|---|
| `IReadOnlyList<string> All` |  |
| `const string Action` | Runs an action (`Action`). |
| `const string If` | Continues at Yes or No depending on a condition. |
| `const string Repeat` | Runs its Each output a number of times, one after the other, then continues at Done. |
| `const string SetVariable` | Sets, adds to or flips a workflow variable. |
| `const string Stop` | Ends the run (waits still to come don't continue). |
| `const string Toggle` | Continues at First and Second in turn: the first run First, the next Second, and so on. |
| `const string Trigger` | Where a run starts: an input of the control (`On`, `WorkflowTriggers`). |
| `const string Wait` | Waits (`Milliseconds`), then continues. |
| `static bool HasInput(string kind)` | Whether links can lead into the kind (a trigger only starts runs). |
| `static IReadOnlyList<string> OutputsOf(string kind)` | The output ports of a kind, in the order the editor shows them. |

### WorkflowPorts

*static class*

| Member | |
|---|---|
| `const string Done` |  |
| `const string Each` |  |
| `const string First` |  |
| `const string Next` |  |
| `const string No` |  |
| `const string Second` |  |
| `const string Yes` |  |
| `static string Describe(string port)` |  |

### WorkflowTriggers

*static class* — The inputs a trigger node can start on.

| Member | |
|---|---|
| `const string DoublePress` |  |
| `const string Down` | The key goes down (right away). |
| `const string Hold` | Held down (the device's long press). |
| `const string RotateLeft` |  |
| `const string RotateRight` |  |
| `const string SwipeLeft` |  |
| `const string SwipeRight` |  |
| `const string Tap` | A short press: on release, not after a hold, and not when a second press follows (with a Double trigger). |
| `const string Up` | The key comes up. |
| `static string Describe(string trigger)` |  |
| `static string Immediate(InputCapabilities eventType, int value)` | The triggers a raw input fires at once (tap, double press and hold need timing: the host decides those). |
