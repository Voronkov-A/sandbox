using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Picshare.ViewModels;
using Picshare.Services;
using Picshare.Views;

namespace Picshare.Tests;

[CollectionDefinition("Album row UI", DisableParallelization = true)]
public sealed class AlbumRowUiCollection;

[Collection("Album row UI")]
public sealed class AlbumPhotoRowPoolingTests
{
    static AlbumPhotoRowPoolingTests() => TestRenderingPlatform.EnsureInitialized();

    [Fact]
    public void RebindingThousandsOfRows_ReusesTheSameLightweightCardsAndNeverBuildsFullContent()
    {
        var list = new AlbumPhotoListBox();
        list.SetLightweightRendering(true);
        var models = Enumerable.Range(0, 1000).Select(index => Row(index, index % 8 + 1)).ToArray();
        list.ItemsSource = models;
        var generator = list.ItemContainerGenerator;
        Assert.True(generator.NeedsContainer(models[0], 0, out var key));
        var container = Assert.IsType<AlbumPhotoRowContainer>(generator.CreateContainer(models[0], 0, key));
        generator.PrepareItemContainer(container, models[7], 7);
        var originals = container.RowPresenter.GetVisualDescendants().OfType<LightweightAlbumPhotoCard>().ToArray();
        var originalImages = container.RowPresenter.GetVisualDescendants().OfType<Image>().ToArray();
        generator.ClearItemContainer(container);
        for (var index = 0; index < models.Length; index++)
        {
            generator.PrepareItemContainer(container, models[index], index);
            Assert.Same(container.RowPresenter, container.Content);
            var cards = container.RowPresenter.GetVisualDescendants().OfType<LightweightAlbumPhotoCard>().ToArray();
            Assert.Equal(originals, cards);
            Assert.Equal(models[index].Photos.Count, cards.Count(card => card.IsVisible));
            Assert.Equal(models[index].Photos[0].ListDisplayName,
                cards[0].GetVisualDescendants().OfType<TextBlock>().Single().Text);
            Assert.Equal(originalImages, container.RowPresenter.GetVisualDescendants().OfType<Image>());
            Assert.All(originalImages, image => Assert.Null(image.Source));
            Assert.Empty(container.RowPresenter.GetVisualDescendants().OfType<Button>());
            generator.ClearItemContainer(container);
            Assert.Null(container.DataContext);
            Assert.All(cards, card => Assert.False(card.IsVisible));
        }
        Assert.Equal(1, list.LightweightRowsCreated);
        Assert.Equal(1001, list.LightweightRowBindings);
        Assert.Equal(0, list.HeavyRowsCreated);
    }

    [Fact]
    public void GroupHeaders_UseADifferentRecycleKeyFromPhotoRows()
    {
        var list = new AlbumPhotoListBox();
        var generator = list.ItemContainerGenerator;
        Assert.True(generator.NeedsContainer(Row(1), 0, out var rowKey));
        Assert.True(generator.NeedsContainer(new AlbumPhotoGroupHeaderViewModel("Committed"), 1, out var headerKey));
        Assert.NotEqual(rowKey, headerKey);
    }

