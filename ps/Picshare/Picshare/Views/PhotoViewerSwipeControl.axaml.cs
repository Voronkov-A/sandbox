using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Media;
using Picshare.ViewModels;

namespace Picshare.Views;

public enum PhotoViewerSwipeDirection { None, Left, Top, Right, Bottom }

public partial class PhotoViewerSwipeControl : UserControl
{
    public static readonly StyledProperty<string> LeftActionProperty =
        AvaloniaProperty.Register<PhotoViewerSwipeControl, string>(nameof(LeftAction), "next");
    public static readonly StyledProperty<string> RightActionProperty =
        AvaloniaProperty.Register<PhotoViewerSwipeControl, string>(nameof(RightAction), "previous");
    public static readonly StyledProperty<string> TopActionProperty =
        AvaloniaProperty.Register<PhotoViewerSwipeControl, string>(nameof(TopAction), "nice");
    public static readonly StyledProperty<string> BottomActionProperty =
        AvaloniaProperty.Register<PhotoViewerSwipeControl, string>(nameof(BottomAction), "okay");

    public string LeftAction { get => GetValue(LeftActionProperty); set => SetValue(LeftActionProperty, value); }
    public string RightAction { get => GetValue(RightActionProperty); set => SetValue(RightActionProperty, value); }
    public string TopAction { get => GetValue(TopActionProperty); set => SetValue(TopActionProperty, value); }
    public string BottomAction { get => GetValue(BottomActionProperty); set => SetValue(BottomActionProperty, value); }

    private static readonly IReadOnlyDictionary<string, Geometry> ActionIcons = new Dictionary<string, Geometry>
    {
        ["previous"] = Geometry.Parse("M12,4 L6,10 L12,16"),
        ["next"] = Geometry.Parse("M8,4 L14,10 L8,16"),
        ["nice"] = Geometry.Parse("M5,7 L9,3 L15,3 L18,8 L15,16 L8,18 L3,14 Z M8,8 L8,9 M14,8 L14,9 M8,13 Q11,16 15,13"),
        ["okay"] = Geometry.Parse("M5,7 L9,3 L15,3 L18,8 L15,16 L8,18 L3,14 Z M8,8 L8,9 M14,8 L14,9 M8,14 L15,14"),
        ["rotate-left"] = Geometry.Parse("M7,4 L3,8 L7,12 M3,8 L11,8 A6,6 0 1 1 6,17"),
        ["rotate-right"] = Geometry.Parse("M13,4 L17,8 L13,12 M17,8 L9,8 A6,6 0 1 0 14,17")
    };
    public static readonly StyledProperty<ICommand?> LeftCommandProperty =
        AvaloniaProperty.Register<PhotoViewerSwipeControl, ICommand?>(nameof(LeftCommand));
    public static readonly StyledProperty<ICommand?> RightCommandProperty =
        AvaloniaProperty.Register<PhotoViewerSwipeControl, ICommand?>(nameof(RightCommand));
    public static readonly StyledProperty<ICommand?> TopCommandProperty =
        AvaloniaProperty.Register<PhotoViewerSwipeControl, ICommand?>(nameof(TopCommand));
    public static readonly StyledProperty<ICommand?> BottomCommandProperty =
        AvaloniaProperty.Register<PhotoViewerSwipeControl, ICommand?>(nameof(BottomCommand));
    public static readonly StyledProperty<bool> CanGoLeftProperty =
        AvaloniaProperty.Register<PhotoViewerSwipeControl, bool>(nameof(CanGoLeft));
    public static readonly StyledProperty<bool> CanGoRightProperty =
        AvaloniaProperty.Register<PhotoViewerSwipeControl, bool>(nameof(CanGoRight));
    public static readonly StyledProperty<bool> CanMoveTopProperty =
        AvaloniaProperty.Register<PhotoViewerSwipeControl, bool>(nameof(CanMoveTop));
    public static readonly StyledProperty<bool> CanMoveBottomProperty =
        AvaloniaProperty.Register<PhotoViewerSwipeControl, bool>(nameof(CanMoveBottom));
    public static readonly StyledProperty<PhotoViewerSwipeDirection> SelectedDirectionProperty =
        AvaloniaProperty.Register<PhotoViewerSwipeControl, PhotoViewerSwipeDirection>(nameof(SelectedDirection));

