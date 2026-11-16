using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Threading;
using Picshare.Services;
using Picshare.ViewModels;

namespace Picshare.Views;

/// <summary>Recycles lightweight row contents along with Avalonia's virtualized containers.</summary>
public class AlbumPhotoListBox : ListBox
{
    private readonly HashSet<AlbumPhotoRowContainer> _rows = [];
    private readonly Queue<(AlbumPhotoRowContainer Row, int Version)> _pendingHeavyRows = new();
    private int _restoreGeneration;
    private bool _restorePosted;
    private Func<AlbumPhotoViewModel, AlbumImageBitmapLease?>? _cachedThumbnailProvider;

    protected override Type StyleKeyOverride => typeof(ListBox);
    public IDataTemplate? HeavyRowTemplate { get; set; }
    public Func<AlbumPhotoViewModel, AlbumImageBitmapLease?>? CachedThumbnailProvider
    {
        get => _cachedThumbnailProvider;
        set
        {
            if (Equals(_cachedThumbnailProvider, value)) return;
            _cachedThumbnailProvider = value;
            foreach (var row in _rows) row.RowPresenter.SetCachedThumbnailProvider(value);
        }
    }
    public bool IsLightweightRendering { get; private set; }
    public long LightweightRowsCreated { get; private set; }
    public long LightweightRowBindings { get; private set; }
    public long HeavyRowsCreated { get; private set; }

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        if (item is AlbumPhotoRowViewModel)
        {
            recycleKey = typeof(AlbumPhotoRowContainer);
            return true;
        }
        return base.NeedsContainerOverride(item, index, out recycleKey);
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
    {
        if (item is not AlbumPhotoRowViewModel) return base.CreateContainerForItemOverride(item, index, recycleKey);
        LightweightRowsCreated++;
        return new AlbumPhotoRowContainer();
    }

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        if (container is AlbumPhotoRowContainer row && item is AlbumPhotoRowViewModel model)
        {
            row.RowPresenter.Bind(model, CachedThumbnailProvider);
            LightweightRowBindings++;
            _rows.Add(row);
            // Preparing with a Control preserves ListBox selection/theme bookkeeping without
            // asking ContentPresenter to construct a fresh data template for every row.
            base.PrepareContainerForItemOverride(container, row.RowPresenter, index);
            if (!IsLightweightRendering) QueueHeavyRow(row);
        }
        else base.PrepareContainerForItemOverride(container, item, index);
    }

    protected override void ClearContainerForItemOverride(Control container)
    {
        if (container is AlbumPhotoRowContainer row)
        {
            _rows.Remove(row);
            row.RowPresenter.Unbind();
        }
        base.ClearContainerForItemOverride(container);
        if (container is AlbumPhotoRowContainer) container.DataContext = null;
    }

    public void SetLightweightRendering(bool lightweight)
    {
        if (IsLightweightRendering == lightweight) return;
        IsLightweightRendering = lightweight;
        CancelRestoration();
        foreach (var row in _rows)
        {
            if (lightweight) row.RowPresenter.ShowLightweight();
            else QueueHeavyRow(row);
        }
    }

    private void QueueHeavyRow(AlbumPhotoRowContainer row)
    {
        _pendingHeavyRows.Enqueue((row, row.RowPresenter.Version));
        ScheduleRestoration();
    }

    private void ScheduleRestoration()
    {
        if (_restorePosted || IsLightweightRendering || _pendingHeavyRows.Count == 0) return;
        _restorePosted = true;
        var generation = _restoreGeneration;
        // Build at most one full row per frame and let input/rendering run first.
        if (TopLevel.GetTopLevel(this) is { } topLevel)
            topLevel.RequestAnimationFrame(_ => Dispatcher.UIThread.Post(() => RestoreNextRow(generation), DispatcherPriority.Background));
        else
            Dispatcher.UIThread.Post(() => RestoreNextRow(generation), DispatcherPriority.Background);
    }

    private void RestoreNextRow(int generation)
    {
        if (generation != _restoreGeneration || IsLightweightRendering) return;
        _restorePosted = false;
        while (_pendingHeavyRows.TryDequeue(out var pending))
        {
            if (!_rows.Contains(pending.Row) || pending.Row.RowPresenter.Version != pending.Version) continue;
            if (pending.Row.RowPresenter.ShowHeavy(HeavyRowTemplate)) HeavyRowsCreated++;
            break;
        }
        ScheduleRestoration();
    }

    private void CancelRestoration()
    {
        _restoreGeneration++;
        _restorePosted = false;
        _pendingHeavyRows.Clear();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelRestoration();
        foreach (var row in _rows) row.RowPresenter.ShowLightweight(showCachedImages: false);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        foreach (var row in _rows)
        {
            row.RowPresenter.ShowLightweight();
            if (!IsLightweightRendering) QueueHeavyRow(row);
        }
    }
}

public sealed class AlbumPhotoRowContainer : ListBoxItem
{
    protected override Type StyleKeyOverride => typeof(ListBoxItem);
    public AlbumPhotoRowPresenter RowPresenter { get; } = new();
}

public sealed class AlbumPhotoRowPresenter : Decorator
{
    private readonly StackPanel _lightweight = new() { Orientation = Avalonia.Layout.Orientation.Horizontal, IsHitTestVisible = false, DataContext = null };
    private readonly List<LightweightAlbumPhotoCard> _cards = [];
    private AlbumPhotoRowViewModel? _model;
    private AlbumPhotoViewModel? _sizingPhoto;
    private bool _sizeUpdateQueued;
    private Func<AlbumPhotoViewModel, AlbumImageBitmapLease?>? _cachedThumbnailProvider;
    public int Version { get; private set; }

