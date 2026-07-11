using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Picshare.Services;

namespace Picshare.ViewModels;

public partial class AlbumPhotoViewModel : ObservableObject
{
    private const int ThumbnailPixelSize = 64;
    private const int DisplayPixelWidth = 220;
    private const int DisplayPixelHeight = 150;
    private const int MaxConcurrentThumbnailLoads = 4;
    private static readonly object ThumbnailLoadQueueSync = new();
    private static readonly Dictionary<AlbumPhotoViewModel, ThumbnailLoadRequest> PendingThumbnailLoads = new();
    private static readonly HashSet<AlbumPhotoViewModel> PrioritizedThumbnailPhotos = new();
    private static int _activeThumbnailLoadCount;
    private static long _thumbnailLoadSequence;
    private static readonly object DisplayImageLoadQueueSync = new();
    private static readonly Dictionary<AlbumPhotoViewModel, DisplayImageLoadRequest> PendingDisplayImageLoads = new();
    private static readonly HashSet<AlbumPhotoViewModel> PrioritizedDisplayImagePhotos = new();
    private static bool _isDisplayImageLoadActive;
    private static long _displayImageLoadSequence;

    public AlbumPhotoViewModel(
        string albumId,
        string photoId,
        string fileName,
        string downloadUrl,
        string thumbnailDownloadUrl)
    {
        AlbumId = albumId;
        PhotoId = photoId;
        FileName = fileName;
        DownloadUrl = downloadUrl;
        ThumbnailDownloadUrl = thumbnailDownloadUrl;
    }

    public string AlbumId { get; }

    public string PhotoId { get; }

    public string FileName { get; }

    public string DownloadUrl { get; }

    public string ThumbnailDownloadUrl { get; }

    public string FileExtension
    {
        get
        {
            var extension = Path.GetExtension(FileName);
            return string.IsNullOrWhiteSpace(extension) ? ".img" : extension;
        }
    }

    [ObservableProperty]
    private Bitmap? _image;

    [ObservableProperty]
    private double _cardWidth = DisplayPixelWidth;

    [ObservableProperty]
    private double _cardHeight = DisplayPixelHeight + 40;

    private AlbumImageBitmapLease? _imageLease;
    private bool _isApplyingImageLease;

    [ObservableProperty]
    private string _status = "Loading";

    [ObservableProperty]
    private bool _isFullImageLoaded;

    [ObservableProperty]
    private bool _isSelectedForViewing;

    [ObservableProperty]
    private string _category = "";

    [ObservableProperty]
    private bool _isFrozen;

    [ObservableProperty]
    private int _rotationDegrees;

    [ObservableProperty]
    private int _score;

    [ObservableProperty]
    private bool _isSelectedForBulk;

    [ObservableProperty]
    private bool _isDuplicateGroupMain;

    [ObservableProperty]
    private string _duplicateGroupId = "";

    [ObservableProperty]
    private int _duplicateGroupCount;

    [ObservableProperty]
    private AlbumPhotoViewModel? _duplicateStackPhoto;

    [ObservableProperty]
    private bool _isBestInDuplicateGroup;

    [ObservableProperty]
    private bool _isImageLoading;

    private int _imageLoadStatusBits;
    private int _untokenedImageWorkToken;

    public bool HasDuplicateGroup => IsDuplicateGroupMain && DuplicateGroupCount > 1;

    public string DuplicateGroupCountText => DuplicateGroupCount > 1 ? DuplicateGroupCount.ToString() : "";

    public bool IsBestMarkerVisible => IsBestInDuplicateGroup;

    public bool IsScoreVisible => Score > 0;

    public string ScoreText => Score.ToString();

    public IBrush CardBorderBrush => IsSelectedForViewing
        ? Brushes.DeepSkyBlue
        : IsSelectedForBulk
            ? Brushes.ForestGreen
        : new SolidColorBrush(Color.Parse("#D6D8D1"));

    public Thickness CardBorderThickness => IsSelectedForViewing
        ? new Thickness(3)
        : IsSelectedForBulk
            ? new Thickness(3)
        : new Thickness(1);

    public string SelectionGlyph => IsSelectedForBulk ? "On" : "+";

    public IBrush SelectionBackground => IsSelectedForBulk ? Brushes.ForestGreen : Brushes.White;

    public IBrush SelectionForeground => IsSelectedForBulk ? Brushes.White : Brushes.Black;

    private CancellationTokenSource? _loadCancellation;
    private int _thumbnailPriorityRank = int.MaxValue;
    private int _displayImagePriorityRank = int.MaxValue;

    public AlbumImageItemStatus FastThumbnailStatus => GetImageItemStatus(ImageLoadStatusField.FastThumbnail);

    public AlbumImageItemStatus OriginalImageStatus => GetImageItemStatus(ImageLoadStatusField.OriginalImage);

    public AlbumImageItemStatus DetailedThumbnailStatus => GetImageItemStatus(ImageLoadStatusField.DetailedThumbnail);

    public bool HasAnyThumbnailLoadedAtomic()
    {
        var bits = Volatile.Read(ref _imageLoadStatusBits);
        return GetImageItemStatus(bits, ImageLoadStatusField.FastThumbnail) == AlbumImageItemStatus.Loaded ||
            GetImageItemStatus(bits, ImageLoadStatusField.DetailedThumbnail) == AlbumImageItemStatus.Loaded;
    }

    public bool IsImageLoadBusyAtomic()
    {
        return IsImageItemBusy(Volatile.Read(ref _imageLoadStatusBits));
    }