    public ICommand? LeftCommand { get => GetValue(LeftCommandProperty); set => SetValue(LeftCommandProperty, value); }
    public ICommand? RightCommand { get => GetValue(RightCommandProperty); set => SetValue(RightCommandProperty, value); }
    public ICommand? TopCommand { get => GetValue(TopCommandProperty); set => SetValue(TopCommandProperty, value); }
    public ICommand? BottomCommand { get => GetValue(BottomCommandProperty); set => SetValue(BottomCommandProperty, value); }
    public bool CanGoLeft { get => GetValue(CanGoLeftProperty); set => SetValue(CanGoLeftProperty, value); }
    public bool CanGoRight { get => GetValue(CanGoRightProperty); set => SetValue(CanGoRightProperty, value); }
    public bool CanMoveTop { get => GetValue(CanMoveTopProperty); set => SetValue(CanMoveTopProperty, value); }
    public bool CanMoveBottom { get => GetValue(CanMoveBottomProperty); set => SetValue(CanMoveBottomProperty, value); }
    public PhotoViewerSwipeDirection SelectedDirection
    {
        get => GetValue(SelectedDirectionProperty);
        set => SetValue(SelectedDirectionProperty, value);
    }

    public PhotoViewerSwipeControl()
    {
        InitializeComponent();
        UpdateActionIcons();
    }

    private void UpdateActionIcons()
    {
        if (LeftButton is null || RightButton is null || TopButton is null || BottomButton is null) return;
        Update(LeftButton, LeftIcon, LeftAction, "next");
        Update(RightButton, RightIcon, RightAction, "previous");
        Update(TopButton, TopIcon, TopAction, "nice");
        Update(BottomButton, BottomIcon, BottomAction, "okay");
        static void Update(Button button, Avalonia.Controls.Shapes.Path icon, string action, string fallback)
        {
            var option = PhotoViewerActionOption.Find(action, fallback);
            icon.Data = ActionIcons[option.Id];
            AutomationProperties.SetName(button, option.Name);
            ToolTip.SetTip(button, option.Name);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LeftActionProperty || change.Property == RightActionProperty ||
            change.Property == TopActionProperty || change.Property == BottomActionProperty)
            UpdateActionIcons();
        if (change.Property == SelectedDirectionProperty)
        {
            LeftButton.Classes.Set("selected", SelectedDirection == PhotoViewerSwipeDirection.Left);
            RightButton.Classes.Set("selected", SelectedDirection == PhotoViewerSwipeDirection.Right);
            TopButton.Classes.Set("selected", SelectedDirection == PhotoViewerSwipeDirection.Top);
            BottomButton.Classes.Set("selected", SelectedDirection == PhotoViewerSwipeDirection.Bottom);
        }
    }

    public bool TryExecuteSelectedAction()
    {
        var (enabled, command) = SelectedDirection switch
        {
            PhotoViewerSwipeDirection.Left => (CanGoLeft, LeftCommand),
            PhotoViewerSwipeDirection.Right => (CanGoRight, RightCommand),
            PhotoViewerSwipeDirection.Top => (CanMoveTop, TopCommand),
            PhotoViewerSwipeDirection.Bottom => (CanMoveBottom, BottomCommand),
            _ => (false, null)
        };
        SelectedDirection = PhotoViewerSwipeDirection.None;
        if (!enabled || command?.CanExecute(null) != true) return false;
        command.Execute(null);
        return true;
    }

    public static PhotoViewerSwipeDirection GetDirection(Vector delta)
    {
        // Ignore tap jitter, then choose the closest axis. Exact diagonals choose horizontally.
        const double minimumDistance = 6;
        if (!double.IsFinite(delta.X) || !double.IsFinite(delta.Y) || delta.Length < minimumDistance)
            return PhotoViewerSwipeDirection.None;
        var horizontal = Math.Abs(delta.X);
        var vertical = Math.Abs(delta.Y);
        if (horizontal >= vertical)
            return delta.X < 0 ? PhotoViewerSwipeDirection.Left : PhotoViewerSwipeDirection.Right;
        return delta.Y < 0 ? PhotoViewerSwipeDirection.Top : PhotoViewerSwipeDirection.Bottom;
    }
}