    public void Bind(AlbumPhotoRowViewModel model, Func<AlbumPhotoViewModel, AlbumImageBitmapLease?>? cachedThumbnailProvider = null)
    {
        Unbind();
        _model = model;
        _cachedThumbnailProvider = cachedThumbnailProvider;
        _sizingPhoto = model.Photos.FirstOrDefault();
        if (_sizingPhoto is not null) _sizingPhoto.PropertyChanged += PhotoSizeChanged;
        while (_cards.Count < model.Photos.Count)
        {
            var card = new LightweightAlbumPhotoCard();
            _cards.Add(card);
            _lightweight.Children.Add(card);
        }
        for (var index = 0; index < _cards.Count; index++)
        {
            var photo = index < model.Photos.Count ? model.Photos[index] : null;
            _cards[index].SetPhoto(photo);
        }
        UpdateSize();
        RefreshCachedImages();
        Child = _lightweight;
    }

    public void SetCachedThumbnailProvider(Func<AlbumPhotoViewModel, AlbumImageBitmapLease?>? provider)
    {
        _cachedThumbnailProvider = provider;
        if (Child == _lightweight) RefreshCachedImages();
    }

    public void ShowLightweight(bool showCachedImages = true)
    {
        if (_model is null) return;
        if (showCachedImages) RefreshCachedImages();
        else ReleaseCachedImages();
        Child = _lightweight;
    }

    private void RefreshCachedImages()
    {
        if (_model is null) return;
        for (var index = 0; index < _model.Photos.Count; index++)
        {
            var photo = _model.Photos[index];
            _cards[index].SetCachedImage(_cachedThumbnailProvider?.Invoke(photo), photo.RotationDegrees);
        }
    }

    private void ReleaseCachedImages()
    {
        foreach (var card in _cards) card.SetCachedImage(null, 0);
    }

    public bool ShowHeavy(IDataTemplate? template)
    {
        if (_model is null || template is null || Child != _lightweight) return false;
        var content = template.Build(_model);
        if (content is null) return false;
        content.DataContext = _model;
        Child = content;
        ReleaseCachedImages();
        return true;
    }

    public void Unbind()
    {
        Version++;
        _sizeUpdateQueued = false;
        if (_sizingPhoto is not null) _sizingPhoto.PropertyChanged -= PhotoSizeChanged;
        _sizingPhoto = null;
        _model = null;
        _cachedThumbnailProvider = null;
        Child = _lightweight; // Drop the full UI; only the reusable skeleton stays pooled.
        foreach (var card in _cards) card.SetPhoto(null);
        Height = double.NaN;
        DataContext = null;
    }

    private void PhotoSizeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(AlbumPhotoViewModel.CardWidth) or nameof(AlbumPhotoViewModel.CardHeight)) || _sizeUpdateQueued) return;
        _sizeUpdateQueued = true;
        var version = Version;
        Dispatcher.UIThread.Post(() =>
        {
            if (version != Version) return;
            _sizeUpdateQueued = false;
            UpdateSize();
        }, DispatcherPriority.Background);
    }

    private void UpdateSize()
    {
        if (_model is null) return;
        for (var index = 0; index < _model.Photos.Count; index++)
            _cards[index].SetSize(_model.Photos[index]);
        Height = _sizingPhoto is null ? 0 : _sizingPhoto.CardHeight + 12;
    }
}

public sealed class LightweightAlbumPhotoCard : Border
{
    private readonly RotateTransform _rotation = new();
    private readonly Image _image = new() { Stretch = Stretch.UniformToFill, RenderTransformOrigin = RelativePoint.Center };
    private AlbumImageBitmapLease? _imageLease;
    private readonly TextBlock _caption = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    private static readonly IBrush PlaceholderBrush = new SolidColorBrush(Color.Parse("#E9ECE7"));
    private static readonly IBrush CaptionBrush = new SolidColorBrush(Color.Parse("#F2F2EE"));
    private static readonly IBrush OutlineBrush = new SolidColorBrush(Color.Parse("#D6D8D1"));

    public LightweightAlbumPhotoCard()
    {
        DataContext = null;
        Margin = new Thickness(0, 0, 12, 12);
        Background = PlaceholderBrush;
        BorderBrush = OutlineBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,40") };
        _image.RenderTransform = _rotation;
        grid.Children.Add(new Border { ClipToBounds = true, Child = _image });
        var caption = new Border { Background = CaptionBrush, Padding = new Thickness(8), Child = _caption };
        Grid.SetRow(caption, 1);
        grid.Children.Add(caption);
        Child = grid;
    }

    public void SetPhoto(AlbumPhotoViewModel? photo)
    {
        SetCachedImage(null, 0);
        IsVisible = photo is not null;
        _caption.Text = photo?.ListDisplayName ?? "";
        if (photo is not null) SetSize(photo);
    }

    public void SetCachedImage(AlbumImageBitmapLease? lease, int rotationDegrees)
    {
        var previous = _imageLease;
        _imageLease = lease;
        _rotation.Angle = rotationDegrees;
        _image.Source = lease?.Bitmap;
        if (!ReferenceEquals(previous, lease)) previous?.Dispose();
    }

    public void SetSize(AlbumPhotoViewModel photo)
    {
        Width = photo.CardWidth;
        Height = photo.CardHeight;
    }
}
