using Avalonia.Threading;
using Picshare.ViewModels;

namespace Picshare.Services;

public sealed class AlbumImageListLoader : IDisposable
{
    private readonly ImageCacheService _imageCache;
    private readonly HttpClient _httpClient;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _updates = new(0);
    private IReadOnlyList<AlbumPhotoViewModel> _photos = [];
    private Dictionary<AlbumPhotoViewModel, int> _indexByPhoto = new();
    private HashSet<int> _viewportIndices = new();
    private CancellationTokenSource? _lifetime;
    private readonly List<Task> _workerTasks = new();
    private int _maximumParallelism = LocalUserSettings.DefaultMaximumParallelism;
    private bool _disposed;

    public AlbumImageListLoader(ImageCacheService imageCache, HttpClient httpClient)
    {
        _imageCache = imageCache;
        _httpClient = httpClient;
    }

    public void SetPhotos(IReadOnlyList<AlbumPhotoViewModel> photos, int maximumParallelism)
    {
        StopWorkers();
        _maximumParallelism = Math.Clamp(maximumParallelism, 1, 64);
        lock (_sync)
        {
            _photos = photos.ToArray();
            _indexByPhoto = _photos
                .Select((photo, index) => new { photo, index })
                .ToDictionary(item => item.photo, item => item.index);
            _viewportIndices = new HashSet<int>();
        }

        StartWorkers();
        SignalWorkers();
    }

    public void UpdateSettings(int maximumParallelism)
    {
        maximumParallelism = Math.Clamp(maximumParallelism, 1, 64);
        if (maximumParallelism == _maximumParallelism)
        {
            SignalWorkers();
            return;
        }

        IReadOnlyList<AlbumPhotoViewModel> viewportPhotos;
        lock (_sync)
        {
            viewportPhotos = _viewportIndices
                .Order()
                .Select(index => index >= 0 && index < _photos.Count ? _photos[index] : null)
                .Where(photo => photo is not null)
                .Cast<AlbumPhotoViewModel>()
                .ToList();
        }

        SetPhotos(_photos, maximumParallelism);
        UpdateViewport(viewportPhotos);
    }

    public void UpdateViewport(IReadOnlyList<AlbumPhotoViewModel> visiblePhotos)
    {
        lock (_sync)
        {
            _viewportIndices = visiblePhotos
                .SelectMany(photo => photo.DuplicateStackPhoto is not null && !ReferenceEquals(photo.DuplicateStackPhoto, photo)
                    ? new[] { photo, photo.DuplicateStackPhoto }
                    : new[] { photo })
                .Select(photo => _indexByPhoto.TryGetValue(photo, out var index) ? index : -1)
                .Where(index => index >= 0)
                .ToHashSet();
        }

        SignalWorkers();
    }

    public void AddViewportPhoto(AlbumPhotoViewModel photo)
    {
        lock (_sync)
        {
            AddViewportPhotoNoLock(photo);
            if (photo.DuplicateStackPhoto is not null && !ReferenceEquals(photo.DuplicateStackPhoto, photo))
            {
                AddViewportPhotoNoLock(photo.DuplicateStackPhoto);
            }
        }

        SignalWorkers();
    }

    public void RemoveViewportPhoto(AlbumPhotoViewModel photo)
    {
        lock (_sync)
        {
            if (_indexByPhoto.TryGetValue(photo, out var index))
            {
                _viewportIndices.Remove(index);
            }

            if (photo.DuplicateStackPhoto is not null &&
                !ReferenceEquals(photo.DuplicateStackPhoto, photo) &&
                _indexByPhoto.TryGetValue(photo.DuplicateStackPhoto, out var duplicateIndex))
            {
                _viewportIndices.Remove(duplicateIndex);
            }
        }

        SignalWorkers();
    }

