using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Picshare.Services;
using Picshare.ViewModels;

namespace Picshare.Tests;

public sealed partial class AlbumImageListLoaderWarmupTests
{
    [Fact]
    public void ScrollingPause_BlocksListAndWarmupWorkAndResumesForLatestViewport()
    {
        using var directory = new TempDirectory();
        using var client = new HttpClient(new BytesHandler([1, 2, 3]));
        using var loader = new AlbumImageListLoader(new ImageCacheService(directory.Path), client);
        var first = ScrollingPhoto("first");
        var second = ScrollingPhoto("second");
        SetLoaderPhotos(loader, [first, second], 1);
        loader.UpdateViewport([first]);
        loader.SetListLoadingPaused(true);
        Assert.False(TryTakeVisibleFastWork(loader, 1, out _, out _, out _));
        Assert.False(TryTakeVisibleThumbnailWork(loader, 1, true, out _, out _, out _, out _, out _, out _));
        foreach (var work in new[] { "FastThumbnail", "DetailedThumbnail", "OriginalImage" })
            Assert.False(TryTakeNearestWork(loader, work, 1, out _, out _, out _));
        Assert.False(first.IsImageLoadBusyAtomic());

        loader.UpdateViewport([second]);
        loader.SetListLoadingPaused(false);
        Assert.True(TryTakeVisibleFastWork(loader, 1, out var selected, out _, out var token));
        Assert.Same(second, selected);
        second.CompleteFastThumbnailLoad(false, token);
    }

    [Fact]
    public void ScrollingPause_AllowsExplicitPhotoViewerPriorityWork()
    {
        using var directory = new TempDirectory();
        using var client = new HttpClient(new BytesHandler([1, 2, 3]));
        using var loader = new AlbumImageListLoader(new ImageCacheService(directory.Path), client);
        var photo = ScrollingPhoto("priority");
        SetLoaderPhotos(loader, [photo], 1);
        loader.UpdateViewport([photo]);
        loader.AddPriorityPhoto(photo);
        loader.SetListLoadingPaused(true);
        Assert.True(TryTakeVisibleFastWork(loader, 1, out var selected, out _, out var token));
        Assert.Same(photo, selected);
        photo.CompleteFastThumbnailLoad(false, token);
    }

    [Fact]
    public async Task ScrollingPause_SuppressesAlreadyQueuedBitmapAssignmentAndReleasesItsLease()
    {
        using var directory = new TempDirectory();
        using var client = new HttpClient(new BytesHandler([1, 2, 3]));
        using var loader = new AlbumImageListLoader(new ImageCacheService(directory.Path), client);
        var photo = ScrollingPhoto("in-flight");
        SetLoaderPhotos(loader, [photo], 1);
        loader.UpdateViewport([photo]);
        Assert.True(TryTakeVisibleFastWork(loader, 1, out _, out _, out var token));
        var releases = 0;
        var bitmap = (Bitmap)RuntimeHelpers.GetUninitializedObject(typeof(Bitmap));
        using var lease = (AlbumImageBitmapLease)Activator.CreateInstance(typeof(AlbumImageBitmapLease),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [bitmap, (Action)(() => releases++)], null)!;
        var method = typeof(AlbumImageListLoader).GetMethod("SetLoadedImageIfStillVisibleAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var assignment = (Task<bool>)method.Invoke(loader, [photo, lease, false, "Loaded", token, 1L])!;

        loader.SetListLoadingPaused(true);
        Dispatcher.UIThread.RunJobs();
        Assert.False(await assignment);
        Assert.Null(photo.Image);
        Assert.Equal(1, releases);
        photo.CompleteFastThumbnailLoad(false, token);
        loader.SetListLoadingPaused(false);
        Assert.True(TryTakeVisibleFastWork(loader, 1, out _, out _, out var resumedToken));
        photo.CompleteFastThumbnailLoad(false, resumedToken);
    }

    private static AlbumPhotoViewModel ScrollingPhoto(string id)
        => new("album", id, id + ".jpg", "https://example.invalid/" + id, "https://example.invalid/thumb-" + id);
}
