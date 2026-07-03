using System.Net;
using Picshare.Services;
using Picshare.ViewModels;

namespace Picshare.Tests;

public sealed class AlbumImageListLoaderWarmupTests
{
    [Fact]
    public void VisibleFastWork_DoesNotClaimPriorityOnlyPhoto()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path);
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));
        using var loader = new AlbumImageListLoader(imageCache, httpClient);
        var photo = new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");

        SetPrivateField(loader, "_photos", new[] { photo });
        SetPrivateField(loader, "_indexByPhoto", new Dictionary<AlbumPhotoViewModel, int> { [photo] = 0 });
        SetPrivateField(loader, "_viewportIndices", new HashSet<int>());
        SetPrivateField(loader, "_viewportSnapshotIndexCounts", new Dictionary<int, int>());
        SetPrivateField(loader, "_viewportLoadedIndexCounts", new Dictionary<int, int>());
        SetPrivateField(loader, "_orderedViewportIndices", new List<int>());
        SetPrivateField(loader, "_priorityIndices", new HashSet<int> { 0 });
        SetPrivateField(loader, "_orderedPriorityIndices", new List<int> { 0 });
        SetPrivateField(loader, "_generation", 1L);

        var method = typeof(AlbumImageListLoader).GetMethod(
            "TryTakeVisibleFastWork",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        object?[] arguments = { 1L, null, false, 0 };

        var didTakeWork = (bool)method.Invoke(loader, arguments)!;

        Assert.False(didTakeWork);
        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.FastThumbnailStatus);
        Assert.False(photo.IsImageLoadBusyAtomic());
    }

    [Fact]
    public void VisibleFastWork_DoesNotClaimPriorityPhotoFromStaleListOrder()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path);
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));
        using var loader = new AlbumImageListLoader(imageCache, httpClient);
        var photo = new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");

        SetPrivateField(loader, "_photos", new[] { photo });
        SetPrivateField(loader, "_indexByPhoto", new Dictionary<AlbumPhotoViewModel, int> { [photo] = 0 });
        SetPrivateField(loader, "_viewportIndices", new HashSet<int>());
        SetPrivateField(loader, "_viewportSnapshotIndexCounts", new Dictionary<int, int>());
        SetPrivateField(loader, "_viewportLoadedIndexCounts", new Dictionary<int, int>());
        SetPrivateField(loader, "_orderedViewportIndices", new List<int> { 0 });
        SetPrivateField(loader, "_priorityIndices", new HashSet<int> { 0 });
        SetPrivateField(loader, "_orderedPriorityIndices", new List<int> { 0 });
        SetPrivateField(loader, "_generation", 1L);

        var method = typeof(AlbumImageListLoader).GetMethod(
            "TryTakeVisibleFastWork",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        object?[] arguments = { 1L, null, false, 0 };

        var didTakeWork = (bool)method.Invoke(loader, arguments)!;

        Assert.False(didTakeWork);
        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.FastThumbnailStatus);
        Assert.False(photo.IsImageLoadBusyAtomic());
    }

    [Fact]
    public async Task FastThumbnailWarmup_DoesNotMarkLoadedWhenLazyCacheCannotStoreItem()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 1,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 0,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 0)
        };
        var handler = new BytesHandler(new byte[] { 1, 2, 3, 4 });
        using var httpClient = new HttpClient(handler);
        using var loader = new AlbumImageListLoader(imageCache, httpClient);
        var photo = new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");

        Assert.True(photo.TryBeginFastThumbnailLoad(out var workToken));
        await InvokeExecuteThumbnailWorkAsync(
            loader,
            photo,
            "FastThumbnail",
            isVisible: false,
            ownsOriginalLoad: false,
            fastThumbnailWasAlreadyLoaded: false,
            detailedThumbnailWasAlreadyLoaded: false,
            workToken,
            generation: 0);

        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.FastThumbnailStatus);
    }

    [Fact]
    public async Task Workers_DoNotWarmPhotosWithoutViewportOrPrioritySeed()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path);
        var handler = new BytesHandler(new byte[] { 1, 2, 3, 4 });
        using var httpClient = new HttpClient(handler);
        using var loader = new AlbumImageListLoader(imageCache, httpClient);
        var photo = new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");

        loader.SetPhotos([photo], maximumParallelism: 1);
        await Task.Delay(250);
        await loader.ClearAndWaitAsync();

        Assert.Equal(0, handler.RequestCount);
        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.FastThumbnailStatus);
        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.DetailedThumbnailStatus);
        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.OriginalImageStatus);
    }

    [Fact]
    public void RemoveViewportPhoto_RemovesPhotoFromSnapshotViewport()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path);
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));
        using var loader = new AlbumImageListLoader(imageCache, httpClient);
        var photo = new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");

        loader.SetPhotos([photo], maximumParallelism: 1);
        loader.UpdateViewport([photo]);

        loader.RemoveViewportPhoto(photo);

        Assert.Empty(loader.GetListViewportPhotosSnapshot());
    }

    [Fact]
    public void RemoveViewportPhoto_DoesNotRemoveSnapshotVisibilityWhenLoadedControlIsRemoved()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path);
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));
        using var loader = new AlbumImageListLoader(imageCache, httpClient);
        var photo = new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");

        loader.SetPhotos([photo], maximumParallelism: 1);
        loader.UpdateViewport([photo]);
        loader.AddViewportPhoto(photo);

        loader.RemoveViewportPhoto(photo);

        Assert.Same(photo, Assert.Single(loader.GetListViewportPhotosSnapshot()));
    }

    [Fact]
    public void RemoveViewportPhoto_DoesNotResurrectLoadedRegistrationAfterViewportUpdate()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path);
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));
        using var loader = new AlbumImageListLoader(imageCache, httpClient);
        var photo = new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");

        loader.SetPhotos([photo], maximumParallelism: 1);
        loader.AddViewportPhoto(photo);

        loader.RemoveViewportPhoto(photo);
        loader.UpdateViewport([]);

        Assert.Empty(loader.GetListViewportPhotosSnapshot());
    }

    [Fact]
    public void RemoveLoadedViewportPhoto_DoesNotRemoveSnapshotVisibilityWithoutLoadedRegistration()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path);
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));
        using var loader = new AlbumImageListLoader(imageCache, httpClient);
        var photo = new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");

        loader.SetPhotos([photo], maximumParallelism: 1);
        loader.UpdateViewport([photo]);

        loader.RemoveLoadedViewportPhoto(photo);

        Assert.Same(photo, Assert.Single(loader.GetListViewportPhotosSnapshot()));
    }

    [Fact]
    public void RemoveLoadedViewportPhoto_RemovesLoadedRegistration()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path);
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));
        using var loader = new AlbumImageListLoader(imageCache, httpClient);
        var photo = new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");

        loader.SetPhotos([photo], maximumParallelism: 1);
        loader.AddViewportPhoto(photo);

        loader.RemoveLoadedViewportPhoto(photo);

        Assert.Empty(loader.GetListViewportPhotosSnapshot());
    }

    [Fact]
    public void RemoveLoadedViewportPhoto_RemovesOriginalDuplicateExpansionAfterDuplicateStateChanges()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path);
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));
        using var loader = new AlbumImageListLoader(imageCache, httpClient);
        var mainPhoto = new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");
        var duplicatePhoto = new AlbumPhotoViewModel(
            "album",
            "photo-2",
            "photo-2.jpg",
            "https://example.invalid/photo-2.jpg",
            "https://example.invalid/photo-2-thumb.jpg")
        {
            DuplicateStackPhoto = mainPhoto
        };

        loader.SetPhotos([mainPhoto, duplicatePhoto], maximumParallelism: 1);
        loader.AddViewportPhoto(duplicatePhoto);

        duplicatePhoto.DuplicateStackPhoto = null;
        var removedPhotos = loader.RemoveLoadedViewportPhoto(duplicatePhoto);

        Assert.Empty(loader.GetListViewportPhotosSnapshot());
        Assert.Equal(2, removedPhotos.Count);
        Assert.Contains(mainPhoto, removedPhotos);
        Assert.Contains(duplicatePhoto, removedPhotos);
    }

    [Fact]
    public void RemoveLoadedViewportPhoto_DoesNotRemoveNewDuplicateStackRegistrationAfterDuplicateStateChanges()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path);
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));
        using var loader = new AlbumImageListLoader(imageCache, httpClient);
        var oldStackPhoto = new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");
        var duplicatePhoto = new AlbumPhotoViewModel(
            "album",
            "photo-2",
            "photo-2.jpg",
            "https://example.invalid/photo-2.jpg",
            "https://example.invalid/photo-2-thumb.jpg")
        {
            DuplicateStackPhoto = oldStackPhoto
        };
        var newStackPhoto = new AlbumPhotoViewModel(
            "album",
            "photo-3",
            "photo-3.jpg",
            "https://example.invalid/photo-3.jpg",
            "https://example.invalid/photo-3-thumb.jpg");

        loader.SetPhotos([oldStackPhoto, duplicatePhoto, newStackPhoto], maximumParallelism: 1);
        loader.AddViewportPhoto(duplicatePhoto);
        loader.AddViewportPhoto(newStackPhoto);

        duplicatePhoto.DuplicateStackPhoto = newStackPhoto;
        loader.RemoveLoadedViewportPhoto(duplicatePhoto);

        Assert.Same(newStackPhoto, Assert.Single(loader.GetListViewportPhotosSnapshot()));
    }

    [Fact]
    public void RemoveViewportPhoto_DoesNotRemoveNewDuplicateStackLoadedRegistrationAfterDuplicateStateChanges()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path);
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));
        using var loader = new AlbumImageListLoader(imageCache, httpClient);
        var oldStackPhoto = new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");
        var duplicatePhoto = new AlbumPhotoViewModel(
            "album",
            "photo-2",
            "photo-2.jpg",
            "https://example.invalid/photo-2.jpg",
            "https://example.invalid/photo-2-thumb.jpg")
        {
            DuplicateStackPhoto = oldStackPhoto
        };
        var newStackPhoto = new AlbumPhotoViewModel(
            "album",
            "photo-3",
            "photo-3.jpg",
            "https://example.invalid/photo-3.jpg",
            "https://example.invalid/photo-3-thumb.jpg");

        loader.SetPhotos([oldStackPhoto, duplicatePhoto, newStackPhoto], maximumParallelism: 1);
        loader.AddViewportPhoto(duplicatePhoto);
        loader.AddViewportPhoto(newStackPhoto);

        duplicatePhoto.DuplicateStackPhoto = newStackPhoto;
        loader.RemoveViewportPhoto(duplicatePhoto);

        Assert.Same(newStackPhoto, Assert.Single(loader.GetListViewportPhotosSnapshot()));
    }

    [Fact]
    public void UpdateViewport_PreservesLoadedRegistrationWhenSnapshotScanMissesControl()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path);
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));
        using var loader = new AlbumImageListLoader(imageCache, httpClient);
        var photo = new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");

        loader.SetPhotos([photo], maximumParallelism: 1);
        loader.AddViewportPhoto(photo);

        loader.UpdateViewport([]);

        Assert.Same(photo, Assert.Single(loader.GetListViewportPhotosSnapshot()));
    }

    [Fact]
    public void ClearViewport_RemovesLoadedRegistrations()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path);
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));
        using var loader = new AlbumImageListLoader(imageCache, httpClient);
        var photo = new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");

        loader.SetPhotos([photo], maximumParallelism: 1);
        loader.AddViewportPhoto(photo);

        loader.ClearViewport();

        Assert.Empty(loader.GetListViewportPhotosSnapshot());
    }

    [Fact]
    public void RestartPreservingState_KeepsLoadedRegistrationRemovable()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path);
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));
        using var loader = new AlbumImageListLoader(imageCache, httpClient);
        var photo = new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");

        loader.SetPhotos([photo], maximumParallelism: 1);
        loader.AddViewportPhoto(photo);

        loader.RestartPreservingState(maximumParallelism: 1);
        loader.RemoveLoadedViewportPhoto(photo);

        Assert.Empty(loader.GetListViewportPhotosSnapshot());
    }

    [Fact]
    public void RestartPreservingState_DoesNotConvertSnapshotVisibilityToLoadedRegistration()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path);
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));
        using var loader = new AlbumImageListLoader(imageCache, httpClient);
        var photo = new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");

        loader.SetPhotos([photo], maximumParallelism: 1);
        loader.UpdateViewport([photo]);

        loader.RestartPreservingState(maximumParallelism: 1);
        loader.RemoveLoadedViewportPhoto(photo);

        Assert.Same(photo, Assert.Single(loader.GetListViewportPhotosSnapshot()));
    }

    [Fact]
    public void GetListViewportIndicesSnapshot_DoesNotDoubleCountSnapshotAndLoadedControl()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path);
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));
        using var loader = new AlbumImageListLoader(imageCache, httpClient);
        var photo = new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");

        loader.SetPhotos([photo], maximumParallelism: 1);
        loader.UpdateViewport([photo]);
        loader.AddViewportPhoto(photo);

        Assert.Equal([0], loader.GetListViewportIndicesSnapshot());
    }

    [Fact]
    public async Task DetailedThumbnailWarmupFailure_IsSkippedAfterPermanentFailure()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 1024 * 1024,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 0,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 0)
        };
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));
        using var loader = new AlbumImageListLoader(imageCache, httpClient);
        var photo = new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");
        SetLoaderPhotos(loader, [photo], generation: 1);
        loader.UpdateViewport([photo]);
        Assert.True(photo.TryBeginDetailedThumbnailLoad(out var ownsOriginalLoad, out var workToken));

        await InvokeExecuteThumbnailWorkAsync(
            loader,
            photo,
            "DetailedThumbnail",
            isVisible: false,
            ownsOriginalLoad,
            fastThumbnailWasAlreadyLoaded: false,
            detailedThumbnailWasAlreadyLoaded: false,
            workToken,
            generation: 1);

        Assert.False(TryTakeNearestWork(loader, "DetailedThumbnail", generation: 1, out _, out _, out _));
        Assert.False(photo.IsImageLoadBusyAtomic());
    }

    [Fact]
    public async Task FastThumbnailWarmupFailure_IsSkippedAfterPermanentFailure()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 1024 * 1024,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 0,
                FastThumbnailDiskBytes: 0,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 0)
        };
        using var httpClient = new HttpClient(new FileNotFoundHandler());
        using var loader = new AlbumImageListLoader(imageCache, httpClient);
        var photo = new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");
        SetLoaderPhotos(loader, [photo], generation: 1);
        loader.UpdateViewport([photo]);
        Assert.True(photo.TryBeginFastThumbnailLoad(out var workToken));

        await InvokeExecuteThumbnailWorkAsync(
            loader,
            photo,
            "FastThumbnail",
            isVisible: false,
            ownsOriginalLoad: false,
            fastThumbnailWasAlreadyLoaded: false,
            detailedThumbnailWasAlreadyLoaded: false,
            workToken,
            generation: 1);

        Assert.False(TryTakeNearestWork(loader, "FastThumbnail", generation: 1, out _, out _, out _));
        Assert.False(photo.IsImageLoadBusyAtomic());
    }

    [Fact]
    public void VisibleFastThumbnailFailure_AllowsDetailedThumbnailFallback()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path);
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));
        using var loader = new AlbumImageListLoader(imageCache, httpClient);
        var photo = new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");
        SetLoaderPhotos(loader, [photo], generation: 1);
        loader.UpdateViewport([photo]);
        MarkVisibleFailureSkipped(loader, photo, "FastThumbnail");

        Assert.True(TryTakeVisibleThumbnailWork(
            loader,
            generation: 1,
            preferDetailed: true,
            out var fallbackPhoto,
            out var fallbackWork,
            out var ownsOriginalLoad,
            out _,
            out _,
            out var fallbackWorkToken));
        Assert.Same(photo, fallbackPhoto);
        Assert.Equal("DetailedThumbnail", fallbackWork);

        photo.CompleteDetailedThumbnailLoad(loaded: false, originalLoaded: false, ownsOriginalLoad, fallbackWorkToken);
    }

    [Fact]
    public void VisibleFastThumbnailFailure_SuppressesFastWarmupRetry()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path);
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));
        using var loader = new AlbumImageListLoader(imageCache, httpClient);
        var photo = new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");
        SetLoaderPhotos(loader, [photo], generation: 1);
        loader.UpdateViewport([photo]);
        MarkVisibleFailureSkipped(loader, photo, "FastThumbnail");

        Assert.False(TryTakeNearestWork(loader, "FastThumbnail", generation: 1, out _, out _, out _));
        Assert.False(photo.IsImageLoadBusyAtomic());
    }

    [Fact]
    public void VisibleFailureSkip_IsNotMarkedAfterPhotoLeavesViewport()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path);
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));
        using var loader = new AlbumImageListLoader(imageCache, httpClient);
        var photo = new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");
        SetLoaderPhotos(loader, [photo], generation: 1);
        loader.UpdateViewport([photo]);
        Assert.True(photo.TryBeginFastThumbnailControlLoad(out _, out var workToken));

        loader.RemoveViewportPhoto(photo);
        MarkVisibleFailureSkippedIfCurrentVisible(loader, photo, "FastThumbnail", workToken);

        Assert.Equal(0, GetFailedVisibleKeyCount(loader));
        photo.CompleteFastThumbnailLoad(loaded: false, workToken);
    }

    [Fact]
    public void VisibleFailureSkip_IsNotMarkedAfterWorkReset()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path);
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));
        using var loader = new AlbumImageListLoader(imageCache, httpClient);
        var photo = new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");
        SetLoaderPhotos(loader, [photo], generation: 1);
        loader.UpdateViewport([photo]);
        Assert.True(photo.TryBeginFastThumbnailControlLoad(out _, out var workToken));

        photo.ResetImageLoadStatuses();
        MarkVisibleFailureSkippedIfCurrentVisible(loader, photo, "FastThumbnail", workToken);

        Assert.Equal(0, GetFailedVisibleKeyCount(loader));
        photo.CompleteFastThumbnailLoad(loaded: false, workToken);
    }

    [Fact]
    public void VisibleFailureSkip_IsMarkedForCurrentVisibleWork()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path);
        using var httpClient = new HttpClient(new BytesHandler([1, 2, 3, 4]));
        using var loader = new AlbumImageListLoader(imageCache, httpClient);
        var photo = new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");
        SetLoaderPhotos(loader, [photo], generation: 1);
        loader.UpdateViewport([photo]);
        Assert.True(photo.TryBeginFastThumbnailControlLoad(out _, out var workToken));

        MarkVisibleFailureSkippedIfCurrentVisible(loader, photo, "FastThumbnail", workToken);

        Assert.Equal(1, GetFailedVisibleKeyCount(loader));
        photo.CompleteFastThumbnailLoad(loaded: false, workToken);
    }

    [Fact]
    public async Task OriginalWarmupFailure_IsSkippedAfterPermanentFailure()
    {
        using var tempDirectory = new TempDirectory();
        var imageCache = new ImageCacheService(tempDirectory.Path)
        {
            Limits = new AlbumImageCacheLimits(
                FastThumbnailMemoryBytes: 0,
                DetailedThumbnailMemoryBytes: 0,
                OriginalImageMemoryBytes: 1024 * 1024,
                FastThumbnailDiskBytes: 0,
                DetailedThumbnailDiskBytes: 0,
                OriginalImageDiskBytes: 0)
        };
        using var httpClient = new HttpClient(new FileNotFoundHandler());
        using var loader = new AlbumImageListLoader(imageCache, httpClient);
        var photo = new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");
        SetLoaderPhotos(loader, [photo], generation: 1);
        loader.UpdateViewport([photo]);
        Assert.True(photo.TryBeginOriginalImageLoad(out var workToken));

        await InvokeExecuteOriginalWarmupAsync(loader, photo, workToken, generation: 1);

        Assert.False(TryTakeNearestWork(loader, "OriginalImage", generation: 1, out _, out _, out _));
        Assert.False(photo.IsImageLoadBusyAtomic());
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            cancellation.Token.ThrowIfCancellationRequested();
            await Task.Delay(20, cancellation.Token);
        }
    }

    private static void SetPrivateField<T>(AlbumImageListLoader loader, string fieldName, T value)
    {
        var field = typeof(AlbumImageListLoader).GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(loader, value);
    }

    private static void SetLoaderPhotos(AlbumImageListLoader loader, IReadOnlyList<AlbumPhotoViewModel> photos, long generation)
    {
        SetPrivateField(loader, "_photos", photos.ToArray());
        SetPrivateField(
            loader,
            "_indexByPhoto",
            photos.Select((photo, index) => new { photo, index }).ToDictionary(item => item.photo, item => item.index));
        SetPrivateField(loader, "_generation", generation);
    }

    private static async Task InvokeExecuteThumbnailWorkAsync(
        AlbumImageListLoader loader,
        AlbumPhotoViewModel photo,
        string workName,
        bool isVisible,
        bool ownsOriginalLoad,
        bool fastThumbnailWasAlreadyLoaded,
        bool detailedThumbnailWasAlreadyLoaded,
        int workToken,
        long generation)
    {
        var method = typeof(AlbumImageListLoader).GetMethod(
            "ExecuteThumbnailWorkAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        var workType = typeof(AlbumImageListLoader).GetNestedType(
            "AlbumImageWork",
            System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(workType);
        var work = Enum.Parse(workType, workName);

        var task = (Task)method.Invoke(
            loader,
            [
                photo,
                work,
                isVisible,
                ownsOriginalLoad,
                fastThumbnailWasAlreadyLoaded,
                detailedThumbnailWasAlreadyLoaded,
                workToken,
                generation,
                CancellationToken.None
        ])!;
        await task;
    }

    private static async Task InvokeExecuteOriginalWarmupAsync(
        AlbumImageListLoader loader,
        AlbumPhotoViewModel photo,
        int workToken,
        long generation)
    {
        var method = typeof(AlbumImageListLoader).GetMethod(
            "ExecuteOriginalWarmupAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task)method.Invoke(
            loader,
            [
                photo,
                workToken,
                generation,
                CancellationToken.None
            ])!;
        await task;
    }

    private static bool TryTakeNearestWork(
        AlbumImageListLoader loader,
        string workName,
        long generation,
        out AlbumPhotoViewModel? photo,
        out bool ownsOriginalLoad,
        out int workToken)
    {
        var method = typeof(AlbumImageListLoader).GetMethod(
            "TryTakeNearestWork",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        var workType = typeof(AlbumImageListLoader).GetNestedType(
            "AlbumImageWork",
            System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(workType);
        var work = Enum.Parse(workType, workName);
        object?[] arguments = { generation, work, null, false, 0 };

        var didTakeWork = (bool)method.Invoke(loader, arguments)!;

        photo = (AlbumPhotoViewModel?)arguments[2];
        ownsOriginalLoad = (bool)arguments[3]!;
        workToken = (int)arguments[4]!;
        return didTakeWork;
    }

    private static bool TryTakeVisibleThumbnailWork(
        AlbumImageListLoader loader,
        long generation,
        bool preferDetailed,
        out AlbumPhotoViewModel? photo,
        out string workName,
        out bool ownsOriginalLoad,
        out bool fastThumbnailWasAlreadyLoaded,
        out bool detailedThumbnailWasAlreadyLoaded,
        out int workToken)
    {
        var method = typeof(AlbumImageListLoader).GetMethod(
            "TryTakeVisibleThumbnailWork",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        object?[] arguments = { generation, preferDetailed, null, null, false, false, false, 0 };

        var didTakeWork = (bool)method.Invoke(loader, arguments)!;

        photo = (AlbumPhotoViewModel?)arguments[2];
        workName = arguments[3]?.ToString() ?? "";
        ownsOriginalLoad = (bool)arguments[4]!;
        fastThumbnailWasAlreadyLoaded = (bool)arguments[5]!;
        detailedThumbnailWasAlreadyLoaded = (bool)arguments[6]!;
        workToken = (int)arguments[7]!;
        return didTakeWork;
    }

    private static void MarkVisibleFailureSkipped(AlbumImageListLoader loader, AlbumPhotoViewModel photo, string workName)
    {
        var method = typeof(AlbumImageListLoader).GetMethod(
            "MarkVisibleFailureSkipped",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var workType = typeof(AlbumImageListLoader).GetNestedType(
            "AlbumImageWork",
            System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.NotNull(workType);
        var work = Enum.Parse(workType, workName);

        method.Invoke(loader, [photo, work]);
    }

    private static void MarkVisibleFailureSkippedIfCurrentVisible(AlbumImageListLoader loader, AlbumPhotoViewModel photo, string workName, int workToken)
    {
        var method = typeof(AlbumImageListLoader).GetMethod(
            "MarkVisibleFailureSkippedIfCurrentVisible",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var workType = typeof(AlbumImageListLoader).GetNestedType(
            "AlbumImageWork",
            System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.NotNull(workType);
        var work = Enum.Parse(workType, workName);

        method.Invoke(loader, [photo, work, workToken]);
    }

    private static int GetFailedVisibleKeyCount(AlbumImageListLoader loader)
    {
        var field = typeof(AlbumImageListLoader).GetField(
            "_failedVisibleKeys",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        var countProperty = field.GetValue(loader)?.GetType().GetProperty("Count");
        Assert.NotNull(countProperty);
        return Assert.IsType<int>(countProperty.GetValue(field.GetValue(loader)));
    }

    private sealed class BytesHandler(byte[] bytes) : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            });
        }
    }

    private sealed class FileNotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new FileNotFoundException("The source image is not available.");
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"picshare-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