    public void Clear()
    {
        StopWorkers();
        lock (_sync)
        {
            _photos = [];
            _indexByPhoto = new Dictionary<AlbumPhotoViewModel, int>();
            _viewportIndices = new HashSet<int>();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Clear();
        _updates.Dispose();
    }

    private void StartWorkers()
    {
        if (_disposed)
        {
            return;
        }

        var lifetime = new CancellationTokenSource();
        _lifetime = lifetime;
        var heavyWorkerCount = Math.Max(1, _maximumParallelism * 3 / 4);
        var normalWorkerCount = Math.Max(0, _maximumParallelism - heavyWorkerCount - 2);
        var lightWorkerCount = Math.Min(1, _maximumParallelism - 1);
        var emergencyWorkerCount = Math.Min(1, Math.Max(0, _maximumParallelism - 2));

        for (var index = 0; index < emergencyWorkerCount; index++)
        {
            _workerTasks.Add(Task.Run(() => RunWorkerAsync(AlbumImageWorkerKind.Emergency, lifetime.Token)));
        }

        for (var index = 0; index < heavyWorkerCount; index++)
        {
            _workerTasks.Add(Task.Run(() => RunWorkerAsync(AlbumImageWorkerKind.Heavy, lifetime.Token)));
        }

        for (var index = 0; index < normalWorkerCount; index++)
        {
            _workerTasks.Add(Task.Run(() => RunWorkerAsync(AlbumImageWorkerKind.Normal, lifetime.Token)));
        }

        for (var index = 0; index < lightWorkerCount; index++)
        {
            _workerTasks.Add(Task.Run(() => RunWorkerAsync(AlbumImageWorkerKind.Light, lifetime.Token)));
        }
    }

    private void StopWorkers()
    {
        var lifetime = _lifetime;
        if (lifetime is null)
        {
            return;
        }

        _lifetime = null;
        lifetime.Cancel();
        _workerTasks.Clear();
        lifetime.Dispose();
    }

    private void SignalWorkers()
    {
        if (_disposed)
        {
            return;
        }

        for (var index = 0; index < Math.Max(1, _maximumParallelism); index++)
        {
            _updates.Release();
        }
    }

    private void AddViewportPhotoNoLock(AlbumPhotoViewModel photo)
    {
        if (_indexByPhoto.TryGetValue(photo, out var index))
        {
            _viewportIndices.Add(index);
        }
    }

    private async Task RunWorkerAsync(AlbumImageWorkerKind kind, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var didWork = await TryRunNextWorkItemAsync(kind, cancellationToken);
            if (didWork)
            {
                continue;
            }

            try
            {
                await _updates.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<bool> TryRunNextWorkItemAsync(AlbumImageWorkerKind kind, CancellationToken cancellationToken)
    {
        return kind switch
        {
            AlbumImageWorkerKind.Emergency => await TryRunEmergencyWorkAsync(cancellationToken),
            AlbumImageWorkerKind.Heavy => await TryRunHeavyWorkAsync(cancellationToken),
            AlbumImageWorkerKind.Normal => await TryRunNormalWorkAsync(cancellationToken),
            AlbumImageWorkerKind.Light => await TryRunLightWorkAsync(cancellationToken),
            _ => false
        };
    }

    private async Task<bool> TryRunEmergencyWorkAsync(CancellationToken cancellationToken)
    {
        if (TryTakeVisibleFastWork(out var visibleFastPhoto))
        {
            await ExecuteThumbnailWorkAsync(visibleFastPhoto, AlbumImageWork.FastThumbnail, isVisible: true, cancellationToken);
            return true;
        }

        return false;
    }

    private async Task<bool> TryRunHeavyWorkAsync(CancellationToken cancellationToken)
    {
        if (TryTakeVisibleThumbnailWork(preferDetailed: true, out var visiblePhoto, out var visibleWork))
        {
            await ExecuteThumbnailWorkAsync(visiblePhoto, visibleWork, isVisible: true, cancellationToken);
            return true;
        }

        if (_imageCache.HasCacheRoom(GetAlbumId(), AlbumImageCacheKind.DetailedThumbnail) &&
            TryTakeNearestWork(AlbumImageWork.DetailedThumbnail, out var detailedPhoto))
        {
            await ExecuteThumbnailWorkAsync(detailedPhoto, AlbumImageWork.DetailedThumbnail, isVisible: false, cancellationToken);
            return true;
        }

        if (_imageCache.HasCacheRoom(GetAlbumId(), AlbumImageCacheKind.FastThumbnail) &&
            TryTakeNearestWork(AlbumImageWork.FastThumbnail, out var fastPhoto))
        {
            await ExecuteThumbnailWorkAsync(fastPhoto, AlbumImageWork.FastThumbnail, isVisible: false, cancellationToken);
            return true;
        }

        if (_imageCache.HasCacheRoom(GetAlbumId(), AlbumImageCacheKind.OriginalImage) &&
            TryTakeNearestWork(AlbumImageWork.OriginalImage, out var originalPhoto))
        {
            await ExecuteOriginalWarmupAsync(originalPhoto, cancellationToken);
            return true;
        }

        return false;
    }

    private async Task<bool> TryRunNormalWorkAsync(CancellationToken cancellationToken)
    {
        if (TryTakeVisibleFastWork(out var visibleFastPhoto))
        {
            await ExecuteThumbnailWorkAsync(visibleFastPhoto, AlbumImageWork.FastThumbnail, isVisible: true, cancellationToken);
            return true;
        }

        if (_imageCache.HasCacheRoom(GetAlbumId(), AlbumImageCacheKind.FastThumbnail) &&
            TryTakeNearestWork(AlbumImageWork.FastThumbnail, out var fastPhoto))
        {
            await ExecuteThumbnailWorkAsync(fastPhoto, AlbumImageWork.FastThumbnail, isVisible: false, cancellationToken);
            return true;
        }

        if (TryTakeVisibleThumbnailWork(preferDetailed: true, out var visiblePhoto, out var visibleWork))
        {
            await ExecuteThumbnailWorkAsync(visiblePhoto, visibleWork, isVisible: true, cancellationToken);
            return true;
        }

        if (_imageCache.HasCacheRoom(GetAlbumId(), AlbumImageCacheKind.DetailedThumbnail) &&
            TryTakeNearestWork(AlbumImageWork.DetailedThumbnail, out var detailedPhoto))
        {
            await ExecuteThumbnailWorkAsync(detailedPhoto, AlbumImageWork.DetailedThumbnail, isVisible: false, cancellationToken);
            return true;
        }

        if (_imageCache.HasCacheRoom(GetAlbumId(), AlbumImageCacheKind.OriginalImage) &&
            TryTakeNearestWork(AlbumImageWork.OriginalImage, out var originalPhoto))
        {
            await ExecuteOriginalWarmupAsync(originalPhoto, cancellationToken);
            return true;
        }

        return false;
    }

    private async Task<bool> TryRunLightWorkAsync(CancellationToken cancellationToken)
    {
        if (TryTakeVisibleFastWork(out var visibleFastPhoto))
        {
            await ExecuteThumbnailWorkAsync(visibleFastPhoto, AlbumImageWork.FastThumbnail, isVisible: true, cancellationToken);
            return true;
        }

        if (_imageCache.HasCacheRoom(GetAlbumId(), AlbumImageCacheKind.FastThumbnail) &&
            TryTakeNearestWork(AlbumImageWork.FastThumbnail, out var fastPhoto))
        {
            await ExecuteThumbnailWorkAsync(fastPhoto, AlbumImageWork.FastThumbnail, isVisible: false, cancellationToken);
            return true;
        }

        return false;
    }

    private bool TryTakeVisibleFastWork(out AlbumPhotoViewModel photo)
    {
        foreach (var candidate in GetViewportPhotos())
        {
            if (candidate.Image is null &&
                candidate.TryBeginFastThumbnailControlLoad())
            {
                photo = candidate;
                return true;
            }
        }

        photo = null!;
        return false;
    }

    private bool TryTakeVisibleThumbnailWork(bool preferDetailed, out AlbumPhotoViewModel photo, out AlbumImageWork work)
    {
        foreach (var candidate in GetViewportPhotos())
        {
            var fastStatus = candidate.FastThumbnailStatus;
            var detailedStatus = candidate.DetailedThumbnailStatus;
            if (candidate.Image is null && fastStatus is AlbumImageItemStatus.Unloaded or AlbumImageItemStatus.Loaded &&
                candidate.TryBeginFastThumbnailControlLoad())
            {
                photo = candidate;
                work = AlbumImageWork.FastThumbnail;
                return true;
            }

            if (preferDetailed &&
                !candidate.IsFullImageLoaded &&
                detailedStatus is AlbumImageItemStatus.Unloaded or AlbumImageItemStatus.Loaded &&
                candidate.TryBeginDetailedThumbnailControlLoad(out _))
            {
                photo = candidate;
                work = AlbumImageWork.DetailedThumbnail;
                return true;
            }
        }

        photo = null!;
        work = AlbumImageWork.FastThumbnail;
        return false;
    }

    private bool TryTakeNearestWork(AlbumImageWork work, out AlbumPhotoViewModel photo)
    {
        foreach (var candidate in GetPhotosByViewportDistance())
        {
            if (work == AlbumImageWork.FastThumbnail &&
                candidate.FastThumbnailStatus == AlbumImageItemStatus.Unloaded &&
                candidate.TryBeginFastThumbnailLoad())
            {
                photo = candidate;
                return true;
            }

            if (work == AlbumImageWork.DetailedThumbnail &&
                candidate.DetailedThumbnailStatus == AlbumImageItemStatus.Unloaded &&
                candidate.TryBeginDetailedThumbnailLoad(out _))
            {
                photo = candidate;
                return true;
            }

            if (work == AlbumImageWork.OriginalImage &&
                candidate.OriginalImageStatus == AlbumImageItemStatus.Unloaded &&
                candidate.TryBeginOriginalImageLoad())
            {
                photo = candidate;
                return true;
            }
        }

        photo = null!;
        return false;
    }

    private async Task ExecuteThumbnailWorkAsync(
        AlbumPhotoViewModel photo,
        AlbumImageWork work,
        bool isVisible,
        CancellationToken cancellationToken)
    {
        var ownsOriginalLoad = false;
        try
        {
            if (isVisible)
            {
                await Dispatcher.UIThread.InvokeAsync(() => photo.IsImageLoading = true);
            }

            if (work == AlbumImageWork.FastThumbnail)
            {
                if (isVisible && await TryLoadCachedDetailedThumbnailForFastWorkAsync(photo, cancellationToken))
                {
                    photo.CompleteFastThumbnailLoad(loaded: false);
                    return;
                }

                var bitmap = await _imageCache.LoadFastThumbnailBitmapAsync(
                    photo.AlbumId,
                    photo.PhotoId,
                    photo.ThumbnailDownloadUrl,
                    _httpClient,
                    isVisible ? AlbumImageCacheReadMode.Eager : AlbumImageCacheReadMode.Lazy,
                    cancellationToken);
                photo.CompleteFastThumbnailLoad(loaded: true);
                if (isVisible)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        photo.SetLoadedImage(bitmap, isDetailedThumbnail: false, "Loading detailed image"));
                }
                else
                {
                    bitmap.Dispose();
                }

                return;
            }

            ownsOriginalLoad = photo.OriginalImageStatus == AlbumImageItemStatus.Loading;
            var detailedBitmap = await _imageCache.LoadDetailedThumbnailBitmapAsync(
                photo.AlbumId,
                photo.PhotoId,
                $"{photo.PhotoId}-full{photo.FileExtension}",
                photo.DownloadUrl,
                _httpClient,
                isVisible ? AlbumImageCacheReadMode.Eager : AlbumImageCacheReadMode.Lazy,
                AlbumImageCacheReadMode.Lazy,
                cancellationToken);
            photo.CompleteDetailedThumbnailLoad(loaded: true, originalLoaded: true, ownsOriginalLoad);
            if (isVisible)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    photo.SetLoadedImage(detailedBitmap, isDetailedThumbnail: true, "Detailed image loaded"));
            }
            else
            {
                detailedBitmap.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            ResetWorkStatus(photo, work, ownsOriginalLoad);
        }
        catch (Exception ex)
        {
            ResetWorkStatus(photo, work, ownsOriginalLoad);
            if (isVisible)
            {
                await Dispatcher.UIThread.InvokeAsync(() => photo.Status = ex.Message);
            }
        }
        finally
        {
            if (isVisible)
            {
                await Dispatcher.UIThread.InvokeAsync(() => photo.IsImageLoading = false);
            }
        }
    }

