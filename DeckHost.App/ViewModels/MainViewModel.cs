using CommunityToolkit.Mvvm.ComponentModel;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using System.Collections.ObjectModel;

namespace DeckHost.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private DeviceDefinition? _selectedDevice;

    public ObservableCollection<IDeckAction> AvailableActions { get; } = new();

    public bool CanDropAction(IDeckAction action, PhysicalControl targetControl)
    {
        // Bitwise capability validation
        bool inputsMatch = (targetControl.Inputs & action.RequiredInputs) == action.RequiredInputs;
        bool outputsMatch = (targetControl.Outputs & action.RequiredOutputs) == action.RequiredOutputs;

        return inputsMatch && outputsMatch;
    }

    public void AssignActionToControl(PhysicalControl control, IDeckAction action)
    {
        if (CanDropAction(action, control))
        {
            // Bind the action to the control ID in the active profile model
        }
    }
}
