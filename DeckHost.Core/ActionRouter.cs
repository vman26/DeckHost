using DeckHost.Contracts;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Interfaces;
using DeckHost.Core.Models;

namespace DeckHost.Core
{
    public sealed class ActionRouter : IActionRouter
    {
        private readonly IVariableStore _variableStore;
        private readonly Action<ActionReference, Exception>? _onActionFailed;
        private Dictionary<string, IDeckAction> _actionsByKey = new(StringComparer.OrdinalIgnoreCase);

        // The set a Multi Action Switch's press ran, so its release goes to the same set.
        private readonly Dictionary<(string DeviceId, string ControlId), string> _switchSetAtPress = [];

        /// <summary>
        /// Runs work later on the host's worker (for steps with a wait): device id, delay, work.
        /// Without it, waits are skipped and every step runs at once.
        /// </summary>
        public Action<string, TimeSpan, Action>? Scheduler { get; set; }
        private List<string> _duplicateActionKeys = [];

        public ActionRouter(
            IVariableStore variableStore,
            IEnumerable<IApplicationPlugin> applicationPlugins,
            Action<ActionReference, Exception>? onActionFailed = null)
        {
            ArgumentNullException.ThrowIfNull(variableStore);
            _variableStore = variableStore;
            _onActionFailed = onActionFailed;
            SetApplicationPlugins(applicationPlugins);
        }

        public IReadOnlyList<string> DuplicateActionKeys => _duplicateActionKeys;

        public void SetApplicationPlugins(IEnumerable<IApplicationPlugin> applicationPlugins)
        {
            ArgumentNullException.ThrowIfNull(applicationPlugins);

            var actions = new Dictionary<string, IDeckAction>(StringComparer.OrdinalIgnoreCase);
            var duplicates = new List<string>();
            foreach (var plugin in applicationPlugins)
            {
                foreach (var action in plugin.Actions)
                {
                    var key = BuildActionKey(plugin.Id, action.Id);
                    if (!actions.TryAdd(key, action))
                    {
                        duplicates.Add(key);
                    }
                }
            }

            _actionsByKey = actions;
            _duplicateActionKeys = duplicates;
        }

        public IDeckAction? FindAction(ActionReference reference)
        {
            ArgumentNullException.ThrowIfNull(reference);
            return _actionsByKey.GetValueOrDefault(BuildActionKey(reference.PluginId, reference.ActionId));
        }

