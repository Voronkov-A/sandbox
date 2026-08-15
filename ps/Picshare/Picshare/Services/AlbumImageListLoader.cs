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
    private Dictionary<int, int> _viewportSnapshotIndexCounts = new();
    private Dictionary<int, int> _viewportLoadedIndexCounts = new();
    private Dictionary<AlbumPhotoViewModel, List<int[]>> _viewportLoadedRegistrations = new();
    private List<int> _orderedViewportIndices = new();
    private HashSet<int> _priorityIndices = new();
    private List<int> _orderedPriorityIndices = new();
    private HashSet<AlbumImageWarmupKey> _skippedWarmupKeys = new();
    private HashSet<AlbumImageWarmupKey> _failedVisibleKeys = new();
    private Dictionary<int, ActiveImageWork> _activeImageWorks = new();
    private bool _isFullImageWarmupActive;
    private string _fullImageWarmupAlbumId = "";
    private string _activeFullImageDuplicateGroupId = "";
    private AlbumPhotoViewModel? _fullImageWarmupCurrentPhoto;
    private List<AlbumPhotoViewModel> _fullImageWarmupNavigationPhotos = new();
    private List<AlbumPhotoViewModel> _fullImageWarmupDuplicatePhotos = new();
    private List<AlbumPhotoViewModel> _fullImageWarmupQueue = new();
    private HashSet<AlbumPhotoViewModel> _fullImageWarmupPriority1Photos = new();
    private HashSet<AlbumPhotoViewModel> _activeFullImageDuplicateContextPhotos = new();
    private HashSet<AlbumPhotoViewModel> _attemptedFullImageWarmupPhotos = new();
    private CancellationTokenSource? _lifetime;
    private readonly List<Task> _workerTasks = new();
    private Task _stoppedWorkerTasks = Task.CompletedTask;
    private int _maximumParallelism = LocalUserSettings.DefaultMaximumParallelism;
    private int _detailedThumbnailPixelWidth = 220;
    private int _detailedThumbnailPixelHeight = 150;
    private int _workerCount;
    private int _pendingWorkerSignalCount;
    private long _generation;
    private bool _disposed;
    private const int FullImageWarmupRadius = 8;

    public AlbumImageListLoader(ImageCacheService imageCache, HttpClient httpClient)
    {
        _imageCache = imageCache;
        _httpClient = httpClient;
    }

    public void SetPhotos(IReadOnlyList<AlbumPhotoViewModel> photos, int maximumParallelism)
    {
        var generation = Interlocked.Increment(ref _generation);
        StopWorkers();
        _maximumParallelism = Math.Clamp(maximumParallelism, 1, 64);
        foreach (var photo in photos)
        {
            photo.ResetImageLoadStatusesForNewGeneration();
        }

        lock (_sync)
        {
            _photos = photos.ToArray();
            _indexByPhoto = _photos
                .Select((photo, index) => new { photo, index })
                .ToDictionary(item => item.photo, item => item.index);
            ClearViewportNoLock();
            _orderedViewportIndices = new List<int>();
            _priorityIndices = new HashSet<int>();
            _orderedPriorityIndices = new List<int>();
            _skippedWarmupKeys = new HashSet<AlbumImageWarmupKey>();
            _failedVisibleKeys = new HashSet<AlbumImageWarmupKey>();
            _activeImageWorks = new Dictionary<int, ActiveImageWork>();
        }

        StartWorkers(generation);
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

        IReadOnlyList<AlbumPhotoViewModel> photos;
        IReadOnlyList<int> viewportSnapshotIndices;
        IReadOnlyList<LoadedViewportRegistration> loadedViewportRegistrations;
        IReadOnlyList<AlbumPhotoViewModel> priorityPhotos;
        lock (_sync)
        {
            photos = _photos.ToArray();
            viewportSnapshotIndices = GetListViewportSnapshotIndicesForRestoreNoLock();
            loadedViewportRegistrations = GetLoadedViewportRegistrationsForRestoreNoLock();
            priorityPhotos = _orderedPriorityIndices
                .Select(index => index >= 0 && index < _photos.Count ? _photos[index] : null)
                .Where(photo => photo is not null)
                .Cast<AlbumPhotoViewModel>()
                .ToList();
        }

        SetPhotos(photos, maximumParallelism);
        RestoreViewportState(viewportSnapshotIndices, loadedViewportRegistrations);
        foreach (var photo in priorityPhotos)
        {
            AddPriorityPhoto(photo);
        }
    }

    public void SetDetailedThumbnailSize(int pixelWidth, int pixelHeight)
    {
        lock (_sync)
        {
            _detailedThumbnailPixelWidth = Math.Max(1, pixelWidth);
            _detailedThumbnailPixelHeight = Math.Max(1, pixelHeight);
        }
    }

    public (int PixelWidth, int PixelHeight) GetDetailedThumbnailSize()
    {
        lock (_sync)
        {
            return (_detailedThumbnailPixelWidth, _detailedThumbnailPixelHeight);
        }
    }

    public void UpdateFullImageWarmup(
        AlbumPhotoViewModel? currentPhoto,
        IReadOnlyList<AlbumPhotoViewModel> navigationPhotos,
        IReadOnlyList<AlbumPhotoViewModel> duplicatePhotos)
    {
        lock (_sync)
        {
            if (currentPhoto is null)
            {
                ClearFullImageWarmupNoLock();
            }
            else
            {
                _isFullImageWarmupActive = true;
                _fullImageWarmupAlbumId = currentPhoto.AlbumId;
                _fullImageWarmupCurrentPhoto = currentPhoto;
                _fullImageWarmupNavigationPhotos = navigationPhotos
                    .Where(photo => string.Equals(photo.AlbumId, currentPhoto.AlbumId, StringComparison.Ordinal))
                    .Distinct()
                    .ToList();
                _fullImageWarmupDuplicatePhotos = duplicatePhotos
                    .Where(photo => string.Equals(photo.AlbumId, currentPhoto.AlbumId, StringComparison.Ordinal))
                    .Distinct()
                    .ToList();

                var duplicateGroupId = currentPhoto.DuplicateGroupId ?? "";
                if (!string.Equals(_activeFullImageDuplicateGroupId, duplicateGroupId, StringComparison.Ordinal))
                {
                    _activeFullImageDuplicateGroupId = duplicateGroupId;
                    _activeFullImageDuplicateContextPhotos.Clear();
                }

                if (!string.IsNullOrWhiteSpace(duplicateGroupId))
                {
                    _activeFullImageDuplicateContextPhotos.Add(currentPhoto);
                }

                RebuildFullImageWarmupNoLock();
            }
        }

        SignalWorkers();
    }

    public void ClearFullImageWarmup()
    {
        lock (_sync)
        {
            ClearFullImageWarmupNoLock();
        }

        SignalWorkers();
    }

    public void RestartPreservingState(int maximumParallelism)
    {
        IReadOnlyList<AlbumPhotoViewModel> photos;
        IReadOnlyList<int> viewportSnapshotIndices;
        IReadOnlyList<LoadedViewportRegistration> loadedViewportRegistrations;
        IReadOnlyList<AlbumPhotoViewModel> priorityPhotos;
        lock (_sync)
        {
            photos = _photos.ToArray();
            viewportSnapshotIndices = GetListViewportSnapshotIndicesForRestoreNoLock();
            loadedViewportRegistrations = GetLoadedViewportRegistrationsForRestoreNoLock();
            priorityPhotos = _orderedPriorityIndices
                .Select(index => index >= 0 && index < _photos.Count ? _photos[index] : null)
                .Where(photo => photo is not null)
                .Cast<AlbumPhotoViewModel>()
                .ToList();
        }

        SetPhotos(photos, maximumParallelism);
        RestoreViewportState(viewportSnapshotIndices, loadedViewportRegistrations);
        foreach (var photo in priorityPhotos)
        {
            AddPriorityPhoto(photo);
        }
    }

    public void UpdateViewport(IReadOnlyList<AlbumPhotoViewModel> visiblePhotos)
    {
        lock (_sync)
        {
            var previousSnapshotIndices = _viewportSnapshotIndexCounts.Keys.ToHashSet();
            var viewportIndices = visiblePhotos
                .SelectMany(photo => photo.DuplicateStackPhoto is not null && !ReferenceEquals(photo.DuplicateStackPhoto, photo)
                    ? new[] { photo, photo.DuplicateStackPhoto }
                    : new[] { photo })
                .Select(photo => _indexByPhoto.TryGetValue(photo, out var index) ? index : -1)
                .Where(index => index >= 0)
                .ToList();
            foreach (var index in viewportIndices.Distinct().Where(index => !previousSnapshotIndices.Contains(index)))
            {
                ClearVisibleFailureSkippedNoLock(index);
            }

            _orderedViewportIndices = viewportIndices
                .Distinct()
                .ToList();
            _viewportSnapshotIndexCounts = viewportIndices
                .GroupBy(index => index)
                .ToDictionary(group => group.Key, group => group.Count());
            RebuildLoadedViewportCountsNoLock();

            RefreshViewportIndicesNoLock();
        }

        SignalWorkers();
    }

    public void AddViewportPhoto(AlbumPhotoViewModel photo)
    {
        lock (_sync)
        {
            var indices = GetLoadedViewportRegistrationIndicesNoLock(photo);
            if (indices.Length > 0)
            {
                if (!_viewportLoadedRegistrations.TryGetValue(photo, out var registrations))
                {
                    registrations = new List<int[]>();
                    _viewportLoadedRegistrations[photo] = registrations;
                }

                registrations.Add(indices);
                foreach (var index in indices)
                {
                    AddLoadedViewportIndexNoLock(index);
                }
            }
        }

        SignalWorkers();
    }

    public void AddPriorityPhoto(AlbumPhotoViewModel photo)
    {
        lock (_sync)
        {
            AddPriorityPhotoNoLock(photo);
            if (photo.DuplicateStackPhoto is not null && !ReferenceEquals(photo.DuplicateStackPhoto, photo))
            {
                AddPriorityPhotoNoLock(photo.DuplicateStackPhoto);
            }
        }

        SignalWorkers();
    }

    public void ClearPriorityPhotos()
    {
        lock (_sync)
        {
            _priorityIndices = new HashSet<int>();
            _orderedPriorityIndices = new List<int>();
        }

        SignalWorkers();
    }

    public void ClearViewport()
    {
        lock (_sync)
        {
            ClearViewportNoLock();
            _orderedViewportIndices = new List<int>();
        }

        SignalWorkers();
    }

    public void RemoveViewportPhoto(AlbumPhotoViewModel photo)
    {
        lock (_sync)
        {
            var removedLoadedRegistration = RemoveViewportPhotoNoLock(photo);

            if (!removedLoadedRegistration &&
                photo.DuplicateStackPhoto is not null &&
                !ReferenceEquals(photo.DuplicateStackPhoto, photo))
            {
                RemoveViewportPhotoNoLock(photo.DuplicateStackPhoto);
            }
        }

        SignalWorkers();
    }

    public IReadOnlyList<AlbumPhotoViewModel> RemoveLoadedViewportPhoto(AlbumPhotoViewModel photo)
    {
        List<AlbumPhotoViewModel> removedPhotos;
        lock (_sync)
        {
            List<int> removedIndices = new();
            removedIndices.AddRange(RemoveLoadedViewportPhotoNoLock(photo));

            removedPhotos = removedIndices
                .Distinct()
                .Select(index => index >= 0 && index < _photos.Count ? _photos[index] : null)
                .Where(removedPhoto => removedPhoto is not null)
                .Cast<AlbumPhotoViewModel>()
                .ToList();
        }

        SignalWorkers();
        return removedPhotos;
    }

    public void Clear()
    {
        Interlocked.Increment(ref _generation);
        StopWorkers();
        lock (_sync)
        {
            _photos = [];
            _indexByPhoto = new Dictionary<AlbumPhotoViewModel, int>();
            ClearViewportNoLock();
            _orderedViewportIndices = new List<int>();
            _priorityIndices = new HashSet<int>();
            _orderedPriorityIndices = new List<int>();
            _skippedWarmupKeys = new HashSet<AlbumImageWarmupKey>();
            _failedVisibleKeys = new HashSet<AlbumImageWarmupKey>();
            _activeImageWorks = new Dictionary<int, ActiveImageWork>();
            ClearFullImageWarmupNoLock();
        }
    }

    private void ClearFullImageWarmupNoLock()
    {
        _isFullImageWarmupActive = false;
        _fullImageWarmupAlbumId = "";
        _activeFullImageDuplicateGroupId = "";
        _fullImageWarmupCurrentPhoto = null;
        _fullImageWarmupNavigationPhotos = new List<AlbumPhotoViewModel>();
        _fullImageWarmupDuplicatePhotos = new List<AlbumPhotoViewModel>();
        _fullImageWarmupQueue = new List<AlbumPhotoViewModel>();
        _fullImageWarmupPriority1Photos = new HashSet<AlbumPhotoViewModel>();
        _activeFullImageDuplicateContextPhotos = new HashSet<AlbumPhotoViewModel>();
        _attemptedFullImageWarmupPhotos = new HashSet<AlbumPhotoViewModel>();
        _imageCache.SetOriginalImageCachePrioritySets("", [], []);
    }

    public async Task ClearAndWaitAsync()
    {
        Clear();
        await _stoppedWorkerTasks.ConfigureAwait(false);
    }

    public IReadOnlyList<AlbumPhotoViewModel> GetViewportPhotosSnapshot()
    {
        return GetViewportPhotos();
    }

    public IReadOnlyList<AlbumPhotoViewModel> GetListViewportPhotosSnapshot()
    {
        return GetListViewportPhotos();
    }

    public string DescribePhotoImageLoadState(AlbumPhotoViewModel photo)
    {
        lock (_sync)
        {
            var hasIndex = _indexByPhoto.TryGetValue(photo, out var index);
            if (!hasIndex)
            {
                return $"index=missing, status=F:{photo.FastThumbnailStatus}/D:{photo.DetailedThumbnailStatus}/O:{photo.OriginalImageStatus}, busy={photo.IsImageLoadBusyAtomic()}, image={(photo.Image is null ? "null" : "set")}, full={photo.IsFullImageLoaded}, loading={photo.IsImageLoading}";
            }

            var fastSkipped = _failedVisibleKeys.Contains(new AlbumImageWarmupKey(index, AlbumImageWork.FastThumbnail));
            var detailedSkipped = _failedVisibleKeys.Contains(new AlbumImageWarmupKey(index, AlbumImageWork.DetailedThumbnail));
            var originalSkipped = _failedVisibleKeys.Contains(new AlbumImageWarmupKey(index, AlbumImageWork.OriginalImage));
            var active = _activeImageWorks.TryGetValue(index, out var activeWork)
                ? $", active={activeWork.Work}/{(activeWork.IsVisible ? "visible" : "warmup")}/{(DateTime.UtcNow - activeWork.StartedUtc).TotalSeconds:0.0}s"
                : "";
            return $"index={index}, viewport={_viewportIndices.Contains(index)}, snapshot={_viewportSnapshotIndexCounts.ContainsKey(index)}, loaded={_viewportLoadedIndexCounts.ContainsKey(index)}, priority={_priorityIndices.Contains(index)}, ordered={_orderedViewportIndices.Contains(index)}, skipped=F:{fastSkipped}/D:{detailedSkipped}/O:{originalSkipped}, status=F:{photo.FastThumbnailStatus}/D:{photo.DetailedThumbnailStatus}/O:{photo.OriginalImageStatus}, busy={photo.IsImageLoadBusyAtomic()}, image={(photo.Image is null ? "null" : "set")}, full={photo.IsFullImageLoaded}, loading={photo.IsImageLoading}{active}";
        }
    }

    public IReadOnlyList<int> GetListViewportIndicesSnapshot()
    {
        lock (_sync)
        {
            return GetListViewportIndicesForRestoreNoLock();
        }
    }

    public void RestoreViewportIndices(IReadOnlyList<int> viewportIndices)
    {
        RestoreViewportState(viewportIndices, []);
    }

    private void RestoreViewportState(
        IReadOnlyList<int> viewportSnapshotIndices,
        IReadOnlyList<LoadedViewportRegistration> loadedViewportRegistrations)
    {
        lock (_sync)
        {
            var validIndices = viewportSnapshotIndices
                .Where(index => index >= 0 && index < _photos.Count)
                .ToList();
            _orderedViewportIndices = validIndices
                .Distinct()
                .ToList();
            _viewportSnapshotIndexCounts = validIndices
                .GroupBy(index => index)
                .ToDictionary(group => group.Key, group => group.Count());
            _viewportLoadedRegistrations = new Dictionary<AlbumPhotoViewModel, List<int[]>>();
            foreach (var registration in loadedViewportRegistrations)
            {
                if (!_indexByPhoto.ContainsKey(registration.Photo))
                {
                    continue;
                }

                var registrationIndices = registration.Indices
                    .Where(index => index >= 0 && index < _photos.Count)
                    .Distinct()
                    .ToArray();
                if (registrationIndices.Length == 0)
                {
                    continue;
                }

                if (!_viewportLoadedRegistrations.TryGetValue(registration.Photo, out var registrations))
                {
                    registrations = new List<int[]>();
                    _viewportLoadedRegistrations[registration.Photo] = registrations;
                }

                registrations.Add(registrationIndices);
            }

            RebuildLoadedViewportCountsNoLock();
            RefreshViewportIndicesNoLock();
        }

        SignalWorkers();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Clear();
        _stoppedWorkerTasks.ContinueWith(_ => _updates.Dispose(), TaskScheduler.Default);
    }

    private void StartWorkers(long generation)
    {
        if (_disposed)
        {
            return;
        }

        DrainUpdateSignals();
        var lifetime = new CancellationTokenSource();
        _lifetime = lifetime;
        var heavyWorkerCount = Math.Max(1, _maximumParallelism * 3 / 4);
        var normalWorkerCount = Math.Max(0, _maximumParallelism - heavyWorkerCount - 2);
        var lightWorkerCount = Math.Min(1, _maximumParallelism - 1);
        var emergencyWorkerCount = Math.Min(1, Math.Max(0, _maximumParallelism - 2));
        Volatile.Write(ref _workerCount, heavyWorkerCount + normalWorkerCount + lightWorkerCount + emergencyWorkerCount);

        for (var index = 0; index < emergencyWorkerCount; index++)
        {
            _workerTasks.Add(Task.Run(() => RunWorkerAsync(AlbumImageWorkerKind.Emergency, generation, lifetime.Token)));
        }

        for (var index = 0; index < heavyWorkerCount; index++)
        {
            _workerTasks.Add(Task.Run(() => RunWorkerAsync(AlbumImageWorkerKind.Heavy, generation, lifetime.Token)));
        }

        for (var index = 0; index < normalWorkerCount; index++)
        {
            _workerTasks.Add(Task.Run(() => RunWorkerAsync(AlbumImageWorkerKind.Normal, generation, lifetime.Token)));
        }

        for (var index = 0; index < lightWorkerCount; index++)
        {
            _workerTasks.Add(Task.Run(() => RunWorkerAsync(AlbumImageWorkerKind.Light, generation, lifetime.Token)));
        }
    }

    private void StopWorkers()
    {
        var lifetime = _lifetime;
        if (lifetime is null)
        {
            return;
        }

        var workerTasks = _workerTasks.ToArray();
        _lifetime = null;
        try
        {
            lifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _workerTasks.Clear();
        Volatile.Write(ref _workerCount, 0);
        if (workerTasks.Length == 0)
        {
            lifetime.Dispose();
        }
        else
        {
            var previousStoppedWorkerTasks = _stoppedWorkerTasks;
            var currentStoppedWorkerTasks = Task.WhenAll(workerTasks)
                .ContinueWith(_ => lifetime.Dispose(), TaskScheduler.Default);
            _stoppedWorkerTasks = Task.WhenAll(previousStoppedWorkerTasks, currentStoppedWorkerTasks);
        }
    }

    private void SignalWorkers()
    {
        if (_disposed)
        {
            return;
        }

        var wakeCount = Math.Max(1, Volatile.Read(ref _workerCount));
        while (true)
        {
            var currentPending = Math.Max(0, Volatile.Read(ref _pendingWorkerSignalCount));
            if (currentPending >= wakeCount)
            {
                return;
            }

            var additionalSignals = wakeCount - currentPending;
            if (Interlocked.CompareExchange(ref _pendingWorkerSignalCount, wakeCount, currentPending) != currentPending)
            {
                continue;
            }

            for (var index = 0; index < additionalSignals; index++)
            {
                _updates.Release();
            }

            return;
        }
    }

    private void DrainUpdateSignals()
    {
        Interlocked.Exchange(ref _pendingWorkerSignalCount, 0);
        while (_updates.Wait(0))
        {
        }
    }

    private int[] GetLoadedViewportRegistrationIndicesNoLock(AlbumPhotoViewModel photo)
    {
        List<int> indices = new();
        if (_indexByPhoto.TryGetValue(photo, out var index))
        {
            indices.Add(index);
        }

        if (photo.DuplicateStackPhoto is not null &&
            !ReferenceEquals(photo.DuplicateStackPhoto, photo) &&
            _indexByPhoto.TryGetValue(photo.DuplicateStackPhoto, out var duplicateIndex))
        {
            indices.Add(duplicateIndex);
        }

        return indices.Distinct().ToArray();
    }

    private void AddLoadedViewportIndexNoLock(int index)
    {
        _viewportLoadedIndexCounts.TryGetValue(index, out var count);
        _viewportLoadedIndexCounts[index] = count + 1;
        ClearVisibleFailureSkippedNoLock(index);
    }

    private void ClearViewportNoLock()
    {
        _viewportIndices = new HashSet<int>();
        _viewportSnapshotIndexCounts = new Dictionary<int, int>();
        _viewportLoadedIndexCounts = new Dictionary<int, int>();
        _viewportLoadedRegistrations = new Dictionary<AlbumPhotoViewModel, List<int[]>>();
    }

    private void RebuildLoadedViewportCountsNoLock()
    {
        _viewportLoadedIndexCounts = new Dictionary<int, int>();
        foreach (var index in _viewportLoadedRegistrations.Values.SelectMany(registrations => registrations).SelectMany(indices => indices))
        {
            _viewportLoadedIndexCounts.TryGetValue(index, out var count);
            _viewportLoadedIndexCounts[index] = count + 1;
        }
    }

    private void RefreshViewportIndicesNoLock()
    {
        var orderedViewportIndices = _orderedViewportIndices
            .Where(index => _viewportSnapshotIndexCounts.ContainsKey(index))
            .ToList();
        foreach (var index in _viewportSnapshotIndexCounts.Keys)
        {
            if (index >= 0 && index < _photos.Count && !orderedViewportIndices.Contains(index))
            {
                orderedViewportIndices.Add(index);
            }
        }

        _orderedViewportIndices = orderedViewportIndices;
        _viewportIndices = _viewportSnapshotIndexCounts.Keys
            .ToHashSet();
    }

    private IReadOnlyList<int> RemoveLoadedViewportPhotoNoLock(AlbumPhotoViewModel photo)
    {
        if (!_viewportLoadedRegistrations.TryGetValue(photo, out var registrations) ||
            registrations.Count == 0)
        {
            return [];
        }

        var registration = registrations[^1];
        registrations.RemoveAt(registrations.Count - 1);
        if (registrations.Count == 0)
        {
            _viewportLoadedRegistrations.Remove(photo);
        }

        foreach (var index in registration)
        {
            if (!_viewportLoadedIndexCounts.TryGetValue(index, out var loadedCount))
            {
                continue;
            }

            if (loadedCount > 1)
            {
                _viewportLoadedIndexCounts[index] = loadedCount - 1;
            }
            else
            {
                _viewportLoadedIndexCounts.Remove(index);
            }
        }

        RefreshViewportIndicesNoLock();
        return registration;
    }

    private bool RemoveViewportPhotoNoLock(AlbumPhotoViewModel photo)
    {
        if (RemoveLoadedViewportPhotoNoLock(photo).Count > 0)
        {
            return true;
        }

        if (!_indexByPhoto.TryGetValue(photo, out var index))
        {
            return false;
        }

        if (_viewportSnapshotIndexCounts.TryGetValue(index, out var snapshotCount))
        {
            if (snapshotCount > 1)
            {
                _viewportSnapshotIndexCounts[index] = snapshotCount - 1;
                RefreshViewportIndicesNoLock();
                return false;
            }

            _viewportSnapshotIndexCounts.Remove(index);
        }
        else
        {
            return false;
        }

        if (!_viewportSnapshotIndexCounts.ContainsKey(index) &&
            !_viewportLoadedIndexCounts.ContainsKey(index))
        {
            _orderedViewportIndices = _orderedViewportIndices
                .Where(candidate => candidate != index)
                .ToList();
        }

        RefreshViewportIndicesNoLock();
        return false;
    }

    private void AddPriorityPhotoNoLock(AlbumPhotoViewModel photo)
    {
        if (_indexByPhoto.TryGetValue(photo, out var index))
        {
            if (_priorityIndices.Add(index))
            {
                ClearVisibleFailureSkippedNoLock(index);
                _orderedPriorityIndices = _orderedPriorityIndices
                    .Append(index)
                    .ToList();
            }
        }
    }

    private bool IsCurrentGeneration(long generation)
    {
        return Volatile.Read(ref _generation) == generation;
    }

    private void RebuildFullImageWarmupNoLock()
    {
        if (!_isFullImageWarmupActive || _fullImageWarmupCurrentPhoto is null)
        {
            _imageCache.SetOriginalImageCachePrioritySets("", [], []);
            return;
        }

        var priority2Photos = _activeFullImageDuplicateContextPhotos
            .Append(_fullImageWarmupCurrentPhoto)
            .Where(photo => string.Equals(photo.AlbumId, _fullImageWarmupAlbumId, StringComparison.Ordinal))
            .Distinct()
            .ToList();
        var currentAnchor = GetFullImageWarmupNavigationAnchorNoLock(_fullImageWarmupCurrentPhoto);
        var currentIndex = _fullImageWarmupNavigationPhotos.FindIndex(photo => ReferenceEquals(photo, currentAnchor));
        if (currentIndex < 0)
        {
            currentIndex = _fullImageWarmupNavigationPhotos.FindIndex(photo => ReferenceEquals(photo, _fullImageWarmupCurrentPhoto));
        }

        var priority1Photos = new List<AlbumPhotoViewModel>();
        if (currentIndex >= 0 && _fullImageWarmupNavigationPhotos.Count > 1)
        {
            for (var distance = 1; distance <= FullImageWarmupRadius; distance++)
            {
                AddFullImageWarmupCandidateNoLock(priority1Photos, currentIndex + distance);
                AddFullImageWarmupCandidateNoLock(priority1Photos, currentIndex - distance);
            }
        }

        var priority2Set = priority2Photos.ToHashSet();
        priority1Photos = priority1Photos
            .Where(photo => !priority2Set.Contains(photo))
            .Distinct()
            .ToList();
        _fullImageWarmupPriority1Photos = priority1Photos.ToHashSet();
        _fullImageWarmupQueue = priority1Photos
            .Where(photo => !_attemptedFullImageWarmupPhotos.Contains(photo))
            .ToList();
        var warmupTargetPhotos = priority1Photos
            .Concat(GetFullImageDuplicateWarmupCandidatesNoLock(priority1Photos, priority2Set))
            .ToHashSet();
        _attemptedFullImageWarmupPhotos.RemoveWhere(photo => !warmupTargetPhotos.Contains(photo));

        _imageCache.SetOriginalImageCachePrioritySets(
            _fullImageWarmupAlbumId,
            priority1Photos.Select(GetOriginalCacheFileName),
            priority2Photos.Select(GetOriginalCacheFileName));
    }

    private IEnumerable<AlbumPhotoViewModel> GetFullImageDuplicateWarmupCandidatesNoLock(
        IReadOnlyCollection<AlbumPhotoViewModel> priority1Photos,
        IReadOnlySet<AlbumPhotoViewModel> priority2Photos)
    {
        return _fullImageWarmupDuplicatePhotos
            .Where(photo => !ReferenceEquals(photo, _fullImageWarmupCurrentPhoto) &&
                !priority2Photos.Contains(photo) &&
                !priority1Photos.Contains(photo))
            .Distinct();
    }

    private AlbumPhotoViewModel GetFullImageWarmupNavigationAnchorNoLock(AlbumPhotoViewModel photo)
    {
        if (!string.IsNullOrWhiteSpace(photo.DuplicateGroupId))
        {
            var mainPhoto = _fullImageWarmupNavigationPhotos.FirstOrDefault(candidate =>
                string.Equals(candidate.DuplicateGroupId, photo.DuplicateGroupId, StringComparison.Ordinal) &&
                candidate.IsDuplicateGroupMain);
            if (mainPhoto is not null)
            {
                return mainPhoto;
            }
        }

        return photo;
    }

    private void AddFullImageWarmupCandidateNoLock(List<AlbumPhotoViewModel> candidates, int index)
    {
        if (_fullImageWarmupNavigationPhotos.Count == 0)
        {
            return;
        }

        var normalizedIndex = index % _fullImageWarmupNavigationPhotos.Count;
        if (normalizedIndex < 0)
        {
            normalizedIndex += _fullImageWarmupNavigationPhotos.Count;
        }

        var candidate = _fullImageWarmupNavigationPhotos[normalizedIndex];
        if (!ReferenceEquals(candidate, _fullImageWarmupCurrentPhoto))
        {
            candidates.Add(candidate);
        }
    }

    private bool TryTakeFullImageOriginalWarmup(
        long generation,
        out AlbumPhotoViewModel photo,
        out int workToken)
    {
        lock (_sync)
        {
            if (!_isFullImageWarmupActive || !IsCurrentGeneration(generation))
            {
                photo = null!;
                workToken = 0;
                return false;
            }

            foreach (var candidate in _fullImageWarmupQueue.ToList())
            {
                _fullImageWarmupQueue.Remove(candidate);
                if (_attemptedFullImageWarmupPhotos.Contains(candidate) ||
                    candidate.OriginalImageStatus != AlbumImageItemStatus.Unloaded ||
                    !candidate.TryBeginOriginalImageLoad(out workToken))
                {
                    continue;
                }

                photo = candidate;
                return true;
            }
        }

        photo = null!;
        workToken = 0;
        return false;
    }

    private bool TryTakeFullImageDuplicateOriginalWarmup(
        long generation,
        out AlbumPhotoViewModel photo,
        out int workToken)
    {
        lock (_sync)
        {
            if (!_isFullImageWarmupActive || !IsCurrentGeneration(generation))
            {
                photo = null!;
                workToken = 0;
                return false;
            }

            var priority1Photos = _fullImageWarmupPriority1Photos;
            var priority2Photos = _activeFullImageDuplicateContextPhotos
                .Append(_fullImageWarmupCurrentPhoto)
                .Where(candidate => candidate is not null)
                .Cast<AlbumPhotoViewModel>()
                .ToHashSet();
            foreach (var candidate in GetFullImageDuplicateWarmupCandidatesNoLock(priority1Photos, priority2Photos))
            {
                if (_attemptedFullImageWarmupPhotos.Contains(candidate) ||
                    candidate.OriginalImageStatus != AlbumImageItemStatus.Unloaded ||
                    !candidate.TryBeginOriginalImageLoad(out workToken))
                {
                    continue;
                }

                photo = candidate;
                return true;
            }
        }

        photo = null!;
        workToken = 0;
        return false;
    }

    private async Task RunWorkerAsync(AlbumImageWorkerKind kind, long generation, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && IsCurrentGeneration(generation))
        {
            try
            {
                var didWork = await TryRunNextWorkItemAsync(kind, generation, cancellationToken);
                if (didWork)
                {
                    continue;
                }

                await _updates.WaitAsync(cancellationToken);
                if (Interlocked.Decrement(ref _pendingWorkerSignalCount) < 0)
                {
                    Interlocked.Exchange(ref _pendingWorkerSignalCount, 0);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PicshareImageLoader: worker {kind} loop failed: {ex.GetType().Name}: {ex.Message}");
                SignalWorkers();
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task<bool> TryRunNextWorkItemAsync(AlbumImageWorkerKind kind, long generation, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || !IsCurrentGeneration(generation))
        {
            return false;
        }

        return kind switch
        {
            AlbumImageWorkerKind.Emergency => await TryRunEmergencyWorkAsync(generation, cancellationToken),
            AlbumImageWorkerKind.Heavy => await TryRunHeavyWorkAsync(generation, cancellationToken),
            AlbumImageWorkerKind.Normal => await TryRunNormalWorkAsync(generation, cancellationToken),
            AlbumImageWorkerKind.Light => await TryRunLightWorkAsync(generation, cancellationToken),
            _ => false
        };
    }

    private async Task<bool> TryRunEmergencyWorkAsync(long generation, CancellationToken cancellationToken)
    {
        if (!IsCurrentGeneration(generation))
        {
            return false;
        }

        if (TryTakeVisibleFastWork(generation, out var visibleFastPhoto, out var visibleFastWasAlreadyLoaded, out var visibleFastWorkToken))
        {
            await ExecuteThumbnailWorkAsync(
                visibleFastPhoto,
                AlbumImageWork.FastThumbnail,
                isVisible: true,
                ownsOriginalLoad: false,
                visibleFastWasAlreadyLoaded,
                detailedThumbnailWasAlreadyLoaded: false,
                visibleFastWorkToken,
                generation,
                cancellationToken);
            return true;
        }

        return false;
    }

    private async Task<bool> TryRunHeavyWorkAsync(long generation, CancellationToken cancellationToken)
    {
        if (!IsCurrentGeneration(generation))
        {
            return false;
        }

        if (TryTakeVisibleThumbnailWork(
            generation,
            preferDetailed: true,
            out var visiblePhoto,
            out var visibleWork,
            out var visibleOwnsOriginalLoad,
            out var visibleThumbnailFastWasAlreadyLoaded,
            out var visibleThumbnailDetailedWasAlreadyLoaded,
            out var visibleThumbnailWorkToken))
        {
            await ExecuteThumbnailWorkAsync(
                visiblePhoto,
                visibleWork,
                isVisible: true,
                visibleOwnsOriginalLoad,
                visibleThumbnailFastWasAlreadyLoaded,
                visibleThumbnailDetailedWasAlreadyLoaded,
                visibleThumbnailWorkToken,
                generation,
                cancellationToken);
            return true;
        }

        if (TryTakeFullImageOriginalWarmup(generation, out var fullImageWarmupPhoto, out var fullImageWarmupToken))
        {
            await ExecuteFullImageOriginalWarmupAsync(fullImageWarmupPhoto, fullImageWarmupToken, generation, cancellationToken);
            return true;
        }

        if (_imageCache.HasCacheRoom(GetAlbumId(), AlbumImageCacheKind.DetailedThumbnail) &&
            TryTakeNearestWork(generation, AlbumImageWork.DetailedThumbnail, out var detailedPhoto, out var detailedOwnsOriginalLoad, out var detailedWorkToken))
        {
            await ExecuteThumbnailWorkAsync(detailedPhoto, AlbumImageWork.DetailedThumbnail, isVisible: false, detailedOwnsOriginalLoad, fastThumbnailWasAlreadyLoaded: false, detailedThumbnailWasAlreadyLoaded: false, detailedWorkToken, generation, cancellationToken);
            return true;
        }

        if (_imageCache.HasCacheRoom(GetAlbumId(), AlbumImageCacheKind.FastThumbnail) &&
            TryTakeNearestWork(generation, AlbumImageWork.FastThumbnail, out var fastPhoto, out _, out var fastWorkToken))
        {
            await ExecuteThumbnailWorkAsync(fastPhoto, AlbumImageWork.FastThumbnail, isVisible: false, ownsOriginalLoad: false, fastThumbnailWasAlreadyLoaded: false, detailedThumbnailWasAlreadyLoaded: false, fastWorkToken, generation, cancellationToken);
            return true;
        }

        if (_imageCache.HasCacheRoom(GetAlbumId(), AlbumImageCacheKind.OriginalImage) &&
            TryTakeNearestWork(generation, AlbumImageWork.OriginalImage, out var originalPhoto, out _, out var originalWorkToken))
        {
            await ExecuteOriginalWarmupAsync(originalPhoto, originalWorkToken, generation, cancellationToken);
            return true;
        }

        if (_imageCache.HasCacheRoom(GetAlbumId(), AlbumImageCacheKind.OriginalImage) &&
            TryTakeFullImageDuplicateOriginalWarmup(generation, out var duplicateWarmupPhoto, out var duplicateWarmupToken))
        {
            await ExecuteFullImageOriginalWarmupAsync(duplicateWarmupPhoto, duplicateWarmupToken, generation, cancellationToken);
            return true;
        }

        return false;
    }

    private async Task<bool> TryRunNormalWorkAsync(long generation, CancellationToken cancellationToken)
    {
        if (!IsCurrentGeneration(generation))
        {
            return false;
        }

        if (TryTakeVisibleOrPriorityFastWork(generation, out var visibleFastPhoto, out var visibleFastWasAlreadyLoaded, out var visibleFastWorkToken))
        {
            await ExecuteThumbnailWorkAsync(
                visibleFastPhoto,
                AlbumImageWork.FastThumbnail,
                isVisible: true,
                ownsOriginalLoad: false,
                visibleFastWasAlreadyLoaded,
                detailedThumbnailWasAlreadyLoaded: false,
                visibleFastWorkToken,
                generation,
                cancellationToken);
            return true;
        }

        if (_imageCache.HasCacheRoom(GetAlbumId(), AlbumImageCacheKind.FastThumbnail) &&
            TryTakeNearestWork(generation, AlbumImageWork.FastThumbnail, out var fastPhoto, out _, out var fastWorkToken))
        {
            await ExecuteThumbnailWorkAsync(fastPhoto, AlbumImageWork.FastThumbnail, isVisible: false, ownsOriginalLoad: false, fastThumbnailWasAlreadyLoaded: false, detailedThumbnailWasAlreadyLoaded: false, fastWorkToken, generation, cancellationToken);
            return true;
        }

        if (TryTakeVisibleThumbnailWork(
            generation,
            preferDetailed: true,
            out var visiblePhoto,
            out var visibleWork,
            out var visibleOwnsOriginalLoad,
            out var visibleThumbnailFastWasAlreadyLoaded,
            out var visibleThumbnailDetailedWasAlreadyLoaded,
            out var visibleThumbnailWorkToken))
        {
            await ExecuteThumbnailWorkAsync(
                visiblePhoto,
                visibleWork,
                isVisible: true,
                visibleOwnsOriginalLoad,
                visibleThumbnailFastWasAlreadyLoaded,
                visibleThumbnailDetailedWasAlreadyLoaded,
                visibleThumbnailWorkToken,
                generation,
                cancellationToken);
            return true;
        }

        if (_imageCache.HasCacheRoom(GetAlbumId(), AlbumImageCacheKind.DetailedThumbnail) &&
            TryTakeNearestWork(generation, AlbumImageWork.DetailedThumbnail, out var detailedPhoto, out var detailedOwnsOriginalLoad, out var detailedWorkToken))
        {
            await ExecuteThumbnailWorkAsync(detailedPhoto, AlbumImageWork.DetailedThumbnail, isVisible: false, detailedOwnsOriginalLoad, fastThumbnailWasAlreadyLoaded: false, detailedThumbnailWasAlreadyLoaded: false, detailedWorkToken, generation, cancellationToken);
            return true;
        }

        if (_imageCache.HasCacheRoom(GetAlbumId(), AlbumImageCacheKind.OriginalImage) &&
            TryTakeNearestWork(generation, AlbumImageWork.OriginalImage, out var originalPhoto, out _, out var originalWorkToken))
        {
            await ExecuteOriginalWarmupAsync(originalPhoto, originalWorkToken, generation, cancellationToken);
            return true;
        }

        return false;
    }

    private async Task<bool> TryRunLightWorkAsync(long generation, CancellationToken cancellationToken)
    {
        if (!IsCurrentGeneration(generation))
        {
            return false;
        }

        if (TryTakeVisibleOrPriorityFastWork(generation, out var visibleFastPhoto, out var visibleFastWasAlreadyLoaded, out var visibleFastWorkToken))
        {
            await ExecuteThumbnailWorkAsync(
                visibleFastPhoto,
                AlbumImageWork.FastThumbnail,
                isVisible: true,
                ownsOriginalLoad: false,
                visibleFastWasAlreadyLoaded,
                detailedThumbnailWasAlreadyLoaded: false,
                visibleFastWorkToken,
                generation,
                cancellationToken);
            return true;
        }

        if (_imageCache.HasCacheRoom(GetAlbumId(), AlbumImageCacheKind.FastThumbnail) &&
            TryTakeNearestWork(generation, AlbumImageWork.FastThumbnail, out var fastPhoto, out _, out var fastWorkToken))
        {
            await ExecuteThumbnailWorkAsync(fastPhoto, AlbumImageWork.FastThumbnail, isVisible: false, ownsOriginalLoad: false, fastThumbnailWasAlreadyLoaded: false, detailedThumbnailWasAlreadyLoaded: false, fastWorkToken, generation, cancellationToken);
            return true;
        }

        return false;
    }

    private bool TryTakeVisibleFastWork(long generation, out AlbumPhotoViewModel photo, out bool fastThumbnailWasAlreadyLoaded, out int workToken)
    {
        return TryTakeFastWorkFromPhotos(
            GetListViewportPhotos(),
            generation,
            requireListViewport: true,
            out photo,
            out fastThumbnailWasAlreadyLoaded,
            out workToken);
    }

    private bool TryTakeVisibleOrPriorityFastWork(long generation, out AlbumPhotoViewModel photo, out bool fastThumbnailWasAlreadyLoaded, out int workToken)
    {
        return TryTakeFastWorkFromPhotos(
            GetViewportPhotos(),
            generation,
            requireListViewport: false,
            out photo,
            out fastThumbnailWasAlreadyLoaded,
            out workToken);
    }

    private bool TryTakeFastWorkFromPhotos(
        IReadOnlyList<AlbumPhotoViewModel> photos,
        long generation,
        bool requireListViewport,
        out AlbumPhotoViewModel photo,
        out bool fastThumbnailWasAlreadyLoaded,
        out int workToken)
    {
        foreach (var candidate in photos)
        {
            if (IsCurrentGeneration(generation) &&
                candidate.Image is null &&
                !IsVisibleFailureSkipped(candidate, AlbumImageWork.FastThumbnail) &&
                candidate.TryBeginFastThumbnailControlLoad(out fastThumbnailWasAlreadyLoaded, out workToken))
            {
                if (!IsCurrentGeneration(generation))
                {
                    candidate.CompleteFastThumbnailLoad(fastThumbnailWasAlreadyLoaded, workToken);
                    continue;
                }

                var isStillEligible = requireListViewport
                    ? IsPhotoInListViewport(candidate)
                    : IsPhotoInViewport(candidate);
                if (!isStillEligible)
                {
                    candidate.CompleteFastThumbnailLoad(fastThumbnailWasAlreadyLoaded, workToken);
                    continue;
                }

                photo = candidate;
                return true;
            }
        }

        photo = null!;
        fastThumbnailWasAlreadyLoaded = false;
        workToken = 0;
        return false;
    }

    private bool TryTakeVisibleThumbnailWork(
        long generation,
        bool preferDetailed,
        out AlbumPhotoViewModel photo,
        out AlbumImageWork work,
        out bool ownsOriginalLoad,
        out bool fastThumbnailWasAlreadyLoaded,
        out bool detailedThumbnailWasAlreadyLoaded,
        out int workToken)
    {
        foreach (var candidate in GetViewportPhotos())
        {
            var allowFastThumbnail = !IsVisibleFailureSkipped(candidate, AlbumImageWork.FastThumbnail);
            if (IsCurrentGeneration(generation) && candidate.TryBeginVisibleThumbnailLoad(
                candidate.Image is null,
                preferDetailed,
                candidate.IsFullImageLoaded,
                out var loadDetailed,
                out ownsOriginalLoad,
                out fastThumbnailWasAlreadyLoaded,
                out detailedThumbnailWasAlreadyLoaded,
                out workToken,
                allowFastThumbnail))
            {
                var selectedWork = loadDetailed ? AlbumImageWork.DetailedThumbnail : AlbumImageWork.FastThumbnail;
                if (IsVisibleFailureSkipped(candidate, selectedWork))
                {
                    if (loadDetailed)
                    {
                        candidate.CompleteDetailedThumbnailLoad(
                            loaded: detailedThumbnailWasAlreadyLoaded,
                            originalLoaded: false,
                            ownsOriginalLoad,
                            workToken);
                    }
                    else
                    {
                        candidate.CompleteFastThumbnailLoad(fastThumbnailWasAlreadyLoaded, workToken);
                    }

                    continue;
                }

                if (!IsCurrentGeneration(generation))
                {
                    if (loadDetailed)
                    {
                        candidate.CompleteDetailedThumbnailLoad(
                            loaded: detailedThumbnailWasAlreadyLoaded,
                            originalLoaded: false,
                            ownsOriginalLoad,
                            workToken);
                    }
                    else
                    {
                        candidate.CompleteFastThumbnailLoad(fastThumbnailWasAlreadyLoaded, workToken);
                    }

                    continue;
                }

                if (!IsPhotoInViewport(candidate))
                {
                    if (loadDetailed)
                    {
                        candidate.CompleteDetailedThumbnailLoad(
                            loaded: detailedThumbnailWasAlreadyLoaded,
                            originalLoaded: false,
                            ownsOriginalLoad,
                            workToken);
                    }
                    else
                    {
                        candidate.CompleteFastThumbnailLoad(fastThumbnailWasAlreadyLoaded, workToken);
                    }

                    continue;
                }

                photo = candidate;
                work = selectedWork;
                return true;
            }
        }

        photo = null!;
        work = AlbumImageWork.FastThumbnail;
        ownsOriginalLoad = false;
        fastThumbnailWasAlreadyLoaded = false;
        detailedThumbnailWasAlreadyLoaded = false;
        workToken = 0;
        return false;
    }

    private bool TryTakeNearestWork(long generation, AlbumImageWork work, out AlbumPhotoViewModel photo, out bool ownsOriginalLoad, out int workToken)
    {
        ownsOriginalLoad = false;
        workToken = 0;
        lock (_sync)
        {
            var seedIndices = _orderedViewportIndices.Count > 0
                ? _orderedViewportIndices
                : _orderedPriorityIndices;
            if (_photos.Count > 0 && seedIndices.Count > 0)
            {
                HashSet<int> yielded = new();
                foreach (var index in seedIndices)
                {
                    if (TryTakeNearestWorkCandidateNoLock(index, generation, work, yielded, out photo, ref ownsOriginalLoad, out workToken))
                    {
                        return true;
                    }
                }

                for (var distance = 1; distance < _photos.Count; distance++)
                {
                    var yieldedAny = false;
                    foreach (var seedIndex in seedIndices)
                    {
                        var down = seedIndex + distance;
                        yieldedAny |= down >= 0 && down < _photos.Count;
                        if (TryTakeNearestWorkCandidateNoLock(down, generation, work, yielded, out photo, ref ownsOriginalLoad, out workToken))
                        {
                            return true;
                        }

                        var up = seedIndex - distance;
                        yieldedAny |= up >= 0 && up < _photos.Count;
                        if (TryTakeNearestWorkCandidateNoLock(up, generation, work, yielded, out photo, ref ownsOriginalLoad, out workToken))
                        {
                            return true;
                        }
                    }

                    if (!yieldedAny)
                    {
                        break;
                    }
                }
            }
        }

        photo = null!;
        ownsOriginalLoad = false;
        workToken = 0;
        return false;
    }

    private bool TryTakeNearestWorkCandidateNoLock(
        int index,
        long generation,
        AlbumImageWork work,
        HashSet<int> yielded,
        out AlbumPhotoViewModel photo,
        ref bool ownsOriginalLoad,
        out int workToken)
    {
        photo = null!;
        workToken = 0;
        if (index < 0 || index >= _photos.Count || !yielded.Add(index))
        {
            return false;
        }

        var candidate = _photos[index];
        if (work == AlbumImageWork.FastThumbnail &&
            candidate.FastThumbnailStatus == AlbumImageItemStatus.Unloaded &&
            IsCurrentGeneration(generation) &&
            !IsWarmupSkipped(candidate, work) &&
            !IsVisibleFailureSkipped(candidate, work) &&
            candidate.TryBeginFastThumbnailLoad(out workToken))
        {
            if (!IsCurrentGeneration(generation))
            {
                candidate.CompleteFastThumbnailLoad(loaded: false, workToken);
                return false;
            }

            photo = candidate;
            return true;
        }

        if (work == AlbumImageWork.DetailedThumbnail &&
            candidate.DetailedThumbnailStatus == AlbumImageItemStatus.Unloaded &&
            IsCurrentGeneration(generation) &&
            !IsWarmupSkipped(candidate, work) &&
            !IsVisibleFailureSkipped(candidate, work) &&
            candidate.TryBeginDetailedThumbnailLoad(out ownsOriginalLoad, out workToken))
        {
            if (!IsCurrentGeneration(generation))
            {
                candidate.CompleteDetailedThumbnailLoad(
                    loaded: false,
                    originalLoaded: false,
                    ownsOriginalLoad,
                    workToken);
                return false;
            }

            photo = candidate;
            return true;
        }

        if (work == AlbumImageWork.OriginalImage &&
            candidate.OriginalImageStatus == AlbumImageItemStatus.Unloaded &&
            IsCurrentGeneration(generation) &&
            !IsWarmupSkipped(candidate, work) &&
            candidate.TryBeginOriginalImageLoad(out workToken))
        {
            if (!IsCurrentGeneration(generation))
            {
                candidate.CompleteOriginalImageLoad(loaded: false, workToken);
                return false;
            }

            photo = candidate;
            return true;
        }

        return false;
    }

    private async Task ExecuteThumbnailWorkAsync(
        AlbumPhotoViewModel photo,
        AlbumImageWork work,
        bool isVisible,
        bool ownsOriginalLoad,
        bool fastThumbnailWasAlreadyLoaded,
        bool detailedThumbnailWasAlreadyLoaded,
        int workToken,
        long generation,
        CancellationToken cancellationToken)
    {
        MarkActiveWork(photo, work, isVisible);
        try
        {
            if (isVisible)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (IsCurrentGeneration(generation) && IsPhotoInViewport(photo))
                    {
                        photo.IsImageLoading = true;
                    }
                });
            }

            if (work == AlbumImageWork.FastThumbnail)
            {
                if (isVisible && await TryLoadCachedDetailedThumbnailForFastWorkAsync(
                    photo,
                    fastThumbnailWasAlreadyLoaded,
                    workToken,
                    generation,
                    cancellationToken))
                {
                    return;
                }

                if (!isVisible)
                {
                    var fastThumbnailLoaded = await _imageCache.WarmFastThumbnailAsync(
                        photo.AlbumId,
                        photo.PhotoId,
                        photo.ThumbnailDownloadUrl,
                        _httpClient,
                        AlbumImageCacheReadMode.Lazy,
                        cancellationToken);
                    if (IsCurrentGeneration(generation))
                    {
                        if (!fastThumbnailLoaded)
                        {
                            MarkWarmupSkipped(photo, AlbumImageWork.FastThumbnail);
                        }
                    }

                    photo.CompleteFastThumbnailLoad(
                        fastThumbnailLoaded && !NeedsVisibleControlReload(photo, generation),
                        workToken);
                    return;
                }

                var fastResult = await _imageCache.LoadFastThumbnailBitmapAsync(
                    photo.AlbumId,
                    photo.PhotoId,
                    photo.ThumbnailDownloadUrl,
                    _httpClient,
                    isVisible ? AlbumImageCacheReadMode.Eager : AlbumImageCacheReadMode.Lazy,
                    cancellationToken);
                var boundToControl = false;
                if (isVisible)
                {
                    boundToControl = await SetLoadedImageIfStillVisibleAsync(
                        photo,
                        fastResult.Bitmap,
                        isDetailedThumbnail: false,
                        "Loading detailed image",
                        workToken,
                        generation);
                }
                else
                {
                    fastResult.Bitmap.Dispose();
                }

                photo.CompleteFastThumbnailLoad(boundToControl, workToken);

                return;
            }

            if (!isVisible)
            {
                var warmResult = await _imageCache.WarmDetailedThumbnailAsync(
                    photo.AlbumId,
                    photo.PhotoId,
                    $"{photo.PhotoId}-full{photo.FileExtension}",
                    photo.DownloadUrl,
                    _httpClient,
                    _detailedThumbnailPixelWidth,
                    _detailedThumbnailPixelHeight,
                    AlbumImageCacheReadMode.Lazy,
                    AlbumImageCacheReadMode.Lazy,
                    cancellationToken);
                if (IsCurrentGeneration(generation))
                {
                    if (!warmResult.DetailedThumbnailLoaded)
                    {
                        MarkWarmupSkipped(photo, AlbumImageWork.DetailedThumbnail);
                    }

                    if (ownsOriginalLoad && !warmResult.OriginalImageLoaded)
                    {
                        MarkWarmupSkipped(photo, AlbumImageWork.OriginalImage);
                    }
                }

                photo.CompleteDetailedThumbnailLoad(
                    loaded: warmResult.DetailedThumbnailLoaded && !NeedsVisibleControlReload(photo, generation),
                    originalLoaded: warmResult.OriginalImageLoaded,
                    ownsOriginalLoad,
                    workToken);
                return;
            }

            var detailedResult = await _imageCache.LoadDetailedThumbnailBitmapAsync(
                photo.AlbumId,
                photo.PhotoId,
                $"{photo.PhotoId}-full{photo.FileExtension}",
                photo.DownloadUrl,
                _httpClient,
                _detailedThumbnailPixelWidth,
                _detailedThumbnailPixelHeight,
                isVisible ? AlbumImageCacheReadMode.Eager : AlbumImageCacheReadMode.Lazy,
                AlbumImageCacheReadMode.Lazy,
                cancellationToken);
            if (isVisible)
            {
                var boundToControl = await SetLoadedImageIfStillVisibleAsync(
                    photo,
                    detailedResult.Bitmap,
                    isDetailedThumbnail: true,
                    "Detailed image loaded",
                    workToken,
                    generation);
                photo.CompleteDetailedThumbnailLoad(
                    loaded: boundToControl,
                    detailedResult.OriginalImageLoaded,
                    ownsOriginalLoad,
                    workToken);
            }
            else
            {
                detailedResult.Bitmap.Dispose();
                photo.CompleteDetailedThumbnailLoad(
                    loaded: detailedResult.DetailedThumbnailLoaded,
                    detailedResult.OriginalImageLoaded,
                    ownsOriginalLoad,
                    workToken);
            }
        }
        catch (OperationCanceledException)
        {
            ResetWorkStatus(photo, work, ownsOriginalLoad, fastThumbnailWasAlreadyLoaded, detailedThumbnailWasAlreadyLoaded, workToken);
        }
        catch (AlbumImageCacheEntryInvalidException ex)
        {
            if (IsCurrentGeneration(generation))
            {
                photo.InvalidateImageCacheStatus(ex.Kind);
                if (isVisible)
                {
                    LogImageLoadFailure(photo, work, isVisible, ex);
                    MarkVisibleFailureSkippedIfCurrentVisible(photo, work, workToken);
                }

                ResetWorkStatus(
                    photo,
                    work,
                    ownsOriginalLoad,
                    ex.Kind != AlbumImageCacheKind.FastThumbnail && fastThumbnailWasAlreadyLoaded,
                    ex.Kind != AlbumImageCacheKind.DetailedThumbnail && detailedThumbnailWasAlreadyLoaded,
                    workToken);
            }
            else
            {
                ResetWorkStatus(photo, work, ownsOriginalLoad, fastThumbnailWasAlreadyLoaded, detailedThumbnailWasAlreadyLoaded, workToken);
            }

            if (isVisible)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (IsCurrentGeneration(generation) && photo.IsImageWorkCurrent(workToken) && IsPhotoInViewport(photo))
                    {
                        photo.Status = ex.InnerException?.Message ?? ex.Message;
                    }
                });
            }
        }
        catch (Exception ex)
        {
            if (IsCurrentGeneration(generation))
            {
                LogImageLoadFailure(photo, work, isVisible, ex);
                if (isVisible)
                {
                    MarkVisibleFailureSkippedIfCurrentVisible(photo, work, workToken);
                }
                else
                {
                    MarkWarmupSkipped(photo, work);
                    if (work == AlbumImageWork.DetailedThumbnail && ownsOriginalLoad)
                    {
                        MarkWarmupSkipped(photo, AlbumImageWork.OriginalImage);
                    }
                }
            }

            ResetWorkStatus(photo, work, ownsOriginalLoad, fastThumbnailWasAlreadyLoaded, detailedThumbnailWasAlreadyLoaded, workToken);

            if (isVisible)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (IsCurrentGeneration(generation) && photo.IsImageWorkCurrent(workToken) && IsPhotoInViewport(photo))
                    {
                        photo.Status = ex.Message;
                    }
                });
            }
        }
        finally
        {
            ClearActiveWork(photo);
            if (isVisible)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (IsCurrentGeneration(generation) && !photo.IsImageLoadBusyAtomic())
                    {
                        photo.IsImageLoading = false;
                    }
                });
            }

            SignalWorkers();
        }
    }

    private static void LogImageLoadFailure(AlbumPhotoViewModel photo, AlbumImageWork work, bool isVisible, Exception ex)
    {
        Console.WriteLine(
            $"PicshareImageLoader: {(TransientRetryPolicy.IsTransient(ex, CancellationToken.None) ? "warning" : "BUG")} {work} {(isVisible ? "visible" : "warmup")} failed for {photo.FileName} ({photo.PhotoId}): {ex.GetType().Name}: {ex.Message}");
        Console.WriteLine($"PicshareImageLoader: exception detail: {ex}");
    }

    private async Task<bool> TryLoadCachedDetailedThumbnailForFastWorkAsync(
        AlbumPhotoViewModel photo,
        bool fastThumbnailWasAlreadyLoaded,
        int workToken,
        long generation,
        CancellationToken cancellationToken)
    {
        var detailedThumbnailWasLoaded = photo.DetailedThumbnailStatus == AlbumImageItemStatus.Loaded;
        if (photo.DetailedThumbnailStatus is not (AlbumImageItemStatus.Unloaded or AlbumImageItemStatus.Loaded))
        {
            return false;
        }

        try
        {
            var result = await _imageCache.LoadDetailedThumbnailBitmapAsync(
                photo.AlbumId,
                photo.PhotoId,
                $"{photo.PhotoId}-full{photo.FileExtension}",
                photo.DownloadUrl,
                _httpClient,
                _detailedThumbnailPixelWidth,
                _detailedThumbnailPixelHeight,
                AlbumImageCacheReadMode.Lookup,
                AlbumImageCacheReadMode.Lookup,
                cancellationToken);

            var boundToControl = await SetLoadedImageIfStillVisibleAsync(
                photo,
                result.Bitmap,
                isDetailedThumbnail: true,
                "Detailed image loaded",
                workToken,
                generation);
            if (!boundToControl)
            {
                return false;
            }

            photo.CompleteFastThumbnailLoadWithDetailedThumbnail(fastThumbnailWasAlreadyLoaded, workToken);

            return true;
        }
        catch (FileNotFoundException)
        {
            if (IsCurrentGeneration(generation) && detailedThumbnailWasLoaded)
            {
                photo.InvalidateDetailedThumbnailStatus();
            }

            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AlbumImageCacheEntryInvalidException ex)
        {
            if (IsCurrentGeneration(generation))
            {
                photo.InvalidateImageCacheStatus(ex.Kind);
            }

            return false;
        }
        catch
        {
            if (IsCurrentGeneration(generation))
            {
                photo.InvalidateDetailedThumbnailStatus();
            }

            return false;
        }
    }

    private async Task ExecuteOriginalWarmupAsync(AlbumPhotoViewModel photo, int workToken, long generation, CancellationToken cancellationToken)
    {
        MarkActiveWork(photo, AlbumImageWork.OriginalImage, isVisible: false);
        try
        {
            var originalLoaded = await _imageCache.WarmOriginalAsync(
                photo.AlbumId,
                $"{photo.PhotoId}-full{photo.FileExtension}",
                photo.DownloadUrl,
                _httpClient,
                AlbumImageCacheReadMode.Lazy,
                cancellationToken);
            if (IsCurrentGeneration(generation))
            {
                if (!originalLoaded)
                {
                    MarkWarmupSkipped(photo, AlbumImageWork.OriginalImage);
                }
            }

            photo.CompleteOriginalImageLoad(originalLoaded, workToken);
        }
        catch (OperationCanceledException)
        {
            photo.CompleteOriginalImageLoad(loaded: false, workToken);
        }
        catch (Exception ex)
        {
            if (IsCurrentGeneration(generation))
            {
                LogImageLoadFailure(photo, AlbumImageWork.OriginalImage, isVisible: false, ex);
                MarkWarmupSkipped(photo, AlbumImageWork.OriginalImage);
            }

            photo.CompleteOriginalImageLoad(loaded: false, workToken);
        }
        finally
        {
            SignalWorkers();
            ClearActiveWork(photo);
        }
    }

    private async Task ExecuteFullImageOriginalWarmupAsync(
        AlbumPhotoViewModel photo,
        int workToken,
        long generation,
        CancellationToken cancellationToken)
    {
        MarkActiveWork(photo, AlbumImageWork.OriginalImage, isVisible: false);
        try
        {
            var originalLoaded = await _imageCache.WarmOriginalAsync(
                photo.AlbumId,
                GetOriginalCacheFileName(photo),
                photo.DownloadUrl,
                _httpClient,
                AlbumImageCacheReadMode.Eager,
                cachePriority: 1,
                cancellationToken);
            lock (_sync)
            {
                _attemptedFullImageWarmupPhotos.Add(photo);
                _fullImageWarmupQueue.Remove(photo);
            }

            photo.CompleteOriginalImageLoad(originalLoaded, workToken);
        }
        catch (OperationCanceledException)
        {
            photo.CompleteOriginalImageLoad(loaded: false, workToken);
        }
        catch (Exception ex)
        {
            if (IsCurrentGeneration(generation))
            {
                LogImageLoadFailure(photo, AlbumImageWork.OriginalImage, isVisible: false, ex);
            }

            lock (_sync)
            {
                _attemptedFullImageWarmupPhotos.Add(photo);
                _fullImageWarmupQueue.Remove(photo);
            }

            photo.CompleteOriginalImageLoad(loaded: false, workToken);
        }
        finally
        {
            SignalWorkers();
            ClearActiveWork(photo);
        }
    }

    private static string GetOriginalCacheFileName(AlbumPhotoViewModel photo)
    {
        return $"{photo.PhotoId}-full{photo.FileExtension}";
    }

    private static void ResetWorkStatus(
        AlbumPhotoViewModel photo,
        AlbumImageWork work,
        bool ownsOriginalLoad,
        bool fastThumbnailWasAlreadyLoaded,
        bool detailedThumbnailWasAlreadyLoaded,
        int workToken)
    {
        if (work == AlbumImageWork.FastThumbnail)
        {
            photo.CompleteFastThumbnailLoad(fastThumbnailWasAlreadyLoaded, workToken);
            return;
        }

        if (work == AlbumImageWork.DetailedThumbnail)
        {
            photo.CompleteDetailedThumbnailLoad(loaded: detailedThumbnailWasAlreadyLoaded, originalLoaded: false, ownsOriginalLoad, workToken);
            return;
        }

        photo.CompleteOriginalImageLoad(loaded: false, workToken);
    }

    private async Task<bool> SetLoadedImageIfStillVisibleAsync(
        AlbumPhotoViewModel photo,
        AlbumImageBitmapLease bitmap,
        bool isDetailedThumbnail,
        string status,
        int workToken,
        long generation)
    {
        var wasBound = false;
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsCurrentGeneration(generation) || !photo.IsImageWorkCurrent(workToken) || !IsPhotoInViewport(photo))
                {
                    return;
                }

                photo.SetLoadedImage(bitmap, isDetailedThumbnail, status);
                wasBound = true;
            });
        }
        finally
        {
            if (!wasBound)
            {
                bitmap.Dispose();
            }
        }

        return wasBound;
    }

    private bool IsPhotoInViewport(AlbumPhotoViewModel photo)
    {
        lock (_sync)
        {
            return _indexByPhoto.TryGetValue(photo, out var index) &&
                (_viewportIndices.Contains(index) || _priorityIndices.Contains(index));
        }
    }

    private bool NeedsVisibleControlReload(AlbumPhotoViewModel photo, long generation)
    {
        return IsCurrentGeneration(generation) && photo.Image is null && IsPhotoInViewport(photo);
    }

    private bool IsPhotoInListViewport(AlbumPhotoViewModel photo)
    {
        lock (_sync)
        {
            return _indexByPhoto.TryGetValue(photo, out var index) &&
                _viewportIndices.Contains(index);
        }
    }

    private bool IsWarmupSkipped(AlbumPhotoViewModel photo, AlbumImageWork work)
    {
        lock (_sync)
        {
            return _indexByPhoto.TryGetValue(photo, out var index) &&
                _skippedWarmupKeys.Contains(new AlbumImageWarmupKey(index, work));
        }
    }

    private bool IsVisibleFailureSkipped(AlbumPhotoViewModel photo, AlbumImageWork work)
    {
        lock (_sync)
        {
            return _indexByPhoto.TryGetValue(photo, out var index) &&
                _failedVisibleKeys.Contains(new AlbumImageWarmupKey(index, work));
        }
    }

    private void MarkWarmupSkipped(AlbumPhotoViewModel photo, AlbumImageWork work)
    {
        lock (_sync)
        {
            if (_indexByPhoto.TryGetValue(photo, out var index))
            {
                _skippedWarmupKeys.Add(new AlbumImageWarmupKey(index, work));
            }
        }
    }

    private void MarkVisibleFailureSkipped(AlbumPhotoViewModel photo, AlbumImageWork work)
    {
        lock (_sync)
        {
            if (_indexByPhoto.TryGetValue(photo, out var index))
            {
                _failedVisibleKeys.Add(new AlbumImageWarmupKey(index, work));
            }
        }
    }

    private void MarkVisibleFailureSkippedIfCurrentVisible(AlbumPhotoViewModel photo, AlbumImageWork work, int workToken)
    {
        lock (_sync)
        {
            if (photo.IsImageWorkCurrent(workToken) &&
                _indexByPhoto.TryGetValue(photo, out var index) &&
                (_viewportIndices.Contains(index) || _priorityIndices.Contains(index)))
            {
                _failedVisibleKeys.Add(new AlbumImageWarmupKey(index, work));
            }
        }
    }

    private void ClearVisibleFailureSkippedNoLock(int index)
    {
        _failedVisibleKeys.Remove(new AlbumImageWarmupKey(index, AlbumImageWork.FastThumbnail));
        _failedVisibleKeys.Remove(new AlbumImageWarmupKey(index, AlbumImageWork.DetailedThumbnail));
        _failedVisibleKeys.Remove(new AlbumImageWarmupKey(index, AlbumImageWork.OriginalImage));
    }

    private void MarkActiveWork(AlbumPhotoViewModel photo, AlbumImageWork work, bool isVisible)
    {
        lock (_sync)
        {
            if (_indexByPhoto.TryGetValue(photo, out var index))
            {
                _activeImageWorks[index] = new ActiveImageWork(work, isVisible, DateTime.UtcNow);
            }
        }
    }

    private void ClearActiveWork(AlbumPhotoViewModel photo)
    {
        lock (_sync)
        {
            if (_indexByPhoto.TryGetValue(photo, out var index))
            {
                _activeImageWorks.Remove(index);
            }
        }
    }

    private IReadOnlyList<AlbumPhotoViewModel> GetViewportPhotos()
    {
        lock (_sync)
        {
            return _orderedPriorityIndices
                .Concat(_orderedViewportIndices)
                .Distinct()
                .Select(index => index >= 0 && index < _photos.Count ? _photos[index] : null)
                .Where(photo => photo is not null)
                .Cast<AlbumPhotoViewModel>()
                .ToList();
        }
    }

    private IReadOnlyList<AlbumPhotoViewModel> GetListViewportPhotos()
    {
        lock (_sync)
        {
            return _orderedViewportIndices
                .Select(index => index >= 0 && index < _photos.Count ? _photos[index] : null)
                .Where(photo => photo is not null)
                .Cast<AlbumPhotoViewModel>()
                .ToList();
        }
    }

    private IReadOnlyList<int> GetListViewportIndicesForRestoreNoLock()
    {
        List<int> indices = new();
        foreach (var index in _orderedViewportIndices)
        {
            if (index < 0 || index >= _photos.Count)
            {
                continue;
            }

            _viewportSnapshotIndexCounts.TryGetValue(index, out var snapshotCount);
            for (var position = 0; position < snapshotCount; position++)
            {
                indices.Add(index);
            }
        }

        return indices;
    }

    private IReadOnlyList<int> GetListViewportSnapshotIndicesForRestoreNoLock()
    {
        List<int> indices = new();
        foreach (var index in _orderedViewportIndices)
        {
            if (index < 0 || index >= _photos.Count)
            {
                continue;
            }

            _viewportSnapshotIndexCounts.TryGetValue(index, out var snapshotCount);
            for (var position = 0; position < snapshotCount; position++)
            {
                indices.Add(index);
            }
        }

        return indices;
    }

    private IReadOnlyList<LoadedViewportRegistration> GetLoadedViewportRegistrationsForRestoreNoLock()
    {
        return _viewportLoadedRegistrations
            .SelectMany(item => item.Value.Select(indices => new LoadedViewportRegistration(item.Key, indices.ToArray())))
            .ToList();
    }

    private string GetAlbumId()
    {
        lock (_sync)
        {
            return _photos.FirstOrDefault()?.AlbumId ?? "";
        }
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

    private readonly record struct AlbumImageWarmupKey(int Index, AlbumImageWork Work);

    private readonly record struct LoadedViewportRegistration(AlbumPhotoViewModel Photo, int[] Indices);

    private readonly record struct ActiveImageWork(AlbumImageWork Work, bool IsVisible, DateTime StartedUtc);
}