    public bool IsImageWorkCurrent(int workToken)
    {
        var current = Volatile.Read(ref _imageLoadStatusBits);
        return !IsImageItemResetRequested(current) && IsImageItemWorkTokenMatch(current, workToken);
    }

    public bool TryBeginFastThumbnailLoad()
    {
        if (TryBeginFastThumbnailLoad(out var workToken))
        {
            Volatile.Write(ref _untokenedImageWorkToken, workToken);
            return true;
        }

        return false;
    }

    public bool TryBeginFastThumbnailLoad(out int workToken)
    {
        return TrySetStatusAndLockItem(ImageLoadStatusField.FastThumbnail, AlbumImageItemStatus.Unloaded, AlbumImageItemStatus.Loading, out workToken);
    }

    public bool TryBeginFastThumbnailControlLoad(out bool wasLoaded)
    {
        if (TryBeginFastThumbnailControlLoad(out wasLoaded, out var workToken))
        {
            Volatile.Write(ref _untokenedImageWorkToken, workToken);
            return true;
        }

        return false;
    }

    public bool TryBeginFastThumbnailControlLoad(out bool wasLoaded, out int workToken)
    {
        wasLoaded = false;
        if (TrySetStatusAndLockItem(ImageLoadStatusField.FastThumbnail, AlbumImageItemStatus.Unloaded, AlbumImageItemStatus.Loading, out workToken))
        {
            return true;
        }

        if (TrySetStatusAndLockItem(ImageLoadStatusField.FastThumbnail, AlbumImageItemStatus.Loaded, AlbumImageItemStatus.Loading, out workToken))
        {
            wasLoaded = true;
            return true;
        }

        workToken = 0;
        return false;
    }

    public bool TryBeginOriginalImageLoad()
    {
        if (TryBeginOriginalImageLoad(out var workToken))
        {
            Volatile.Write(ref _untokenedImageWorkToken, workToken);
            return true;
        }

        return false;
    }

    public bool TryBeginOriginalImageLoad(out int workToken)
    {
        return TrySetStatusAndLockItem(ImageLoadStatusField.OriginalImage, AlbumImageItemStatus.Unloaded, AlbumImageItemStatus.Loading, out workToken);
    }

    public bool TryBeginDetailedThumbnailLoad(out bool ownsOriginalLoad)
    {
        if (TryBeginDetailedThumbnailLoad(out ownsOriginalLoad, out var workToken))
        {
            Volatile.Write(ref _untokenedImageWorkToken, workToken);
            return true;
        }

        return false;
    }

    public bool TryBeginDetailedThumbnailLoad(out bool ownsOriginalLoad, out int workToken)
    {
        ownsOriginalLoad = false;
        workToken = 0;
        while (true)
        {
            var current = Volatile.Read(ref _imageLoadStatusBits);
            if (IsImageItemBusy(current))
            {
                return false;
            }

            if (GetImageItemStatus(current, ImageLoadStatusField.DetailedThumbnail) != AlbumImageItemStatus.Unloaded)
            {
                return false;
            }

            var originalStatus = GetImageItemStatus(current, ImageLoadStatusField.OriginalImage);
            if (originalStatus == AlbumImageItemStatus.Loading)
            {
                return false;
            }

            var next = SetImageItemBusyForNewWork(
                SetImageItemStatus(current, ImageLoadStatusField.DetailedThumbnail, AlbumImageItemStatus.Loading));
            if (originalStatus == AlbumImageItemStatus.Unloaded)
            {
                next = SetImageItemStatus(next, ImageLoadStatusField.OriginalImage, AlbumImageItemStatus.Loading);
            }

            next = AdvanceImageItemWorkToken(next);
            if (Interlocked.CompareExchange(ref _imageLoadStatusBits, next, current) == current)
            {
                ownsOriginalLoad = originalStatus == AlbumImageItemStatus.Unloaded;
                workToken = GetImageItemWorkToken(next);
                return true;
            }
        }
    }

    public bool TryBeginDetailedThumbnailControlLoad(out bool ownsOriginalLoad, out bool detailedThumbnailWasAlreadyLoaded)
    {
        if (TryBeginDetailedThumbnailControlLoad(out ownsOriginalLoad, out detailedThumbnailWasAlreadyLoaded, out var workToken))
        {
            Volatile.Write(ref _untokenedImageWorkToken, workToken);
            return true;
        }

        return false;
    }

    public bool TryBeginDetailedThumbnailControlLoad(out bool ownsOriginalLoad, out bool detailedThumbnailWasAlreadyLoaded, out int workToken)
    {
        ownsOriginalLoad = false;
        detailedThumbnailWasAlreadyLoaded = false;
        workToken = 0;
        while (true)
        {
            var current = Volatile.Read(ref _imageLoadStatusBits);
            if (IsImageItemBusy(current))
            {
                return false;
            }

            var detailedStatus = GetImageItemStatus(current, ImageLoadStatusField.DetailedThumbnail);
            if (detailedStatus is not (AlbumImageItemStatus.Unloaded or AlbumImageItemStatus.Loaded))
            {
                return false;
            }

            var originalStatus = GetImageItemStatus(current, ImageLoadStatusField.OriginalImage);
            if (originalStatus == AlbumImageItemStatus.Loading)
            {
                return false;
            }

            var next = SetImageItemBusyForNewWork(
                SetImageItemStatus(current, ImageLoadStatusField.DetailedThumbnail, AlbumImageItemStatus.Loading));
            if (originalStatus == AlbumImageItemStatus.Unloaded)
            {
                next = SetImageItemStatus(next, ImageLoadStatusField.OriginalImage, AlbumImageItemStatus.Loading);
            }

            next = AdvanceImageItemWorkToken(next);
            if (Interlocked.CompareExchange(ref _imageLoadStatusBits, next, current) == current)
            {
                ownsOriginalLoad = originalStatus == AlbumImageItemStatus.Unloaded;
                detailedThumbnailWasAlreadyLoaded = detailedStatus == AlbumImageItemStatus.Loaded;
                workToken = GetImageItemWorkToken(next);
                return true;
            }
        }
    }

