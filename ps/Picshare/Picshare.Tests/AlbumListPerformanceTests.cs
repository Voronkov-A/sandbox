using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using Picshare.Services;
using Picshare.ViewModels;
using Picshare.Views;

namespace Picshare.Tests;

public sealed class AlbumListPerformanceTests
{
    static AlbumListPerformanceTests() => TestRenderingPlatform.EnsureInitialized();

    [Fact]
    public void Counters_ReportIntervalCountsTotalTimeAndMaximumWithoutRetainingEvents()
    {
        var counters = new AlbumListPerformanceCounters();
        var first = counters.Capture();
        counters.Count(AlbumListMetric.CardsBuilt);
        counters.Record(AlbumListMetric.RowMeasure, Stopwatch.Frequency / 100);
        counters.Record(AlbumListMetric.RowMeasure, Stopwatch.Frequency / 50);
        var second = counters.Capture();
        var description = second.DescribeChanges(first);
        Assert.Contains("CardsBuilt=1", description);
        Assert.Contains("RowMeasure=2/30.00ms/max=20.00ms", description);
        counters.Record(AlbumListMetric.RowMeasure, Stopwatch.Frequency / 200);
        var third = counters.Capture();
        Assert.Contains("CardsBuilt=0", third.DescribeChanges(second));
        Assert.Contains("RowMeasure=1/5.00ms/max=5.00ms", third.DescribeChanges(second));
    }

    [Fact]
    public void MeasuredControls_DistinguishConstructionFromRebindingAndPreserveLayout()
    {
        var counters = AlbumListPerformanceCounters.Shared;
        var before = counters.Capture();
        var row = new MeasuredAlbumPhotoRow();
        var card = new MeasuredAlbumPhotoCard { Width = 120, Height = 90 };
        row.DataContext = new object();
        card.DataContext = new object();
        row.DataContext = null;
        card.DataContext = null;
        row.DataContext = new object();
        card.DataContext = new object();
        card.Measure(new Size(200, 200));
        card.Arrange(new Rect(0, 0, 120, 90));
        Assert.Equal(new Size(120, 90), card.Bounds.Size);
        var after = counters.Capture();
        // Other tests may construct real views concurrently; counters are process-wide.
        Assert.True(after.Values[(int)AlbumListMetric.CardsBuilt].Count > before.Values[(int)AlbumListMetric.CardsBuilt].Count);
        Assert.True(after.Values[(int)AlbumListMetric.RowsRebound].Count > before.Values[(int)AlbumListMetric.RowsRebound].Count);
        Assert.True(after.Values[(int)AlbumListMetric.CardsRebound].Count > before.Values[(int)AlbumListMetric.CardsRebound].Count);
    }

    [Fact]
    public void InstrumentedRowTemplate_StillRealizesPhotoCardsWithTheirBindings()
    {
        var view = new MainView();
        var first = new AlbumPhotoViewModel("album", "one", "photo001.jpg", "", "") { CardWidth = 120, CardHeight = 90 };
        var second = new AlbumPhotoViewModel("album", "two", "photo002.jpg", "", "") { CardWidth = 120, CardHeight = 90 };
        var model = new AlbumPhotoRowViewModel("Uncommitted", [first, second]);
        var template = view.DataTemplates.Single(template => template.Match(model));
        var row = Assert.IsType<MeasuredAlbumPhotoRow>(template.Build(model));
        var theme = new FluentTheme();
        row.Styles.Add(theme);
        row.Theme = (Avalonia.Styling.ControlTheme)theme.Resources[typeof(ItemsControl)]!;
        row.DataContext = model;
        row.ApplyStyling();
        row.Measure(new Size(400, 200));
        Assert.Equal(2, row.ItemCount);
        // Detached presenters defer their data templates until logical attachment. Force
        // that documented test hook without requiring a native window/platform host.
        foreach (var presenter in row.GetVisualDescendants().OfType<ContentPresenter>().ToArray())
            presenter.UpdateChild();
        row.Measure(new Size(400, 200));
        row.Arrange(new Rect(0, 0, 400, row.DesiredSize.Height));
        var cards = row.GetVisualDescendants().OfType<MeasuredAlbumPhotoCard>().ToArray();
        Assert.Equal(2, cards.Length);
        Assert.Same(first, cards[0].DataContext);
        Assert.Same(second, cards[1].DataContext);
        Assert.All(cards, card => Assert.Equal(new Size(120, 90), card.Bounds.Size));
    }
}
