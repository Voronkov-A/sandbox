namespace Picshare.Views;

using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

public partial class ProgressDialogView : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<ProgressDialogView, string>(nameof(Title), "");

    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<ProgressDialogView, string>(nameof(Message), "");

    public static readonly StyledProperty<bool> IsWarningVisibleProperty =
        AvaloniaProperty.Register<ProgressDialogView, bool>(nameof(IsWarningVisible));

    public static readonly StyledProperty<string> WarningProperty =
        AvaloniaProperty.Register<ProgressDialogView, string>(nameof(Warning), "");

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<ProgressDialogView, double>(nameof(Maximum), 1);

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ProgressDialogView, double>(nameof(Value));

    public static readonly StyledProperty<ICommand?> CancelCommandProperty =
        AvaloniaProperty.Register<ProgressDialogView, ICommand?>(nameof(CancelCommand));

    public ProgressDialogView()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public bool IsWarningVisible
    {
        get => GetValue(IsWarningVisibleProperty);
        set => SetValue(IsWarningVisibleProperty, value);
    }

    public string Warning
    {
        get => GetValue(WarningProperty);
        set => SetValue(WarningProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public ICommand? CancelCommand
    {
        get => GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }
}