    public bool TryBeginVisibleThumbnailLoad(
        bool imageMissing,
        bool preferDetailed,
        bool isFullImageLoaded,
        out bool loadDetailed,
        out bool ownsOriginalLoad,
        out bool fastThumbnailWasAlreadyLoaded,
        out bool detailedThumbnailWasAlreadyLoaded)
    {
        return TryBeginVisibleThumbnailLoad(
            imageMissing,
            preferDetailed,
            isFullImageLoaded,
            out loadDetailed,
            out ownsOriginalLoad,
            out fastThumbnailWasAlreadyLoaded,
            out detailedThumbnailWasAlreadyLoaded,
            out var workToken) &&
            StoreUntokenedImageWorkToken(workToken);
    }

    public bool TryBeginVisibleThumbnailLoad(
        bool imageMissing,
        bool preferDetailed,
        bool isFullImageLoaded,
        out bool loadDetailed,
        out bool ownsOriginalLoad,
        out bool fastThumbnailWasAlreadyLoaded,
        out bool detailedThumbnailWasAlreadyLoaded,
        out int workToken,
        bool allowFastThumbnail = true)
    {
        loadDetailed = false;
        ownsOriginalLoad = false;
        fastThumbnailWasAlreadyLoaded = false;
        detailedThumbnailWasAlreadyLoaded = false;
        workToken = 0;
        while (true)
        {
            var current = Volatile.Read(ref _imageLoadStatusBits);
            if (IsImageItemBusy(current))
            {
                return false;
            }

            var fastStatus = GetImageItemStatus(current, ImageLoadStatusField.FastThumbnail);
            if (imageMissing &&
                allowFastThumbnail &&
                fastStatus is AlbumImageItemStatus.Unloaded or AlbumImageItemStatus.Loaded)
            {
                var next = SetImageItemBusyForNewWork(
                    SetImageItemStatus(current, ImageLoadStatusField.FastThumbnail, AlbumImageItemStatus.Loading));
                next = AdvanceImageItemWorkToken(next);
                if (Interlocked.CompareExchange(ref _imageLoadStatusBits, next, current) == current)
                {
                    fastThumbnailWasAlreadyLoaded = fastStatus == AlbumImageItemStatus.Loaded;
                    workToken = GetImageItemWorkToken(next);
                    return true;
                }

                continue;
            }

            var detailedStatus = GetImageItemStatus(current, ImageLoadStatusField.DetailedThumbnail);
            if (!preferDetailed ||
                isFullImageLoaded ||
                detailedStatus is not (AlbumImageItemStatus.Unloaded or AlbumImageItemStatus.Loaded))
            {
                return false;
            }

            var originalStatus = GetImageItemStatus(current, ImageLoadStatusField.OriginalImage);
            if (originalStatus == AlbumImageItemStatus.Loading)
            {
                return false;
            }

            var claimedOwnsOriginalLoad = originalStatus == AlbumImageItemStatus.Unloaded;
            var detailedNext = SetImageItemBusyForNewWork(
                SetImageItemStatus(current, ImageLoadStatusField.DetailedThumbnail, AlbumImageItemStatus.Loading));
            if (claimedOwnsOriginalLoad)
            {
                detailedNext = SetImageItemStatus(detailedNext, ImageLoadStatusField.OriginalImage, AlbumImageItemStatus.Loading);
            }

            detailedNext = AdvanceImageItemWorkToken(detailedNext);
            if (Interlocked.CompareExchange(ref _imageLoadStatusBits, detailedNext, current) == current)
            {
                loadDetailed = true;
                ownsOriginalLoad = claimedOwnsOriginalLoad;
                detailedThumbnailWasAlreadyLoaded = detailedStatus == AlbumImageItemStatus.Loaded;
                workToken = GetImageItemWorkToken(detailedNext);
                return true;
            }
        }
    }

    public void CompleteFastThumbnailLoad(bool loaded)
    {
        CompleteFastThumbnailLoad(loaded, TakeUntokenedImageWorkToken());
    }

    public void CompleteFastThumbnailLoad(bool loaded, int workToken)
    {
        SetStatusAndUnlockItem(ImageLoadStatusField.FastThumbnail, loaded ? AlbumImageItemStatus.Loaded : AlbumImageItemStatus.Unloaded, workToken);
    }

    public void CompleteOriginalImageLoad(bool loaded)
    {
        CompleteOriginalImageLoad(loaded, TakeUntokenedImageWorkToken());
    }

    public void CompleteOriginalImageLoad(bool loaded, int workToken)
    {
        SetStatusAndUnlockItem(ImageLoadStatusField.OriginalImage, loaded ? AlbumImageItemStatus.Loaded : AlbumImageItemStatus.Unloaded, workToken);
    }

