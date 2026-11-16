using Avalonia;
using Avalonia.Controls;
using Picshare.Services;

namespace Picshare.Views;

// These retain the existing UI and layout behavior. Constructor counts distinguish real
// template construction from Loaded/Unloaded events, which can occur on reused controls.
public sealed class MeasuredAlbumPhotoCard : Border
{
    private bool _hasBeenBound;
    protected override Type StyleKeyOverride => typeof(Border);
    public MeasuredAlbumPhotoCard() => AlbumListPerformanceCounters.Shared.Count(AlbumListMetric.CardsBuilt);

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (DataContext is not null)
        {
            if (_hasBeenBound)
                AlbumListPerformanceCounters.Shared.Count(AlbumListMetric.CardsRebound);
            _hasBeenBound = true;
        }
        base.OnDataContextChanged(e);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        using var timing = AlbumListPerformanceCounters.Shared.Measure(AlbumListMetric.CardMeasure);
        return base.MeasureOverride(availableSize);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        using var timing = AlbumListPerformanceCounters.Shared.Measure(AlbumListMetric.CardArrange);
        return base.ArrangeOverride(finalSize);
    }
}

public sealed class MeasuredAlbumPhotoRow : ItemsControl
{
    private bool _hasBeenBound;
    // Preserve the normal ItemsControl theme and selectors.
    protected override Type StyleKeyOverride => typeof(ItemsControl);
    public MeasuredAlbumPhotoRow() => AlbumListPerformanceCounters.Shared.Count(AlbumListMetric.RowsBuilt);

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (DataContext is not null)
        {
            if (_hasBeenBound)
                AlbumListPerformanceCounters.Shared.Count(AlbumListMetric.RowsRebound);
            _hasBeenBound = true;
        }
        base.OnDataContextChanged(e);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        using var timing = AlbumListPerformanceCounters.Shared.Measure(AlbumListMetric.RowMeasure);
        return base.MeasureOverride(availableSize);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        using var timing = AlbumListPerformanceCounters.Shared.Measure(AlbumListMetric.RowArrange);
        return base.ArrangeOverride(finalSize);
    }
}
