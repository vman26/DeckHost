using CommunityToolkit.Mvvm.Input;
using DeckHost.App.Platform;
using DeckHost.Core.Profiles;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace DeckHost.App.ViewModels;

/// <summary>
/// Profile Manager: create, duplicate, rename, reorder and delete profiles, and choose the apps
/// that switch to a profile automatically.
/// </summary>
public partial class ProfileManagerViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private ProfileItemViewModel? _editingProfile;
    private string _newAppName = string.Empty;
    private string? _selectedRunningApp;
    private bool _confirmingDelete;

    public ProfileManagerViewModel(MainViewModel main)
    {
        _main = main;
        _main.PropertyChanged += OnMainPropertyChanged;
        _editingProfile = main.SelectedProfile ?? main.Profiles.FirstOrDefault();
        RefreshRunningApps();
    }

    public ObservableCollection<ProfileItemViewModel> Profiles => _main.Profiles;

    public ObservableCollection<string> RunningApps { get; } = [];

    public ProfileItemViewModel? EditingProfile
    {
        get => _editingProfile;
        set
        {
            if (SetProperty(ref _editingProfile, value))
            {
                ConfirmingDelete = false;
                OnPropertyChanged(nameof(EditingName));
                OnPropertyChanged(nameof(EditingBackgroundColor));
                OnPropertyChanged(nameof(EditingBackgroundImage));
                OnPropertyChanged(nameof(BackgroundColorError));
                OnPropertyChanged(nameof(HasBackgroundColorError));
                OnPropertyChanged(nameof(HasEditingProfile));
                OnPropertyChanged(nameof(IsEditingActiveProfile));
                OnPropertyChanged(nameof(CanDelete));
            }
        }
    }

    public bool HasEditingProfile => EditingProfile is not null;

    public string EditingName
    {
        get => EditingProfile?.Name ?? string.Empty;
        set
        {
            if (EditingProfile is null || string.IsNullOrWhiteSpace(value) || value.Trim() == EditingProfile.Name)
            {
                return;
            }

            EditingProfile.Name = value;
            _main.NotifyProfilesEdited();
            OnPropertyChanged();
        }
    }

    /// <summary>Profile background color (#RRGGBB); applied when valid or empty.</summary>
    public string EditingBackgroundColor
    {
        get => EditingProfile?.BackgroundColor ?? string.Empty;
        set
        {
            if (EditingProfile is null || value == EditingProfile.BackgroundColor)
            {
                return;
            }

            EditingProfile.BackgroundColor = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BackgroundColorError));
            OnPropertyChanged(nameof(HasBackgroundColorError));
            if (BackgroundColorError is null)
            {
                _main.NotifyProfilesEdited();
            }
        }
    }

    public string? BackgroundColorError =>
        string.IsNullOrWhiteSpace(EditingBackgroundColor) ||
        System.Text.RegularExpressions.Regex.IsMatch(EditingBackgroundColor.Trim(), "^#?[0-9A-Fa-f]{6}$")
            ? null
            : "Use #RRGGBB, for example #0F172A.";

    public bool HasBackgroundColorError => BackgroundColorError is not null;

    /// <summary>Profile background image or GIF; spans all controls.</summary>
    public string EditingBackgroundImage
    {
        get => EditingProfile?.BackgroundImagePath ?? string.Empty;
        set
        {
            if (EditingProfile is null || value == EditingProfile.BackgroundImagePath)
            {
                return;
            }

            EditingProfile.BackgroundImagePath = value ?? string.Empty;
            OnPropertyChanged();
            _main.NotifyProfilesEdited();
        }
    }

    /// <summary>Background color for every profile without one of its own.</summary>
    public string DefaultBackgroundColor
    {
        get => _main.DefaultBackgroundColor;
        set
        {
            if (value != _main.DefaultBackgroundColor)
            {
                _main.DefaultBackgroundColor = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DefaultBackgroundColorError));
                OnPropertyChanged(nameof(HasDefaultBackgroundColorError));
            }
        }
    }

    public string? DefaultBackgroundColorError =>
        string.IsNullOrWhiteSpace(DefaultBackgroundColor) ||
        System.Text.RegularExpressions.Regex.IsMatch(DefaultBackgroundColor.Trim(), "^#?[0-9A-Fa-f]{6}$")
            ? null
            : "Use #RRGGBB, for example #0F172A.";

    public bool HasDefaultBackgroundColorError => DefaultBackgroundColorError is not null;

    /// <summary>Background image for every profile without a background of its own.</summary>
    public string DefaultBackgroundImage
    {
        get => _main.DefaultBackgroundImage;
        set
        {
            if (value != _main.DefaultBackgroundImage)
            {
                _main.DefaultBackgroundImage = value ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    [RelayCommand]
    private void ClearDefaultBackground()
    {
        DefaultBackgroundColor = string.Empty;
        DefaultBackgroundImage = string.Empty;
    }

    [RelayCommand]
    private void ClearBackground()
    {
        EditingBackgroundColor = string.Empty;
        EditingBackgroundImage = string.Empty;
    }

    public bool IsEditingActiveProfile => EditingProfile is not null && EditingProfile == _main.SelectedProfile;

    public bool CanDelete => EditingProfile is not null && Profiles.Count > 1;

    public bool ConfirmingDelete
    {
        get => _confirmingDelete;
        private set
        {
            if (SetProperty(ref _confirmingDelete, value))
            {
                OnPropertyChanged(nameof(DeleteButtonText));
            }
        }
    }

    public string DeleteButtonText => ConfirmingDelete ? "Click again to delete" : "Delete";

    public bool AutoSwitchEnabled
    {
        get => _main.AutoSwitchEnabled;
        set => _main.AutoSwitchEnabled = value;
    }

    public bool AutoSwitchSupported => _main.AutoSwitchSupported;

    public bool AutoSwitchUnsupported => !_main.AutoSwitchSupported;

    public string AutoSwitchUnsupportedReason => Platform.ForegroundAppWatcher.UnsupportedReason;

    public string CurrentAppName => _main.CurrentAppName;

    public string NewAppName
    {
        get => _newAppName;
        set => SetProperty(ref _newAppName, value);
    }

    public string? SelectedRunningApp
    {
        get => _selectedRunningApp;
        set => SetProperty(ref _selectedRunningApp, value);
    }

    public void Detach() => _main.PropertyChanged -= OnMainPropertyChanged;

    [RelayCommand]
    private void NewProfile() => EditingProfile = _main.CreateProfile("New Profile");

    [RelayCommand]
    private void DuplicateProfile()
    {
        if (EditingProfile is not null)
        {
            EditingProfile = _main.CreateProfile($"{EditingProfile.Name} copy", EditingProfile);
        }
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (EditingProfile is null || !CanDelete)
        {
            return;
        }

        // Deleting throws away the profile's mappings, so it takes a second click.
        if (!ConfirmingDelete)
        {
            ConfirmingDelete = true;
            return;
        }

        var index = Profiles.IndexOf(EditingProfile);
        if (_main.DeleteProfile(EditingProfile))
        {
            EditingProfile = Profiles[Math.Clamp(index, 0, Profiles.Count - 1)];
        }

        OnPropertyChanged(nameof(CanDelete));
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (EditingProfile is not null)
        {
            _main.MoveProfile(EditingProfile, -1);
        }
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (EditingProfile is not null)
        {
            _main.MoveProfile(EditingProfile, 1);
        }
    }

    [RelayCommand]
    private void UseNow()
    {
        if (EditingProfile is not null)
        {
            _main.SelectedProfile = EditingProfile;
        }
    }

    [RelayCommand]
    private void AddApp() => AddAppName(NewAppName);

    [RelayCommand]
    private void AddRunningApp() => AddAppName(SelectedRunningApp);

    [RelayCommand]
    private void RemoveApp(string app)
    {
        if (EditingProfile?.AutoSwitchApps.Remove(app) == true)
        {
            _main.NotifyProfilesEdited();
        }
    }

    [RelayCommand]
    private void RefreshRunningApps()
    {
        RunningApps.Clear();
        if (!ForegroundAppWatcher.IsSupported)
        {
            return;
        }

        foreach (var app in ForegroundAppWatcher.GetRunningApps())
        {
            RunningApps.Add(app);
        }
    }

    private void AddAppName(string? name)
    {
        var normalized = AutoProfileSelector.NormalizeAppName(name);
        if (EditingProfile is null || normalized.Length == 0)
        {
            return;
        }

        if (!EditingProfile.AutoSwitchApps.Any(app => AutoProfileSelector.NormalizeAppName(app) == normalized))
        {
            EditingProfile.AutoSwitchApps.Add(normalized);
            _main.NotifyProfilesEdited();
        }

        NewAppName = string.Empty;
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.CurrentAppName):
                OnPropertyChanged(nameof(CurrentAppName));
                break;
            case nameof(MainViewModel.AutoSwitchEnabled):
                OnPropertyChanged(nameof(AutoSwitchEnabled));
                break;
            case nameof(MainViewModel.SelectedProfile):
                OnPropertyChanged(nameof(IsEditingActiveProfile));
                break;
        }
    }
}