    public void CompleteDetailedThumbnailLoad(bool loaded, bool originalLoaded, bool ownsOriginalLoad)
    {
        CompleteDetailedThumbnailLoad(loaded, originalLoaded, ownsOriginalLoad, TakeUntokenedImageWorkToken());
    }

    public void CompleteDetailedThumbnailLoad(bool loaded, bool originalLoaded, bool ownsOriginalLoad, int workToken)
    {
        while (true)
        {
            var current = Volatile.Read(ref _imageLoadStatusBits);
            if (!IsImageItemWorkTokenMatch(current, workToken))
            {
                return;
            }

            if (IsImageItemResetRequested(current))
            {
                var reset = ClearImageItemStatusesAndAdvanceWorkToken(current);
                if (Interlocked.CompareExchange(ref _imageLoadStatusBits, reset, current) == current)
                {
                    return;
                }

                continue;
            }

            if (!IsImageItemBusy(current))
            {
                return;
            }

            var next = SetImageItemStatus(
                current,
                ImageLoadStatusField.DetailedThumbnail,
                loaded ? AlbumImageItemStatus.Loaded : AlbumImageItemStatus.Unloaded);
            if (ownsOriginalLoad)
            {
                next = SetImageItemStatus(
                    next,
                    ImageLoadStatusField.OriginalImage,
                    originalLoaded ? AlbumImageItemStatus.Loaded : AlbumImageItemStatus.Unloaded);
            }

            next = SetImageItemBusy(next, isBusy: false);
            if (Interlocked.CompareExchange(ref _imageLoadStatusBits, next, current) == current)
            {
                return;
            }
        }
    }

    public void CompleteFastThumbnailLoadWithDetailedThumbnail(bool fastThumbnailWasAlreadyLoaded)
    {
        CompleteFastThumbnailLoadWithDetailedThumbnail(fastThumbnailWasAlreadyLoaded, TakeUntokenedImageWorkToken());
    }

    public void CompleteFastThumbnailLoadWithDetailedThumbnail(bool fastThumbnailWasAlreadyLoaded, int workToken)
    {
        while (true)
        {
            var current = Volatile.Read(ref _imageLoadStatusBits);
            if (!IsImageItemWorkTokenMatch(current, workToken))
            {
                return;
            }

            if (IsImageItemResetRequested(current))
            {
                var reset = ClearImageItemStatusesAndAdvanceWorkToken(current);
                if (Interlocked.CompareExchange(ref _imageLoadStatusBits, reset, current) == current)
                {
                    return;
                }

                continue;
            }

            if (!IsImageItemBusy(current))
            {
                return;
            }

            var next = SetImageItemStatus(
                current,
                ImageLoadStatusField.FastThumbnail,
                fastThumbnailWasAlreadyLoaded ? AlbumImageItemStatus.Loaded : AlbumImageItemStatus.Unloaded);
            next = SetImageItemStatus(next, ImageLoadStatusField.DetailedThumbnail, AlbumImageItemStatus.Loaded);
            next = SetImageItemBusy(next, isBusy: false);
            if (Interlocked.CompareExchange(ref _imageLoadStatusBits, next, current) == current)
            {
                return;
            }
        }
    }

    public void InvalidateDetailedThumbnailStatus()
    {
        InvalidateImageLoadStatus(ImageLoadStatusField.DetailedThumbnail);
    }

    public void InvalidateImageCacheStatus(AlbumImageCacheKind kind)
    {
        var field = kind switch
        {
            AlbumImageCacheKind.FastThumbnail => ImageLoadStatusField.FastThumbnail,
            AlbumImageCacheKind.DetailedThumbnail => ImageLoadStatusField.DetailedThumbnail,
            AlbumImageCacheKind.OriginalImage => ImageLoadStatusField.OriginalImage,
            _ => ImageLoadStatusField.FastThumbnail
        };
        InvalidateImageLoadStatus(field);
    }

    public void ResetImageLoadStatuses()
    {
        while (true)
        {
            var current = Volatile.Read(ref _imageLoadStatusBits);
            if (IsImageItemBusy(current))
            {
                var next = SetImageItemResetRequested(current, isResetRequested: true);
                if (Interlocked.CompareExchange(ref _imageLoadStatusBits, next, current) != current)
                {
                    continue;
                }

                IsFullImageLoaded = false;
                IsImageLoading = false;
                return;
            }

            if (Interlocked.CompareExchange(ref _imageLoadStatusBits, 0, current) == current)
            {
                Interlocked.Exchange(ref _untokenedImageWorkToken, 0);
                break;
            }
        }

        IsFullImageLoaded = false;
        IsImageLoading = false;
    }

    public void ResetImageLoadStatusesIfImageMissing()
    {
        if (Image is not null)
        {
            return;
        }

        ResetImageLoadStatuses();
        Status = "Loading";
    }

    public void ResetImageLoadStatusesForNewGeneration()
    {
        SetImageLease(null);
        ClearImageLoadStatusesAndInvalidateClaims();
        Interlocked.Exchange(ref _untokenedImageWorkToken, 0);
        IsFullImageLoaded = false;
        IsImageLoading = false;
        Status = "Loading";
    }

    public void SetLoadedImage(AlbumImageBitmapLease bitmap, bool isDetailedThumbnail, string status)
    {
        SetImageLease(bitmap);
        IsFullImageLoaded = isDetailedThumbnail;
        Status = status;
    }

