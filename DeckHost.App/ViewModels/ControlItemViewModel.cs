using Avalonia.Media;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using System;

namespace DeckHost.App.ViewModels;

public class ControlItemViewModel : ViewModelBase
{
    private const double CompactHeight = 60;

    private static readonly IBrush SelectedBrush = new SolidColorBrush(Color.Parse("#2563EB"));
    private static readonly IBrush AssignedBrush = new SolidColorBrush(Color.Parse("#0F766E"));
    private static readonly IBrush DefaultBrush = new SolidColorBrush(Color.Parse("#27272A"));

    private ControlBindingViewModel? _assignedBinding;
    private bool _isSelected;

    public ControlItemViewModel(PhysicalControl control)
    {
        Control = control;
        Id = control.Id;
        DisplayName = control.DisplayName;
        Type = control.Type;
        X = control.Bounds.X;
        Y = control.Bounds.Y;
        Width = control.Bounds.Width;
        Height = control.Bounds.Height;
        InputsLabel = CapabilityFormatter.Format(control.Inputs, "None");
        OutputsLabel = CapabilityFormatter.Format(control.Outputs, "None");
        SupportsAppearance = control.Outputs != OutputCapabilities.None;
    }

    public PhysicalControl Control { get; }

    public string Id { get; }

    public string DisplayName { get; }

    public ControlType Type { get; }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    public bool SupportsAppearance { get; }

    public bool SupportsLight => Control.HasLight;

    /// <summary>Every control can be customized: appearance, action settings, Multi Action steps or its LED.</summary>
    public bool SupportsCustomization => true;

    /// <summary>Thin controls (the swipe bar) only show their name.</summary>
    public bool IsCompact => Height < CompactHeight;

    public bool ShowDetails => !IsCompact;

    public string InputsLabel { get; }

    public string OutputsLabel { get; }

    public string TypeLabel => Type switch
    {
        ControlType.Key => "Key",
        ControlType.Encoder => "Knob",
        ControlType.TouchSurface => "Touch",
        ControlType.Display => "Display",
        ControlType.Slider => "Slider",
        _ => Type.ToString(),
    };

    public ControlBindingViewModel? AssignedBinding
    {
        get => _assignedBinding;
        set
        {
            if (ReferenceEquals(_assignedBinding, value))
            {
                return;
            }

            _assignedBinding = value;
            OnPropertyChanged(nameof(AssignedBinding));
            RefreshComputedState();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(SurfaceBrush));
            }
        }
    }

    public string AssignmentLabel => AssignedBinding?.DisplayName ?? "Drop action here";

    public bool HasAssignment => AssignedBinding is not null;

    public bool ShowRemoveHint => HasAssignment && ShowDetails;

    public string CapabilitySummary => $"{InputsLabel} / {OutputsLabel}";

    public string PreviewText => !string.IsNullOrWhiteSpace(AssignedBinding?.AppearanceText)
        ? AssignedBinding.AppearanceText
        : AssignedBinding is null ? DisplayName
        : AssignedBinding.IsDisplayContentOnly ? string.Empty
        : AssignedBinding.DisplayName;

    public string PreviewImageLabel => string.IsNullOrWhiteSpace(AssignedBinding?.AppearanceImagePath)
        ? string.Empty
        : System.IO.Path.GetFileName(AssignedBinding.AppearanceImagePath);

    public bool HasPreviewImage => !string.IsNullOrWhiteSpace(AssignedBinding?.AppearanceImagePath);

    public bool ShowPreviewLabel => SupportsAppearance;

    public IBrush SurfaceBrush => IsSelected ? SelectedBrush : AssignedBinding is null ? DefaultBrush : AssignedBrush;

    public double CornerRadius => Type == ControlType.Encoder ? Width / 2d : Math.Min(14d, Height / 3d);


    private void RefreshComputedState()
    {
        OnPropertyChanged(nameof(HasAssignment));
        OnPropertyChanged(nameof(ShowRemoveHint));
        OnPropertyChanged(nameof(AssignmentLabel));
        OnPropertyChanged(nameof(SurfaceBrush));
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(PreviewImageLabel));
        OnPropertyChanged(nameof(HasPreviewImage));
    }
}
