using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace Picshare.Views;

public enum PhotoViewerSwipeDirection { None, Left, Top, Right, Bottom }

public partial class PhotoViewerSwipeControl : UserControl
{
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

    public PhotoViewerSwipeControl() => InitializeComponent();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
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