    private AlbumImageItemStatus GetImageItemStatus(ImageLoadStatusField field)
    {
        return GetImageItemStatus(Volatile.Read(ref _imageLoadStatusBits), field);
    }

    private bool TrySetStatusAndLockItem(ImageLoadStatusField field, AlbumImageItemStatus expected, AlbumImageItemStatus replacement, out int workToken)
    {
        workToken = 0;
        while (true)
        {
            var current = Volatile.Read(ref _imageLoadStatusBits);
            if (IsImageItemBusy(current) || GetImageItemStatus(current, field) != expected)
            {
                return false;
            }

            var next = SetImageItemBusyForNewWork(SetImageItemStatus(current, field, replacement));
            next = AdvanceImageItemWorkToken(next);
            if (Interlocked.CompareExchange(ref _imageLoadStatusBits, next, current) == current)
            {
                workToken = GetImageItemWorkToken(next);
                return true;
            }
        }
    }

    private void SetStatusAndUnlockItem(ImageLoadStatusField field, AlbumImageItemStatus status, int workToken)
    {
        while (true)
        {
            var current = Volatile.Read(ref _imageLoadStatusBits);
            if (!IsImageItemWorkTokenMatch(current, workToken))
            {
                return;
            }

            if (IsImageItemResetRequested(current))
            {
                var reset = ClearImageItemStatusesAndAdvanceWorkToken(current);
                if (Interlocked.CompareExchange(ref _imageLoadStatusBits, reset, current) == current)
                {
                    return;
                }

                continue;
            }

            if (!IsImageItemBusy(current))
            {
                return;
            }

            var next = SetImageItemBusy(SetImageItemStatus(current, field, status), isBusy: false);
            if (Interlocked.CompareExchange(ref _imageLoadStatusBits, next, current) == current)
            {
                return;
            }
        }
    }

    private void InvalidateImageLoadStatus(ImageLoadStatusField field)
    {
        while (true)
        {
            var current = Volatile.Read(ref _imageLoadStatusBits);
            var status = GetImageItemStatus(current, field);
            if (status != AlbumImageItemStatus.Loaded)
            {
                return;
            }

            var next = SetImageItemStatus(current, field, AlbumImageItemStatus.Unloaded);
            if (Interlocked.CompareExchange(ref _imageLoadStatusBits, next, current) == current)
            {
                return;
            }
        }
    }

    private static AlbumImageItemStatus GetImageItemStatus(int bits, ImageLoadStatusField field)
    {
        return (AlbumImageItemStatus)((bits >> GetImageLoadStatusOffset(field)) & 0b11);
    }

    private static int SetImageItemStatus(int bits, ImageLoadStatusField field, AlbumImageItemStatus status)
    {
        var offset = GetImageLoadStatusOffset(field);
        var mask = 0b11 << offset;
        return (bits & ~mask) | (((int)status & 0b11) << offset);
    }

    private static int GetImageItemWorkToken(int bits)
    {
        return (int)((uint)bits >> 8);
    }

    private static bool IsImageItemWorkTokenMatch(int bits, int workToken)
    {
        return workToken != 0 && GetImageItemWorkToken(bits) == workToken;
    }

    private bool StoreUntokenedImageWorkToken(int workToken)
    {
        Volatile.Write(ref _untokenedImageWorkToken, workToken);
        return true;
    }

    private int TakeUntokenedImageWorkToken()
    {
        return Interlocked.Exchange(ref _untokenedImageWorkToken, 0);
    }

    private static int AdvanceImageItemWorkToken(int bits)
    {
        var nextToken = (GetImageItemWorkToken(bits) + 1) & 0x00ffffff;
        if (nextToken == 0)
        {
            nextToken = 1;
        }

        return (bits & 0xff) | (nextToken << 8);
    }

    private static int ClearImageItemStatusesAndAdvanceWorkToken(int bits)
    {
        return AdvanceImageItemWorkToken(bits) & ~0xff;
    }

    private void ClearImageLoadStatusesAndInvalidateClaims()
    {
        while (true)
        {
            var current = Volatile.Read(ref _imageLoadStatusBits);
            var next = ClearImageItemStatusesAndAdvanceWorkToken(current);
            if (Interlocked.CompareExchange(ref _imageLoadStatusBits, next, current) == current)
            {
                return;
            }
        }
    }

    private static bool IsImageItemBusy(int bits)
    {
        return (bits & (1 << 6)) != 0;
    }

    private static int SetImageItemBusy(int bits, bool isBusy)
    {
        const int mask = 1 << 6;
        return isBusy ? bits | mask : bits & ~mask;
    }

    private static bool IsImageItemResetRequested(int bits)
    {
        return (bits & (1 << 7)) != 0;
    }

    private static int SetImageItemResetRequested(int bits, bool isResetRequested)
    {
        const int mask = 1 << 7;
        return isResetRequested ? bits | mask : bits & ~mask;
    }

    private static int SetImageItemBusyForNewWork(int bits)
    {
        return SetImageItemResetRequested(SetImageItemBusy(bits, isBusy: true), isResetRequested: false);
    }

    private static int GetImageLoadStatusOffset(ImageLoadStatusField field)
    {
        return field switch
        {
            ImageLoadStatusField.FastThumbnail => 0,
            ImageLoadStatusField.OriginalImage => 2,
            ImageLoadStatusField.DetailedThumbnail => 4,
            _ => 0
        };
    }