        public bool Dispatch(DeviceProfile profile, DeviceInputEvent inputEvent, IActionHost host)
        {
            ArgumentNullException.ThrowIfNull(profile);
            ArgumentNullException.ThrowIfNull(inputEvent);
            ArgumentNullException.ThrowIfNull(host);

            if (!string.Equals(profile.DeviceId, inputEvent.DeviceId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            PublishInputState(inputEvent);

            var binding = profile.Bindings.FirstOrDefault(candidate =>
                string.Equals(candidate.ControlId, inputEvent.ControlId, StringComparison.OrdinalIgnoreCase));

            if (binding is null)
            {
                return false;
            }

            switch (binding.ActionKind)
            {
                case ProfileActionKind.MultiAction:
                {
                    // The Multi Action itself runs too (it records the activation for its own label).
                    var dispatched = DispatchActionReference(binding.Action, inputEvent, host, profile.Id);
                    return RunSteps(binding.Steps.OrderBy(static step => step.Order).ToList(), inputEvent, host, profile.Id) | dispatched;
                }

                case ProfileActionKind.MultiActionSwitch:
                {
                    // Each press runs the next set; its release goes to the set the press ran.
                    var key = (inputEvent.DeviceId, inputEvent.ControlId);
                    var stateKey = VariableKeys.For(inputEvent.DeviceId, inputEvent.ControlId, VariableKeys.SwitchState);
                    string set;
                    if (inputEvent.EventType == InputCapabilities.Press)
                    {
                        set = _variableStore.TryGetValue<string>(stateKey, out var current) && current == StepGroups.Second ? StepGroups.Second : StepGroups.First;
                        _switchSetAtPress[key] = set;
                        _variableStore.Publish(stateKey, set == StepGroups.First ? StepGroups.Second : StepGroups.First);
                    }
                    else
                    {
                        set = _switchSetAtPress.GetValueOrDefault(key, StepGroups.First);
                    }

                    var dispatched = DispatchActionReference(binding.Action, inputEvent, host, profile.Id);
                    return RunSteps(StepsOf(binding, set), inputEvent, host, profile.Id) | dispatched;
                }

                case ProfileActionKind.KeyLogic:
                    // The host tells a press, a double press and a hold apart (it needs timing) and runs DispatchGroup.
                    return false;

                default:
                    return DispatchActionReference(binding.Action, inputEvent, host, profile.Id);
            }
        }

        /// <summary>
        /// Runs one set of a binding's steps (Key Logic: press, double press or hold) as a press and
        /// release of the control, with the steps' waits.
        /// </summary>
        public bool DispatchGroup(DeviceProfile profile, ControlBinding binding, string group, string deviceId, IActionHost host)
        {
            ArgumentNullException.ThrowIfNull(profile);
            ArgumentNullException.ThrowIfNull(binding);
            var steps = StepsOf(binding, group);
            if (steps.Count == 0)
            {
                return false;
            }

            // The binding's own action (Key Logic's label) records the activation.
            DispatchActionReference(binding.Action, new DeviceInputEvent(deviceId, binding.ControlId, InputCapabilities.Press, 1), host, profile.Id);
            var dispatched = RunSteps(steps, new DeviceInputEvent(deviceId, binding.ControlId, InputCapabilities.Press, 1), host, profile.Id);
            dispatched |= RunSteps(steps, new DeviceInputEvent(deviceId, binding.ControlId, InputCapabilities.Release, 0), host, profile.Id);
            return dispatched;
        }

        /// <summary>Whether a Key Logic binding has steps for a set (no double-press steps: a press runs at once).</summary>
        public static bool HasGroup(ControlBinding binding, string group) => StepsOf(binding, group).Count > 0;

        private static List<MultiActionStep> StepsOf(ControlBinding binding, string group) =>
            [.. binding.Steps.Where(step => StepGroups.Of(step, binding.ActionKind) == group).OrderBy(static step => step.Order)];

        /// <summary>
        /// Runs steps in order. At a step with a wait the rest is scheduled on the host's worker
        /// (the worker is never blocked); without a scheduler waits are skipped.
        /// </summary>
        private bool RunSteps(IReadOnlyList<MultiActionStep> steps, DeviceInputEvent inputEvent, IActionHost host, string profileId, int from = 0, bool waitedForFirst = false)
        {
            var dispatched = false;
            for (var index = from; index < steps.Count; index++)
            {
                var delay = Math.Clamp(steps[index].DelayMs, 0, StepGroups.MaxDelayMs);
                if (delay > 0 && Scheduler is { } schedule && !(waitedForFirst && index == from))
                {
                    var next = index;
                    schedule(inputEvent.DeviceId, TimeSpan.FromMilliseconds(delay), () => RunSteps(steps, inputEvent, host, profileId, next, waitedForFirst: true));
                    return true;
                }

                dispatched |= DispatchActionReference(steps[index].Action, inputEvent, host, profileId);
            }

            return dispatched;
        }

        private bool DispatchActionReference(ActionReference actionReference, DeviceInputEvent inputEvent, IActionHost host, string profileId)
        {
            var action = FindAction(actionReference);
            if (action is null)
            {
                return false;
            }

            if (action.RequiredInputs != InputCapabilities.None &&
                (action.RequiredInputs & inputEvent.EventType) != inputEvent.EventType)
            {
                return false;
            }

            try
            {
                action.Execute(new ActionContext(inputEvent.DeviceId, inputEvent.ControlId, _variableStore, inputEvent.EventType, inputEvent.Value)
                {
                    // Settings that differ per input (a knob turned left or right) as they are for this one.
                    Settings = actionReference.EffectiveSettings.ForInput(action.Settings, ControlInputs.FromEvent(inputEvent.EventType, inputEvent.Value)),
                    Host = host,
                    ProfileId = profileId,
                });
                return true;
            }
            catch (Exception ex) when (_onActionFailed is not null)
            {
                // One failing step must not stop the rest of a Multi Action.
                _onActionFailed(actionReference, ex);
                return false;
            }
        }

        private void PublishInputState(DeviceInputEvent inputEvent)
        {
            var prefix = VariableKeys.Control(inputEvent.DeviceId, inputEvent.ControlId);
            _variableStore.Publish(prefix + VariableKeys.LastEvent, inputEvent.EventType);
            _variableStore.Publish(prefix + VariableKeys.LastValue, inputEvent.Value);
            _variableStore.Publish(VariableKeys.Device(inputEvent.DeviceId) + VariableKeys.LastControl, inputEvent.ControlId);
        }

        private static string BuildActionKey(string pluginId, string actionId) => $"{pluginId}:{actionId}";
    }
}