    [Fact]
    public void FullRows_RestoreOneAtATimeAndFastScrollingCancelsQueuedUpgrades()
    {
        var built = new List<object?>();
        var list = new AlbumPhotoListBox
        {
            HeavyRowTemplate = new FuncDataTemplate<AlbumPhotoRowViewModel>((model, _) =>
            {
                built.Add(model);
                return new TextBlock { Text = model!.Photos[0].FileName };
            })
        };
        list.SetLightweightRendering(true);
        var models = new[] { Row(1), Row(2) };
        list.ItemsSource = models;
        var generator = list.ItemContainerGenerator;
        var containers = models.Select((model, index) =>
        {
            generator.NeedsContainer(model, index, out var key);
            var container = (AlbumPhotoRowContainer)generator.CreateContainer(model, index, key);
            generator.PrepareItemContainer(container, model, index);
            return container;
        }).ToArray();
        list.SetLightweightRendering(false);
        var staleGeneration = Generation(list);
        RestoreOne(list, staleGeneration);
        Assert.Single(built);
        list.SetLightweightRendering(true);
        RestoreOne(list, staleGeneration);
        Assert.Single(built);
        Assert.All(containers, row => Assert.IsType<StackPanel>(row.RowPresenter.Child));
        list.SetLightweightRendering(false);
        RestoreOne(list, Generation(list));
        Assert.Equal(2, built.Count);
        RestoreOne(list, Generation(list));
        Assert.Equal(3, built.Count);
        Assert.All(containers, row => Assert.IsType<TextBlock>(row.RowPresenter.Child));
        foreach (var row in containers) generator.ClearItemContainer(row);
        list.SetLightweightRendering(true);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void RecycledRow_CannotBeUpgradedWithItsPreviousPhotoModel()
    {
        object? builtModel = null;
        var list = new AlbumPhotoListBox
        {
            HeavyRowTemplate = new FuncDataTemplate<AlbumPhotoRowViewModel>((model, _) =>
            { builtModel = model; return new TextBlock(); })
        };
        var oldModel = Row(1);
        var current = Row(2);
        list.ItemsSource = new[] { oldModel, current };
        var generator = list.ItemContainerGenerator;
        generator.NeedsContainer(oldModel, 0, out var key);
        var row = (AlbumPhotoRowContainer)generator.CreateContainer(oldModel, 0, key);
        generator.PrepareItemContainer(row, oldModel, 0);
        generator.ClearItemContainer(row);
        generator.PrepareItemContainer(row, current, 1);
        RestoreOne(list, Generation(list));
        Assert.Same(current, builtModel);
        Assert.Same(current, row.RowPresenter.Child!.DataContext);
        generator.ClearItemContainer(row);
        list.SetLightweightRendering(true);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void ClearedPresenter_DoesNotRetainPhotosOrTheirSizeSubscriptions()
    {
        var (presenter, model, photo) = BindAndClear();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(model.IsAlive);
        Assert.False(photo.IsAlive);
        GC.KeepAlive(presenter);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (AlbumPhotoRowPresenter Presenter, WeakReference Model, WeakReference Photo) BindAndClear()
    {
        var model = Row(1);
        var presenter = new AlbumPhotoRowPresenter();
        presenter.Bind(model);
        var references = (presenter, new WeakReference(model), new WeakReference(model.Photos[0]));
        presenter.Unbind();
        return references;
    }

    [Fact]
    public void LightweightAndFullRows_KeepTheSameGeometryAndUpdateAfterResize()
    {
        var view = new MainView();
        var template = (IDataTemplate)view.Resources["AlbumHeavyRowTemplate"]!;
        var presenter = new AlbumPhotoRowPresenter();
        var model = Row(1, 2);
        presenter.Bind(model);
        presenter.Measure(new Size(400, 300));
        var lightSize = presenter.DesiredSize;
        Assert.True(presenter.ShowHeavy(template));
        var full = Assert.IsType<ItemsControl>(presenter.Child);
        var theme = new FluentTheme();
        full.Styles.Add(theme);
        full.Theme = (Avalonia.Styling.ControlTheme)theme.Resources[typeof(ItemsControl)]!;
        full.ApplyStyling();
        full.Measure(new Size(400, 300));
        foreach (var content in full.GetVisualDescendants().OfType<ContentPresenter>().ToArray()) content.UpdateChild();
        presenter.Measure(new Size(400, 300));
        Assert.Equal(lightSize, presenter.DesiredSize);
        var cards = full.GetVisualDescendants().OfType<Border>().Where(card => card.Name == "AlbumPhotoCard").ToArray();
        Assert.Equal(2, cards.Length);
        Assert.Same(model.Photos[0], cards[0].DataContext);
        presenter.ShowLightweight();
        foreach (var photo in model.Photos) { photo.CardWidth = 150; photo.CardHeight = 110; }
        Dispatcher.UIThread.RunJobs();
        presenter.Measure(new Size(400, 300));
        Assert.Equal(new Size(324, 122), presenter.DesiredSize);
        presenter.Unbind();
    }

    [Fact]
    public void CachedImages_AreRotatedAndReleasedOnRebindingFullContentAndSuspension()
    {
        using var bitmap = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96));
        var releases = 0;
        AlbumImageBitmapLease? Acquire(AlbumPhotoViewModel _) => (AlbumImageBitmapLease)Activator.CreateInstance(
            typeof(AlbumImageBitmapLease), BindingFlags.Instance | BindingFlags.NonPublic, null,
            [bitmap, (Action)(() => releases++)], null)!;
        var presenter = new AlbumPhotoRowPresenter();
        var first = Row(1, 2);
        first.Photos[0].RotationDegrees = 90;
        presenter.Bind(first, Acquire);
        var images = presenter.GetVisualDescendants().OfType<Image>().ToArray();
        Assert.Equal(2, images.Length);
        Assert.All(images, image => Assert.Same(bitmap, image.Source));
        Assert.Equal(90, Assert.IsType<RotateTransform>(images[0].RenderTransform).Angle);

        presenter.Bind(Row(2, 1)); // Cache miss: never leave the old row's pictures behind.
        Assert.Equal(2, releases);
        Assert.All(images, image => Assert.Null(image.Source));
        presenter.SetCachedThumbnailProvider(Acquire);
        Assert.Same(bitmap, images[0].Source);
        Assert.Null(images[1].Source);
        Assert.True(presenter.ShowHeavy(new FuncDataTemplate<AlbumPhotoRowViewModel>((_, _) => new TextBlock())));
        Assert.Equal(3, releases);
        Assert.All(images, image => Assert.Null(image.Source));
        presenter.ShowLightweight();
        Assert.Same(bitmap, images[0].Source);
        presenter.ShowLightweight(showCachedImages: false);
        Assert.Equal(4, releases);
        Assert.All(images, image => Assert.Null(image.Source));
        presenter.Unbind();
        Assert.Equal(4, releases);
    }

    [Fact]
    public void ThumbnailProvider_RefreshesRealizedRowsAndIsUsedByRecycledContainers()
    {
        using var bitmap = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96));
        var releases = 0;
        AlbumImageBitmapLease? Acquire(AlbumPhotoViewModel _) => (AlbumImageBitmapLease)Activator.CreateInstance(
            typeof(AlbumImageBitmapLease), BindingFlags.Instance | BindingFlags.NonPublic, null,
            [bitmap, (Action)(() => releases++)], null)!;
        var list = new AlbumPhotoListBox { CachedThumbnailProvider = Acquire };
        list.SetLightweightRendering(true);
        var models = new[] { Row(1, 1), Row(2, 1) };
        list.ItemsSource = models;
        var generator = list.ItemContainerGenerator;
        generator.NeedsContainer(models[0], 0, out var key);
        var row = (AlbumPhotoRowContainer)generator.CreateContainer(models[0], 0, key);
        generator.PrepareItemContainer(row, models[0], 0);
        var image = row.RowPresenter.GetVisualDescendants().OfType<Image>().Single();
        Assert.Same(bitmap, image.Source);
        list.CachedThumbnailProvider = null;
        Assert.Null(image.Source);
        Assert.Equal(1, releases);
        list.CachedThumbnailProvider = Acquire;
        Assert.Same(bitmap, image.Source);
        generator.ClearItemContainer(row);
        Assert.Equal(2, releases);
        generator.PrepareItemContainer(row, models[1], 1);
        Assert.Same(image, row.RowPresenter.GetVisualDescendants().OfType<Image>().Single());
        Assert.Same(bitmap, image.Source);
        generator.ClearItemContainer(row);
        Assert.Equal(3, releases);
        Assert.Null(image.Source);
    }

    private static AlbumPhotoRowViewModel Row(int index, int count = 4) => new("Uncommitted",
        Enumerable.Range(0, count).Select(offset => new AlbumPhotoViewModel("album", $"{index}-{offset}", $"photo{index:0000}{offset}.jpg", "", "")
            { CardWidth = 120, CardHeight = 90 }).ToArray());

    private static int Generation(AlbumPhotoListBox list) =>
        (int)typeof(AlbumPhotoListBox).GetField("_restoreGeneration", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(list)!;
    private static void RestoreOne(AlbumPhotoListBox list, int generation) =>
        typeof(AlbumPhotoListBox).GetMethod("RestoreNextRow", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(list, [generation]);
}
