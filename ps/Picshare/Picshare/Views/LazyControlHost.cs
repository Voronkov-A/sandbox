namespace Picshare.Views;

using Avalonia;
using Avalonia.Controls;

public sealed class LazyControlHost : ContentControl
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<LazyControlHost, bool>(nameof(IsActive));

    public static readonly StyledProperty<Type?> ControlTypeProperty =
        AvaloniaProperty.Register<LazyControlHost, Type?>(nameof(ControlType));

    public static readonly StyledProperty<bool> KeepAliveProperty =
        AvaloniaProperty.Register<LazyControlHost, bool>(nameof(KeepAlive));

    public event EventHandler<Control>? ControlCreated;

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public Type? ControlType
    {
        get => GetValue(ControlTypeProperty);
        set => SetValue(ControlTypeProperty, value);
    }

    public bool KeepAlive
    {
        get => GetValue(KeepAliveProperty);
        set => SetValue(KeepAliveProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsActiveProperty || change.Property == ControlTypeProperty)
        {
            UpdateContent();
        }
        else if (change.Property == DataContextProperty && Content is Control control)
        {
            control.DataContext = DataContext;
        }
    }

    private void UpdateContent()
    {
        IsVisible = IsActive;
        if (!IsActive)
        {
            if (!KeepAlive)
            {
                Content = null;
            }

            return;
        }

        if (Content is null)
        {
            Content = CreateControl();
            ControlCreated?.Invoke(this, (Control)Content);
        }

        if (Content is Control control)
        {
            control.DataContext = DataContext;
        }

        if (Content is ILazyActivated lazyActivated)
        {
            lazyActivated.OnLazyActivated();
        }
    }

    private Control CreateControl()
    {
        var controlType = ControlType ?? throw new InvalidOperationException("Lazy control type is not set.");
        if (!typeof(Control).IsAssignableFrom(controlType))
        {
            throw new InvalidOperationException($"Lazy control type '{controlType.FullName}' is not an Avalonia control.");
        }

        return (Control)(Activator.CreateInstance(controlType)
            ?? throw new InvalidOperationException($"Lazy control type '{controlType.FullName}' could not be created."));
    }
}