    private async Task<bool> TryLoadCachedDetailedThumbnailForFastWorkAsync(
        AlbumPhotoViewModel photo,
        CancellationToken cancellationToken)
    {
        if (photo.DetailedThumbnailStatus != AlbumImageItemStatus.Unloaded)
        {
            return false;
        }

        try
        {
            var bitmap = await _imageCache.LoadDetailedThumbnailBitmapAsync(
                photo.AlbumId,
                photo.PhotoId,
                $"{photo.PhotoId}-full{photo.FileExtension}",
                photo.DownloadUrl,
                _httpClient,
                AlbumImageCacheReadMode.Lookup,
                AlbumImageCacheReadMode.Lookup,
                cancellationToken);

            photo.CompleteDetailedThumbnailLoad(loaded: true, originalLoaded: false, ownsOriginalLoad: false);
            await Dispatcher.UIThread.InvokeAsync(() =>
                photo.SetLoadedImage(bitmap, isDetailedThumbnail: true, "Detailed image loaded"));
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    private async Task ExecuteOriginalWarmupAsync(AlbumPhotoViewModel photo, CancellationToken cancellationToken)
    {
        try
        {
            await _imageCache.WarmOriginalAsync(
                photo.AlbumId,
                $"{photo.PhotoId}-full{photo.FileExtension}",
                photo.DownloadUrl,
                _httpClient,
                AlbumImageCacheReadMode.Lazy,
                cancellationToken);
            photo.CompleteOriginalImageLoad(loaded: true);
        }
        catch
        {
            photo.CompleteOriginalImageLoad(loaded: false);
        }
    }

    private static void ResetWorkStatus(AlbumPhotoViewModel photo, AlbumImageWork work, bool ownsOriginalLoad)
    {
        if (work == AlbumImageWork.FastThumbnail)
        {
            photo.CompleteFastThumbnailLoad(loaded: false);
            return;
        }

        if (work == AlbumImageWork.DetailedThumbnail)
        {
            photo.CompleteDetailedThumbnailLoad(loaded: false, originalLoaded: false, ownsOriginalLoad);
            return;
        }

        photo.CompleteOriginalImageLoad(loaded: false);
    }

    private IReadOnlyList<AlbumPhotoViewModel> GetViewportPhotos()
    {
        lock (_sync)
        {
            return _viewportIndices
                .Order()
                .Select(index => index >= 0 && index < _photos.Count ? _photos[index] : null)
                .Where(photo => photo is not null)
                .Cast<AlbumPhotoViewModel>()
                .ToList();
        }
    }

    private IEnumerable<AlbumPhotoViewModel> GetPhotosByViewportDistance()
    {
        IReadOnlyList<AlbumPhotoViewModel> photos;
        int min;
        int max;
        lock (_sync)
        {
            photos = _photos;
            if (_viewportIndices.Count == 0)
            {
                min = 0;
                max = -1;
            }
            else
            {
                min = _viewportIndices.Min();
                max = _viewportIndices.Max();
            }
        }

        if (photos.Count == 0)
        {
            yield break;
        }

        if (max >= min)
        {
            for (var index = min; index <= max && index < photos.Count; index++)
            {
                if (index >= 0)
                {
                    yield return photos[index];
                }
            }
        }

        for (var distance = 1; distance < photos.Count; distance++)
        {
            var down = max + distance;
            if (down >= 0 && down < photos.Count)
            {
                yield return photos[down];
            }

            var up = min - distance;
            if (up >= 0 && up < photos.Count)
            {
                yield return photos[up];
            }
        }
    }

    private string GetAlbumId()
    {
        return _photos.FirstOrDefault()?.AlbumId ?? "";
    }

    private enum AlbumImageWorkerKind
    {
        Emergency,
        Heavy,
        Normal,
        Light
    }

    private enum AlbumImageWork
    {
        FastThumbnail,
        DetailedThumbnail,
        OriginalImage
    }
}