    private sealed class ThumbnailLoadRequest
    {
        public ThumbnailLoadRequest(AlbumPhotoViewModel photo, CancellationToken cancellationToken)
        {
            Photo = photo;
            CancellationToken = cancellationToken;
            Completion = new TaskCompletionSource<ThumbnailLoadPermit>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public AlbumPhotoViewModel Photo { get; }

        public CancellationToken CancellationToken { get; set; }

        public bool IsVisiblePriority { get; set; }

        public int PriorityRank { get; set; } = int.MaxValue;

        public long Sequence { get; set; }

        public TaskCompletionSource<ThumbnailLoadPermit> Completion { get; }
    }

    private sealed class DisplayImageLoadRequest
    {
        public DisplayImageLoadRequest(AlbumPhotoViewModel photo, CancellationToken cancellationToken)
        {
            Photo = photo;
            CancellationToken = cancellationToken;
            Completion = new TaskCompletionSource<DisplayImageLoadPermit>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public AlbumPhotoViewModel Photo { get; }

        public CancellationToken CancellationToken { get; set; }

        public bool IsVisiblePriority { get; set; }

        public int PriorityRank { get; set; } = int.MaxValue;

        public long Sequence { get; set; }

        public TaskCompletionSource<DisplayImageLoadPermit> Completion { get; }
    }

    private sealed class ThumbnailLoadPermit : IDisposable
    {
        private readonly bool _countsAgainstActive;
        private bool _disposed;

        public ThumbnailLoadPermit(bool countsAgainstActive = true)
        {
            _countsAgainstActive = countsAgainstActive;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_countsAgainstActive)
            {
                CompleteThumbnailLoad();
            }
        }
    }

    private sealed class DisplayImageLoadPermit : IDisposable
    {
        private readonly bool _countsAgainstActive;
        private bool _disposed;

        public DisplayImageLoadPermit(bool countsAgainstActive = true)
        {
            _countsAgainstActive = countsAgainstActive;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_countsAgainstActive)
            {
                CompleteDisplayImageLoad();
            }
        }
    }

    public async Task StartViewportLoadAsync(ImageCacheService imageCache, HttpClient httpClient)
    {
        if (IsImageLoading)
        {
            return;
        }

        var previousCancellation = _loadCancellation;
        _loadCancellation = null;
        try
        {
            previousCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            previousCancellation?.Dispose();
        }

        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;

        try
        {
            IsImageLoading = true;

            if (Image is null)
            {
                await LoadThumbnailAsync(imageCache, httpClient, cancellation.Token);
            }

            await LoadDisplayImageAsync(imageCache, httpClient, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                _loadCancellation.Dispose();
                _loadCancellation = null;
                IsImageLoading = false;
            }
        }
    }

    public void StopViewportLoad()
    {
        var cancellation = _loadCancellation;
        _loadCancellation = null;
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            cancellation?.Dispose();
        }

