using DeckHost.Core.Interfaces;
using DeckHost.Core.Models;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;

namespace DeckHost.Core
{
    public sealed class ActionRouter : IActionRouter
    {
        private readonly IVariableStore _variableStore;
        private readonly Dictionary<string, IDeckAction> _actionsByKey;

        public ActionRouter(IVariableStore variableStore, IEnumerable<IApplicationPlugin> applicationPlugins)
        {
            ArgumentNullException.ThrowIfNull(variableStore);
            ArgumentNullException.ThrowIfNull(applicationPlugins);

            _variableStore = variableStore;
            _actionsByKey = applicationPlugins
                .SelectMany(static plugin => plugin.Actions.Select(action => new KeyValuePair<string, IDeckAction>(BuildActionKey(plugin.Id, action.Id), action)))
                .ToDictionary();
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
                return DispatchMultiAction(binding, inputEvent);
            }

            return DispatchActionReference(binding.Action, inputEvent);
        }

        private bool DispatchMultiAction(ControlBinding binding, DeviceInputEvent inputEvent)
        {
            var dispatched = false;

            foreach (var step in binding.Steps.OrderBy(static step => step.Order))
            {
                dispatched |= DispatchActionReference(step.Action, inputEvent);
            }

            return dispatched;
        }

        private bool DispatchActionReference(ActionReference actionReference, DeviceInputEvent inputEvent)
        {
            if (!_actionsByKey.TryGetValue(BuildActionKey(actionReference.PluginId, actionReference.ActionId), out var action))
            {
                return false;
            }

            if (action.RequiredInputs != DeckHost.Contracts.Enums.InputCapabilities.None &&
                (action.RequiredInputs & inputEvent.EventType) != inputEvent.EventType)
            {
                return false;
            }

            action.Execute(new ActionContext(inputEvent.DeviceId, inputEvent.ControlId, _variableStore));
            return true;
        }

        private void PublishInputState(DeviceInputEvent inputEvent)
        {
            _variableStore.Publish($"devices.{inputEvent.DeviceId}.{inputEvent.ControlId}.lastEvent", inputEvent.EventType);
            _variableStore.Publish($"devices.{inputEvent.DeviceId}.{inputEvent.ControlId}.lastValue", inputEvent.Value);
            _variableStore.Publish($"devices.{inputEvent.DeviceId}.lastControl", inputEvent.ControlId);
        }

        private static string BuildActionKey(string pluginId, string actionId) => $"{pluginId}:{actionId}";
    }
}
