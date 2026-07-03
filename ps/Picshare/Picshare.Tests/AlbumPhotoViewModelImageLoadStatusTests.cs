using Picshare.Services;
using Picshare.ViewModels;
using Avalonia.Media.Imaging;

namespace Picshare.Tests;

public sealed class AlbumPhotoViewModelImageLoadStatusTests
{
    [Fact]
    public void TryBeginMethods_LockWholeItem()
    {
        var photo = CreatePhoto();

        Assert.True(photo.TryBeginFastThumbnailLoad());

        Assert.False(photo.TryBeginOriginalImageLoad());
        Assert.False(photo.TryBeginDetailedThumbnailLoad(out _));
        Assert.False(photo.TryBeginFastThumbnailControlLoad(out _));
        Assert.False(photo.TryBeginDetailedThumbnailControlLoad(out _, out _));
        Assert.True(photo.IsImageLoadBusyAtomic());
    }

    [Fact]
    public void ResetWhileBusy_PreventsStaleCompletionFromRestoringLoadedStatus()
    {
        var photo = CreatePhoto();
        Assert.True(photo.TryBeginFastThumbnailLoad());

        photo.ResetImageLoadStatuses();
        photo.CompleteFastThumbnailLoad(loaded: true);

        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.FastThumbnailStatus);
        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.OriginalImageStatus);
        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.DetailedThumbnailStatus);
        Assert.False(photo.IsImageLoadBusyAtomic());
    }

    [Fact]
    public void ResetWhileBusy_ClearsTokenedCurrentCompletion()
    {
        var photo = CreatePhoto();
        Assert.True(photo.TryBeginFastThumbnailLoad(out var workToken));

        photo.ResetImageLoadStatuses();
        photo.CompleteFastThumbnailLoad(loaded: true, workToken);

        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.FastThumbnailStatus);
        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.OriginalImageStatus);
        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.DetailedThumbnailStatus);
        Assert.False(photo.IsImageLoadBusyAtomic());
    }

    [Fact]
    public void DetailedThumbnailLoad_ClaimsOriginalOnlyWhenNeeded()
    {
        var photo = CreatePhoto();

        Assert.True(photo.TryBeginDetailedThumbnailLoad(out var ownsOriginalLoad));
        Assert.True(ownsOriginalLoad);
        Assert.Equal(AlbumImageItemStatus.Loading, photo.DetailedThumbnailStatus);
        Assert.Equal(AlbumImageItemStatus.Loading, photo.OriginalImageStatus);

        photo.CompleteDetailedThumbnailLoad(loaded: false, originalLoaded: false, ownsOriginalLoad);
        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.DetailedThumbnailStatus);
        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.OriginalImageStatus);

        Assert.True(photo.TryBeginOriginalImageLoad());
        photo.CompleteOriginalImageLoad(loaded: true);
        Assert.True(photo.TryBeginDetailedThumbnailLoad(out ownsOriginalLoad));
        Assert.False(ownsOriginalLoad);

        photo.CompleteDetailedThumbnailLoad(loaded: false, originalLoaded: false, ownsOriginalLoad);
        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.DetailedThumbnailStatus);
        Assert.Equal(AlbumImageItemStatus.Loaded, photo.OriginalImageStatus);
    }

    [Fact]
    public void FastThumbnailControlLoad_RestoresAlreadyLoadedStatusWhenReleased()
    {
        var photo = CreatePhoto();
        Assert.True(photo.TryBeginFastThumbnailLoad());
        photo.CompleteFastThumbnailLoad(loaded: true);

        Assert.True(photo.TryBeginFastThumbnailControlLoad(out var wasLoaded));
        Assert.True(wasLoaded);

        photo.CompleteFastThumbnailLoad(wasLoaded);

        Assert.Equal(AlbumImageItemStatus.Loaded, photo.FastThumbnailStatus);
        Assert.False(photo.IsImageLoadBusyAtomic());
    }

    [Fact]
    public void DetailedThumbnailControlLoad_ReportsAlreadyLoadedStatusForCompletion()
    {
        var photo = CreatePhoto();
        Assert.True(photo.TryBeginDetailedThumbnailLoad(out var ownsOriginalLoad));
        photo.CompleteDetailedThumbnailLoad(loaded: true, originalLoaded: true, ownsOriginalLoad);

        Assert.True(photo.TryBeginDetailedThumbnailControlLoad(
            out ownsOriginalLoad,
            out var detailedThumbnailWasAlreadyLoaded));
        Assert.False(ownsOriginalLoad);
        Assert.True(detailedThumbnailWasAlreadyLoaded);

        photo.CompleteDetailedThumbnailLoad(
            loaded: detailedThumbnailWasAlreadyLoaded,
            originalLoaded: false,
            ownsOriginalLoad);

        Assert.Equal(AlbumImageItemStatus.Loaded, photo.DetailedThumbnailStatus);
        Assert.Equal(AlbumImageItemStatus.Loaded, photo.OriginalImageStatus);
        Assert.False(photo.IsImageLoadBusyAtomic());
    }

    [Fact]
    public void ResetForNewGeneration_ClearsBusyAndLoadedStatuses()
    {
        var photo = CreatePhoto();
        Assert.True(photo.TryBeginDetailedThumbnailLoad(out var ownsOriginalLoad));
        photo.CompleteDetailedThumbnailLoad(loaded: true, originalLoaded: true, ownsOriginalLoad);

        photo.ResetImageLoadStatusesForNewGeneration();

        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.FastThumbnailStatus);
        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.OriginalImageStatus);
        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.DetailedThumbnailStatus);
        Assert.False(photo.IsFullImageLoaded);
        Assert.False(photo.IsImageLoading);
        Assert.False(photo.IsImageLoadBusyAtomic());
    }

    [Fact]
    public void ResetForNewGenerationWhileBusy_PreventsStaleFastCompletionFromRestoringLoadedStatus()
    {
        var photo = CreatePhoto();
        Assert.True(photo.TryBeginFastThumbnailLoad());

        photo.ResetImageLoadStatusesForNewGeneration();
        photo.CompleteFastThumbnailLoad(loaded: true);

        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.FastThumbnailStatus);
        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.OriginalImageStatus);
        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.DetailedThumbnailStatus);
        Assert.False(photo.IsImageLoadBusyAtomic());
    }

    [Fact]
    public void ResetForNewGenerationWhileBusy_PreventsStaleDetailedCompletionFromRestoringLoadedStatus()
    {
        var photo = CreatePhoto();
        Assert.True(photo.TryBeginDetailedThumbnailLoad(out var ownsOriginalLoad));

        photo.ResetImageLoadStatusesForNewGeneration();
        photo.CompleteDetailedThumbnailLoad(loaded: true, originalLoaded: true, ownsOriginalLoad);

        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.FastThumbnailStatus);
        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.OriginalImageStatus);
        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.DetailedThumbnailStatus);
        Assert.False(photo.IsImageLoadBusyAtomic());
    }

    [Fact]
    public void StaleFastCompletionAfterNewGeneration_DoesNotUnlockNewDetailedWork()
    {
        var photo = CreatePhoto();
        Assert.True(photo.TryBeginFastThumbnailLoad(out var oldWorkToken));

        photo.ResetImageLoadStatusesForNewGeneration();
        Assert.True(photo.TryBeginDetailedThumbnailLoad(out var ownsOriginalLoad, out var newWorkToken));

        photo.CompleteFastThumbnailLoad(loaded: true, oldWorkToken);

        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.FastThumbnailStatus);
        Assert.Equal(AlbumImageItemStatus.Loading, photo.OriginalImageStatus);
        Assert.Equal(AlbumImageItemStatus.Loading, photo.DetailedThumbnailStatus);
        Assert.True(photo.IsImageLoadBusyAtomic());

        photo.CompleteDetailedThumbnailLoad(loaded: true, originalLoaded: true, ownsOriginalLoad, newWorkToken);

        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.FastThumbnailStatus);
        Assert.Equal(AlbumImageItemStatus.Loaded, photo.OriginalImageStatus);
        Assert.Equal(AlbumImageItemStatus.Loaded, photo.DetailedThumbnailStatus);
        Assert.False(photo.IsImageLoadBusyAtomic());
    }

    [Fact]
    public void UntokenedStaleFastCompletionAfterNewGeneration_DoesNotUnlockNewDetailedWork()
    {
        var photo = CreatePhoto();
        Assert.True(photo.TryBeginFastThumbnailLoad());

        photo.ResetImageLoadStatusesForNewGeneration();
        Assert.True(photo.TryBeginDetailedThumbnailLoad(out var ownsOriginalLoad, out var newWorkToken));

        photo.CompleteFastThumbnailLoad(loaded: true);

        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.FastThumbnailStatus);
        Assert.Equal(AlbumImageItemStatus.Loading, photo.OriginalImageStatus);
        Assert.Equal(AlbumImageItemStatus.Loading, photo.DetailedThumbnailStatus);
        Assert.True(photo.IsImageLoadBusyAtomic());

        photo.CompleteDetailedThumbnailLoad(loaded: true, originalLoaded: true, ownsOriginalLoad, newWorkToken);

        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.FastThumbnailStatus);
        Assert.Equal(AlbumImageItemStatus.Loaded, photo.OriginalImageStatus);
        Assert.Equal(AlbumImageItemStatus.Loaded, photo.DetailedThumbnailStatus);
        Assert.False(photo.IsImageLoadBusyAtomic());
    }

    [Fact]
    public void StaleFastCompletionAfterNewGeneration_DoesNotUnlockNewFastWork()
    {
        var photo = CreatePhoto();
        Assert.True(photo.TryBeginFastThumbnailLoad(out var oldWorkToken));

        photo.ResetImageLoadStatusesForNewGeneration();
        Assert.True(photo.TryBeginFastThumbnailLoad(out var newWorkToken));

        photo.CompleteFastThumbnailLoad(loaded: true, oldWorkToken);

        Assert.Equal(AlbumImageItemStatus.Loading, photo.FastThumbnailStatus);
        Assert.True(photo.IsImageLoadBusyAtomic());

        photo.CompleteFastThumbnailLoad(loaded: true, newWorkToken);

        Assert.Equal(AlbumImageItemStatus.Loaded, photo.FastThumbnailStatus);
        Assert.False(photo.IsImageLoadBusyAtomic());
    }

    [Fact]
    public void UntokenedStaleDetailedCompletionAfterNewGeneration_DoesNotUnlockNewFastWork()
    {
        var photo = CreatePhoto();
        Assert.True(photo.TryBeginDetailedThumbnailLoad(out var oldOwnsOriginalLoad));

        photo.ResetImageLoadStatusesForNewGeneration();
        Assert.True(photo.TryBeginFastThumbnailLoad(out var newWorkToken));

        photo.CompleteDetailedThumbnailLoad(loaded: true, originalLoaded: true, oldOwnsOriginalLoad);

        Assert.Equal(AlbumImageItemStatus.Loading, photo.FastThumbnailStatus);
        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.OriginalImageStatus);
        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.DetailedThumbnailStatus);
        Assert.True(photo.IsImageLoadBusyAtomic());

        photo.CompleteFastThumbnailLoad(loaded: true, newWorkToken);

        Assert.Equal(AlbumImageItemStatus.Loaded, photo.FastThumbnailStatus);
        Assert.False(photo.IsImageLoadBusyAtomic());
    }

    [Fact]
    public void UntokenedStaleCompletionAfterHardReset_DoesNotUnlockNewWork()
    {
        var photo = CreatePhoto();
        Assert.True(photo.TryBeginFastThumbnailLoad(out var workToken));
        SetPrivateField(photo, "_untokenedImageWorkToken", workToken);
        photo.CompleteFastThumbnailLoad(loaded: true, workToken);
        photo.ResetImageLoadStatuses();

        Assert.True(photo.TryBeginOriginalImageLoad(out var newWorkToken));

        photo.CompleteFastThumbnailLoad(loaded: false);

        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.FastThumbnailStatus);
        Assert.Equal(AlbumImageItemStatus.Loading, photo.OriginalImageStatus);
        Assert.True(photo.IsImageLoadBusyAtomic());

        photo.CompleteOriginalImageLoad(loaded: true, newWorkToken);

        Assert.Equal(AlbumImageItemStatus.Loaded, photo.OriginalImageStatus);
        Assert.False(photo.IsImageLoadBusyAtomic());
    }

    [Fact]
    public void StaleDetailedCompletionAfterNewGeneration_DoesNotUnlockNewFastWork()
    {
        var photo = CreatePhoto();
        Assert.True(photo.TryBeginDetailedThumbnailLoad(out var oldOwnsOriginalLoad, out var oldWorkToken));

        photo.ResetImageLoadStatusesForNewGeneration();
        Assert.True(photo.TryBeginFastThumbnailLoad(out var newWorkToken));

        photo.CompleteDetailedThumbnailLoad(loaded: true, originalLoaded: true, oldOwnsOriginalLoad, oldWorkToken);

        Assert.Equal(AlbumImageItemStatus.Loading, photo.FastThumbnailStatus);
        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.OriginalImageStatus);
        Assert.Equal(AlbumImageItemStatus.Unloaded, photo.DetailedThumbnailStatus);
        Assert.True(photo.IsImageLoadBusyAtomic());

        photo.CompleteFastThumbnailLoad(loaded: true, newWorkToken);

        Assert.Equal(AlbumImageItemStatus.Loaded, photo.FastThumbnailStatus);
        Assert.False(photo.IsImageLoadBusyAtomic());
    }

    [Fact]
    public void IsImageWorkCurrent_ReturnsFalseAfterNewGenerationReset()
    {
        var photo = CreatePhoto();
        Assert.True(photo.TryBeginFastThumbnailLoad(out var oldWorkToken));

        photo.ResetImageLoadStatusesForNewGeneration();

        Assert.False(photo.IsImageWorkCurrent(oldWorkToken));
    }

    [Fact]
    public void IsImageWorkCurrent_ReturnsFalseWhileResetIsPending()
    {
        var photo = CreatePhoto();
        Assert.True(photo.TryBeginFastThumbnailLoad(out var workToken));

        photo.ResetImageLoadStatuses();

        Assert.False(photo.IsImageWorkCurrent(workToken));
    }

    [Fact]
    public void IsImageWorkCurrent_ReturnsFalseAfterNewWorkClaim()
    {
        var photo = CreatePhoto();
        Assert.True(photo.TryBeginFastThumbnailLoad(out var oldWorkToken));

        photo.ResetImageLoadStatusesForNewGeneration();
        Assert.True(photo.TryBeginDetailedThumbnailLoad(out _, out var newWorkToken));

        Assert.False(photo.IsImageWorkCurrent(oldWorkToken));
        Assert.True(photo.IsImageWorkCurrent(newWorkToken));
    }

    [Fact]
    public void StopViewportLoad_DefersImageRelease()
    {
        var photo = CreatePhoto();
        var bitmap = CreateBitmap();
        Assert.True(photo.TryBeginFastThumbnailLoad(out var workToken));
        SetPrivateField(photo, "_image", bitmap);
        photo.Status = "Loading detailed image";
        photo.CompleteFastThumbnailLoad(loaded: true, workToken);

        photo.StopViewportLoad();

        Assert.Same(bitmap, photo.Image);
        Assert.Equal(AlbumImageItemStatus.Loaded, photo.FastThumbnailStatus);
        Assert.Equal("Loading detailed image", photo.Status);
        Assert.NotNull(GetPrivateField<CancellationTokenSource?>(photo, "_imageReleaseCancellation"));

        photo.KeepCachedImage();
        SetPrivateField<Bitmap?>(photo, "_image", null);
    }

    [Fact]
    public void KeepCachedImage_CancelsDeferredImageRelease()
    {
        var photo = CreatePhoto();
        var bitmap = CreateBitmap();
        SetPrivateField(photo, "_image", bitmap);
        photo.Status = "Loading detailed image";

        photo.ScheduleDeferredImageRelease();
        Assert.NotNull(GetPrivateField<CancellationTokenSource?>(photo, "_imageReleaseCancellation"));

        photo.KeepCachedImage();

        Assert.Null(GetPrivateField<CancellationTokenSource?>(photo, "_imageReleaseCancellation"));
        Assert.Same(bitmap, photo.Image);

        SetPrivateField<Bitmap?>(photo, "_image", null);
    }

    private static AlbumPhotoViewModel CreatePhoto()
    {
        return new AlbumPhotoViewModel(
            "album",
            "photo-1",
            "photo-1.jpg",
            "https://example.invalid/photo-1.jpg",
            "https://example.invalid/photo-1-thumb.jpg");
    }

    private static Bitmap CreateBitmap()
    {
        return (Bitmap)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Bitmap));
    }

    private static void SetPrivateField<T>(AlbumPhotoViewModel photo, string fieldName, T value)
    {
        var field = typeof(AlbumPhotoViewModel).GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(photo, value);
    }

    private static T GetPrivateField<T>(AlbumPhotoViewModel photo, string fieldName)
    {
        var field = typeof(AlbumPhotoViewModel).GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T)field.GetValue(photo)!;
    }
}