        CancelPendingThumbnailLoad(this);
        CancelPendingDisplayImageLoad(this);
        ClearPendingLoadPriority(this);
        IsImageLoading = false;
        ReleaseCachedImage();
    }

    private async Task LoadThumbnailAsync(ImageCacheService imageCache, HttpClient httpClient, CancellationToken cancellationToken)
    {
        try
        {
            Status = "Loading thumbnail";
            using var permit = await EnqueueThumbnailLoadAsync(this, cancellationToken);
            if (Image is not null)
            {
                return;
            }

            var bitmap = await imageCache.LoadDisplayBitmapAsync(
                AlbumId,
                $"{PhotoId}-thumbnail.jpg",
                ThumbnailDownloadUrl,
                httpClient,
                ThumbnailPixelSize,
                ThumbnailPixelSize,
                cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                bitmap.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }

            SetImageLease(bitmap);

            Status = "Loading full image";
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
            {
                Status = ex.Message;
            }
        }
    }

    private static Task<ThumbnailLoadPermit> EnqueueThumbnailLoadAsync(
        AlbumPhotoViewModel photo,
        CancellationToken cancellationToken)
    {
        Task<ThumbnailLoadPermit> task;
        lock (ThumbnailLoadQueueSync)
        {
            if (!PendingThumbnailLoads.TryGetValue(photo, out var request))
            {
                request = new ThumbnailLoadRequest(photo, cancellationToken);
                PendingThumbnailLoads.Add(photo, request);
            }

            request.CancellationToken = cancellationToken;
            request.IsVisiblePriority = photo._thumbnailPriorityRank != int.MaxValue;
            request.PriorityRank = photo._thumbnailPriorityRank;
            request.Sequence = ++_thumbnailLoadSequence;
            task = request.Completion.Task;
        }

        ProcessThumbnailLoadQueue();
        return task;
    }

    private async Task LoadDisplayImageAsync(ImageCacheService imageCache, HttpClient httpClient, CancellationToken cancellationToken)
    {
        if (IsFullImageLoaded)
        {
            return;
        }

        try
        {
            Status = "Loading full image";
            using var permit = await EnqueueDisplayImageLoadAsync(this, cancellationToken);
            if (IsFullImageLoaded)
            {
                return;
            }

            var bitmap = await imageCache.LoadDisplayBitmapAsync(
                AlbumId,
                $"{PhotoId}-full{FileExtension}",
                DownloadUrl,
                httpClient,
                DisplayPixelWidth,
                DisplayPixelHeight,
                cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                bitmap.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }

            SetImageLease(bitmap);
            IsFullImageLoaded = true;
            Status = "Full image loaded";
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
            {
                Status = ex.Message;
            }
        }
    }

    private static Task<DisplayImageLoadPermit> EnqueueDisplayImageLoadAsync(
        AlbumPhotoViewModel photo,
        CancellationToken cancellationToken)
    {
        Task<DisplayImageLoadPermit> task;
        lock (DisplayImageLoadQueueSync)
        {
            if (!PendingDisplayImageLoads.TryGetValue(photo, out var request))
            {
                request = new DisplayImageLoadRequest(photo, cancellationToken);
                PendingDisplayImageLoads.Add(photo, request);
            }

            request.CancellationToken = cancellationToken;
            request.IsVisiblePriority = photo._displayImagePriorityRank != int.MaxValue;
            request.PriorityRank = photo._displayImagePriorityRank;
            request.Sequence = ++_displayImageLoadSequence;
            task = request.Completion.Task;
        }

        ProcessDisplayImageLoadQueue();
        return task;
    }

    public static void PrioritizeViewportLoads(IReadOnlyList<AlbumPhotoViewModel> prioritizedPhotos)
    {
        PrioritizeThumbnailLoads(prioritizedPhotos);
        PrioritizeDisplayImageLoads(prioritizedPhotos);
    }

    private static void PrioritizeThumbnailLoads(IReadOnlyList<AlbumPhotoViewModel> prioritizedPhotos)
    {
        var priorityByPhoto = prioritizedPhotos
            .Select((photo, index) => new { photo, index })
            .GroupBy(item => item.photo)
            .ToDictionary(group => group.Key, group => group.Min(item => item.index));

        lock (ThumbnailLoadQueueSync)
        {
            foreach (var photo in PrioritizedThumbnailPhotos)
            {
                photo._thumbnailPriorityRank = int.MaxValue;
            }

            PrioritizedThumbnailPhotos.Clear();

            foreach (var request in PendingThumbnailLoads.Values)
            {
                request.Photo._thumbnailPriorityRank = int.MaxValue;
            }

            foreach (var (photo, index) in priorityByPhoto)
            {
                photo._thumbnailPriorityRank = index;
                PrioritizedThumbnailPhotos.Add(photo);
            }

            foreach (var request in PendingThumbnailLoads.Values)
            {
                request.IsVisiblePriority = priorityByPhoto.TryGetValue(request.Photo, out var index);
                request.PriorityRank = request.IsVisiblePriority ? index : int.MaxValue;
            }
        }

        ProcessThumbnailLoadQueue();
    }

    private static void PrioritizeDisplayImageLoads(IReadOnlyList<AlbumPhotoViewModel> prioritizedPhotos)
    {
        var priorityByPhoto = prioritizedPhotos
            .Select((photo, index) => new { photo, index })
            .GroupBy(item => item.photo)
            .ToDictionary(group => group.Key, group => group.Min(item => item.index));

        lock (DisplayImageLoadQueueSync)
        {
            foreach (var photo in PrioritizedDisplayImagePhotos)
            {
                photo._displayImagePriorityRank = int.MaxValue;
            }

            PrioritizedDisplayImagePhotos.Clear();

            foreach (var request in PendingDisplayImageLoads.Values)
            {
                request.Photo._displayImagePriorityRank = int.MaxValue;
            }

            foreach (var (photo, index) in priorityByPhoto)
            {
                photo._displayImagePriorityRank = index;
                PrioritizedDisplayImagePhotos.Add(photo);
            }

            foreach (var request in PendingDisplayImageLoads.Values)
            {
                request.IsVisiblePriority = priorityByPhoto.TryGetValue(request.Photo, out var index);
                request.PriorityRank = request.IsVisiblePriority ? index : int.MaxValue;
            }
        }

        ProcessDisplayImageLoadQueue();
    }

    private static void CancelPendingThumbnailLoad(AlbumPhotoViewModel photo)
    {
        ThumbnailLoadRequest? cancelledRequest = null;
        lock (ThumbnailLoadQueueSync)
        {
            if (PendingThumbnailLoads.Remove(photo, out var request))
            {
                cancelledRequest = request;
            }
        }

        cancelledRequest?.Completion.TrySetCanceled(cancelledRequest.CancellationToken);
        ProcessThumbnailLoadQueue();
    }

    private static void CancelPendingDisplayImageLoad(AlbumPhotoViewModel photo)
    {
        DisplayImageLoadRequest? cancelledRequest = null;
        lock (DisplayImageLoadQueueSync)
        {
            if (PendingDisplayImageLoads.Remove(photo, out var request))
            {
                cancelledRequest = request;
            }
        }

        cancelledRequest?.Completion.TrySetCanceled(cancelledRequest.CancellationToken);
        ProcessDisplayImageLoadQueue();
    }

    private static void ClearPendingLoadPriority(AlbumPhotoViewModel photo)
    {
        lock (ThumbnailLoadQueueSync)
        {
            PrioritizedThumbnailPhotos.Remove(photo);
            photo._thumbnailPriorityRank = int.MaxValue;
        }

        lock (DisplayImageLoadQueueSync)
        {
            PrioritizedDisplayImagePhotos.Remove(photo);
            photo._displayImagePriorityRank = int.MaxValue;
        }
    }

    private static void CompleteThumbnailLoad()
    {
        lock (ThumbnailLoadQueueSync)
        {
            _activeThumbnailLoadCount = Math.Max(0, _activeThumbnailLoadCount - 1);
        }

        ProcessThumbnailLoadQueue();
    }

    private static void CompleteDisplayImageLoad()
    {
        lock (DisplayImageLoadQueueSync)
        {
            _isDisplayImageLoadActive = false;
        }

        ProcessDisplayImageLoadQueue();
    }

    private static void ProcessThumbnailLoadQueue()
    {
        List<ThumbnailLoadRequest> cancelledRequests = new();
        List<ThumbnailLoadRequest> selectedRequests = new();

        lock (ThumbnailLoadQueueSync)
        {
            foreach (var request in PendingThumbnailLoads.Values.ToList())
            {
                if (request.CancellationToken.IsCancellationRequested || request.Photo.Image is not null)
                {
                    PendingThumbnailLoads.Remove(request.Photo);
                    cancelledRequests.Add(request);
                }
            }

            while (_activeThumbnailLoadCount < MaxConcurrentThumbnailLoads)
            {
                var selectedRequest = PendingThumbnailLoads.Values
                    .OrderByDescending(request => request.IsVisiblePriority)
                    .ThenBy(request => request.PriorityRank)
                    .ThenBy(request => request.Sequence)
                    .FirstOrDefault();

                if (selectedRequest is null)
                {
                    break;
                }

                PendingThumbnailLoads.Remove(selectedRequest.Photo);
                selectedRequests.Add(selectedRequest);
                _activeThumbnailLoadCount++;
            }
        }

        foreach (var request in cancelledRequests)
        {
            if (request.CancellationToken.IsCancellationRequested)
            {
                request.Completion.TrySetCanceled(request.CancellationToken);
            }
            else
            {
                request.Completion.TrySetResult(new ThumbnailLoadPermit(countsAgainstActive: false));
            }
        }

        foreach (var request in selectedRequests)
        {
            request.Completion.TrySetResult(new ThumbnailLoadPermit());
        }
    }

    private static void ProcessDisplayImageLoadQueue()
    {
        List<DisplayImageLoadRequest> cancelledRequests = new();
        DisplayImageLoadRequest? selectedRequest = null;

        lock (DisplayImageLoadQueueSync)
        {
            if (_isDisplayImageLoadActive)
            {
                return;
            }

            foreach (var request in PendingDisplayImageLoads.Values.ToList())
            {
                if (request.CancellationToken.IsCancellationRequested || request.Photo.IsFullImageLoaded)
                {
                    PendingDisplayImageLoads.Remove(request.Photo);
                    cancelledRequests.Add(request);
                }
            }

            selectedRequest = PendingDisplayImageLoads.Values
                .OrderByDescending(request => request.IsVisiblePriority)
                .ThenBy(request => request.PriorityRank)
                .ThenBy(request => request.Sequence)
                .FirstOrDefault();

            if (selectedRequest is not null)
            {
                PendingDisplayImageLoads.Remove(selectedRequest.Photo);
                _isDisplayImageLoadActive = true;
            }
        }

        foreach (var request in cancelledRequests)
        {
            if (request.CancellationToken.IsCancellationRequested)
            {
                request.Completion.TrySetCanceled(request.CancellationToken);
            }
            else
            {
                request.Completion.TrySetResult(new DisplayImageLoadPermit(countsAgainstActive: false));
            }
        }

        selectedRequest?.Completion.TrySetResult(new DisplayImageLoadPermit());
    }

    public void ReleaseCachedImage()
    {
        SetImageLease(null);
        ResetImageLoadStatuses();
        Status = "Loading";
    }

    partial void OnImageChanging(Bitmap? value)
    {
        if (Image is null || ReferenceEquals(Image, value) || _isApplyingImageLease)
        {
            return;
        }

        if (_imageLease is not null)
        {
            _imageLease.Dispose();
            _imageLease = null;
            return;
        }

        if (Image is not null)
        {
            Image.Dispose();
        }
    }

    private void SetImageLease(AlbumImageBitmapLease? value)
    {
        var oldLease = _imageLease;
        _imageLease = value;
        _isApplyingImageLease = true;
        try
        {
            Image = value?.Bitmap;
        }
        finally
        {
            _isApplyingImageLease = false;
        }

        if (!ReferenceEquals(oldLease, value))
        {
            oldLease?.Dispose();
        }
    }

    partial void OnIsSelectedForViewingChanged(bool value)
    {
        OnPropertyChanged(nameof(CardBorderBrush));
        OnPropertyChanged(nameof(CardBorderThickness));
    }

    partial void OnIsSelectedForBulkChanged(bool value)
    {
        OnPropertyChanged(nameof(CardBorderBrush));
        OnPropertyChanged(nameof(CardBorderThickness));
        OnPropertyChanged(nameof(SelectionGlyph));
        OnPropertyChanged(nameof(SelectionBackground));
        OnPropertyChanged(nameof(SelectionForeground));
    }

    partial void OnIsDuplicateGroupMainChanged(bool value)
    {
        OnPropertyChanged(nameof(HasDuplicateGroup));
    }

    partial void OnDuplicateGroupCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasDuplicateGroup));
        OnPropertyChanged(nameof(DuplicateGroupCountText));
    }

    partial void OnIsBestInDuplicateGroupChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBestMarkerVisible));
    }

    partial void OnScoreChanged(int value)
    {
        OnPropertyChanged(nameof(IsScoreVisible));
        OnPropertyChanged(nameof(ScoreText));
    }
}

public enum AlbumImageItemStatus
{
    Unloaded = 0,
    Loading = 1,
    Loaded = 2
}

internal enum ImageLoadStatusField
{
    FastThumbnail,
    OriginalImage,
    DetailedThumbnail
}
