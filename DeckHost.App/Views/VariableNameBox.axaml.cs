using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;

namespace DeckHost.App.Views;

/// <summary>
/// A box for a variable: type any name, or open the list of the variables DeckHost knows. Shared by the workflow editor,
/// the Customize and Device Settings pages (a <see cref="ViewModels.VariableSettingField"/>) and the Background Studio.
/// </summary>
public partial class VariableNameBox : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<VariableNameBox, string?>(nameof(Text), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IEnumerable<string>?> ItemsProperty =
        AvaloniaProperty.Register<VariableNameBox, IEnumerable<string>?>(nameof(Items));

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<VariableNameBox, string?>(nameof(Watermark));

    public VariableNameBox()
    {
        InitializeComponent();
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IEnumerable<string>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    private void Box_OnGotFocus(object? sender, RoutedEventArgs e) => Box.IsDropDownOpen = true;

    private void List_OnClick(object? sender, RoutedEventArgs e)
    {
        if (Box.IsDropDownOpen)
        {
            Box.IsDropDownOpen = false;
            return;
        }

        Box.Focus();
        Box.IsDropDownOpen = true;
    }
}
