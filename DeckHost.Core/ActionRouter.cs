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
        private readonly Dictionary<string, IDeckAction> _actionsByKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _duplicateActionKeys = [];

        public ActionRouter(
            IVariableStore variableStore,
            IEnumerable<IApplicationPlugin> applicationPlugins,
            Action<ActionReference, Exception>? onActionFailed = null)
        {
            ArgumentNullException.ThrowIfNull(variableStore);
            ArgumentNullException.ThrowIfNull(applicationPlugins);

            _variableStore = variableStore;
            _onActionFailed = onActionFailed;

            foreach (var plugin in applicationPlugins)
            {
                foreach (var action in plugin.Actions)
                {
                    var key = BuildActionKey(plugin.Id, action.Id);
                    if (!_actionsByKey.TryAdd(key, action))
                    {
                        _duplicateActionKeys.Add(key);
                    }
                }
            }
        }

        public IReadOnlyList<string> DuplicateActionKeys => _duplicateActionKeys;

        public IDeckAction? FindAction(ActionReference reference)
        {
            ArgumentNullException.ThrowIfNull(reference);
            return _actionsByKey.GetValueOrDefault(BuildActionKey(reference.PluginId, reference.ActionId));
        }

        public bool Dispatch(DeviceProfile profile, DeviceInputEvent inputEvent)
        {
            ArgumentNullException.ThrowIfNull(profile);
            ArgumentNullException.ThrowIfNull(inputEvent);

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

            if (binding.ActionKind == ProfileActionKind.MultiAction)
            {
                var dispatched = false;
                foreach (var step in binding.Steps.OrderBy(static step => step.Order))
                {
                    dispatched |= DispatchActionReference(step.Action, inputEvent);
                }

                return dispatched;
            }

            return DispatchActionReference(binding.Action, inputEvent);
        }

        private bool DispatchActionReference(ActionReference actionReference, DeviceInputEvent inputEvent)
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
                action.Execute(new ActionContext(inputEvent.DeviceId, inputEvent.ControlId, _variableStore, inputEvent.EventType, inputEvent.Value));
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
