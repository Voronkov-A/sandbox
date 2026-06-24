using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Picshare.Models;
using Picshare.Services;

namespace Picshare.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private const string LocalToGoogleDriveAlbumTypeId = "local-to-google-drive";
    private const string LocalAlbumTypeId = "local-file-system";
    private const string UncategorizedReviewTabId = "uncategorized";
    private const string NiceReviewTabId = "nice";
    private const string OkReviewTabId = "ok";
    private const string TrashReviewTabId = "trash";
    private const string UnresolvedDuplicatesReviewTabId = "unresolved-duplicates";
    private const string FlowReviewTabId = "flow";
    private const int PhotosPerRow = 4;
    private const int MaxRecentAlbumCount = 10;

    [ObservableProperty]
    private AlbumTypeOptionViewModel? _selectedAlbumType;

    [ObservableProperty]
    private string _albumTitle = "New album";

    [ObservableProperty]
    private int _targetNicePhotoCount;

    [ObservableProperty]
    private DateTimeOffset? _importDate = DateTimeOffset.Now;

    [ObservableProperty]
    private bool _isFolderDateImportVisible;

    [ObservableProperty]
    private string _folderDateImportSourceText = "";

    [ObservableProperty]
    private string _parentDriveFolderId = "";

    [ObservableProperty]
    private string _googleSignInUrl = "";

    [ObservableProperty]
    private string _googleSignInMessage = "";

    [ObservableProperty]
    private bool _isGoogleSignedIn;

    [ObservableProperty]
    private bool _isGoogleSignInPending;

    [ObservableProperty]
    private string _selectedDriveFolderName = "Please select a folder";

    [ObservableProperty]
    private string _selectedLocalAlbumDestinationName = "Please select a folder";

    [ObservableProperty]
    private string _localAlbumDestinationPath = "";

    [ObservableProperty]
    private bool _isLocalAlbumDestinationSelected;

    [ObservableProperty]
    private bool _isDriveFolderSelected;

    [ObservableProperty]
    private bool _isDriveFolderPickerVisible;

    [ObservableProperty]
    private bool _isCreateDriveFolderDialogVisible;

    [ObservableProperty]
    private string _currentDriveFolderId = "root";

    [ObservableProperty]
    private string _currentDriveFolderName = "My Drive";

    [ObservableProperty]
    private bool _currentDriveFolderCanBeUsed = true;

    [ObservableProperty]
    private string _newDriveFolderName = "";

    [ObservableProperty]
    private string _openAlbumLink = "";

    [ObservableProperty]
    private bool _hasRecentAlbums;

    [ObservableProperty]
    private string _shareLink = "";

    [ObservableProperty]
    private string _driveFolderLink = "";

    [ObservableProperty]
    private string _currentAlbumTitle = "";

    [ObservableProperty]
    private string _status = "Create a Google Drive album or open a shared Picshare link.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isSettingsPanelVisible;

    [ObservableProperty]
    private string _anonymousReviewerName = "";

    [ObservableProperty]
    private int _maximumParallelism = LocalUserSettings.DefaultMaximumParallelism;

    [ObservableProperty]
    private bool _cacheThumbnails = true;

    [ObservableProperty]
    private bool _cacheOriginalImages = true;

    [ObservableProperty]
    private string _pictureDefaultDownloadDirectoryPath = "";

    [ObservableProperty]
    private string _uncategorizedDefaultDownloadDirectoryPath = "";

    [ObservableProperty]
    private string _niceDefaultDownloadDirectoryPath = "";

    [ObservableProperty]
    private string _okDefaultDownloadDirectoryPath = "";

    [ObservableProperty]
    private string _trashDefaultDownloadDirectoryPath = "";

    [ObservableProperty]
    private bool _isPhotoViewerVisible;

    [ObservableProperty]
    private string _photoViewerTitle = "";

    [ObservableProperty]
    private string _photoViewerStatus = "";

    [ObservableProperty]
    private Bitmap? _photoViewerImage;

    [ObservableProperty]
    private int _photoViewerRotationDegrees;

    [ObservableProperty]
    private bool _isPhotoViewerActionsVisible = true;

    [ObservableProperty]
    private bool _isGoogleAuthorizationRequired;

    [ObservableProperty]
    private string _googleAuthorizationMessage = "";

    [ObservableProperty]
    private bool _isFeedbackConflictNoticeVisible;

    [ObservableProperty]
    private string _feedbackConflictMessage = "";

    [ObservableProperty]
    private bool _isCommitConfirmationVisible;

    [ObservableProperty]
    private string _commitConfirmationMessage = "";

    [ObservableProperty]
    private bool _isFeedbackCommitted;

    [ObservableProperty]
    private bool _isPassConfirmationVisible;

    [ObservableProperty]
    private string _passConfirmationMessage = "";

    [ObservableProperty]
    private bool _isFeedbackPassed;

    [ObservableProperty]
    private bool _isLeaveConfirmationVisible;

    [ObservableProperty]
    private string _leaveConfirmationMessage = "";

    [ObservableProperty]
    private bool _isFeedbackLeft;

    [ObservableProperty]
    private bool _isCollectFeedbackConfirmationVisible;

    [ObservableProperty]
    private string _collectFeedbackConfirmationMessage = "";

    [ObservableProperty]
    private bool _isFinalizeConfirmationVisible;

    [ObservableProperty]
    private string _finalizeConfirmationMessage = "";

    [ObservableProperty]
    private bool _isDeleteAlbumConfirmationVisible;

    [ObservableProperty]
    private string _deleteAlbumConfirmationMessage = "";

    [ObservableProperty]
    private bool _canDeleteAlbum;

    [ObservableProperty]
    private bool _canCollectFeedback;

    [ObservableProperty]
    private bool _isCollectFeedbackVisible;

    [ObservableProperty]
    private bool _canStartNextRound;

    [ObservableProperty]
    private bool _canFinalizeFeedback;

    [ObservableProperty]
    private bool _canModifyFeedback;

    [ObservableProperty]
    private bool _canCommitFeedback;

    [ObservableProperty]
    private string _commitFeedbackStatus = "";

    [ObservableProperty]
    private bool _canPassFeedback;

    [ObservableProperty]
    private bool _canLeaveFeedback;

    [ObservableProperty]
    private bool _isStandardFeedbackActionsVisible;

    [ObservableProperty]
    private bool _isLeaveFeedbackVisible;

    [ObservableProperty]
    private bool _isLeaveFeedbackMenuItemVisible;

    [ObservableProperty]
    private string _finalizedFeedbackMessage = "";

    [ObservableProperty]
    private bool _canUseAlbumMoreMenu;

    [ObservableProperty]
    private bool _isAlbumDownloadDialogVisible;

    [ObservableProperty]
    private bool _isAlbumDownloadProgressVisible;

    [ObservableProperty]
    private string _albumDownloadProgressMessage = "";

    [ObservableProperty]
    private int _albumDownloadProgressValue;

    [ObservableProperty]
    private int _albumDownloadProgressMaximum = 1;

    [ObservableProperty]
    private bool _isAlbumCreationProgressVisible;

    [ObservableProperty]
    private string _albumCreationProgressMessage = "";

    [ObservableProperty]
    private int _albumCreationProgressValue;

    [ObservableProperty]
    private int _albumCreationProgressMaximum = 1;

    [ObservableProperty]
    private bool _isAlbumDeletionProgressVisible;

    [ObservableProperty]
    private string _albumDeletionProgressMessage = "";

    [ObservableProperty]
    private int _albumDeletionProgressValue;

    [ObservableProperty]
    private int _albumDeletionProgressMaximum = 1;

    [ObservableProperty]
    private bool _isCancelAlbumDeletionConfirmationVisible;

    [ObservableProperty]
    private string _cancelAlbumDeletionConfirmationMessage = "";

    [ObservableProperty]
    private bool _isClearAlbumDestinationConfirmationVisible;

    [ObservableProperty]
    private string _clearAlbumDestinationConfirmationMessage = "";

    [ObservableProperty]
    private bool _isInfrastructureErrorVisible;

    [ObservableProperty]
    private string _infrastructureErrorMessage = "";

    [ObservableProperty]
    private bool _canMarkCurrentPhotoUncategorized;

    [ObservableProperty]
    private bool _canMarkCurrentPhotoNice;

    [ObservableProperty]
    private bool _canMarkCurrentPhotoOk;

    [ObservableProperty]
    private bool _canMarkCurrentPhotoTrash;

    [ObservableProperty]
    private bool _canShowCurrentPhotoTrashAction;

    [ObservableProperty]
    private bool _shouldShowCurrentPhotoUncategorizedAction;

    [ObservableProperty]
    private bool _shouldShowCurrentPhotoNiceAction;

    [ObservableProperty]
    private bool _shouldShowCurrentPhotoOkAction;

    [ObservableProperty]
    private bool _shouldShowCurrentPhotoTrashAction;

    [ObservableProperty]
    private bool _canNavigatePhotoViewerCategory;

    [ObservableProperty]
    private bool _canDownloadCurrentPhoto;

    [ObservableProperty]
    private int _selectedPhotoCount;

    [ObservableProperty]
    private bool _isBulkPhotoActionPanelVisible;

    [ObservableProperty]
    private string _bulkSelectionStatus = "";

    [ObservableProperty]
    private bool _canMarkSelectedPhotosUncategorized;

    [ObservableProperty]
    private bool _canMarkSelectedPhotosNice;

    [ObservableProperty]
    private bool _canMarkSelectedPhotosOk;

    [ObservableProperty]
    private bool _canMarkSelectedPhotosTrash;

    [ObservableProperty]
    private bool _canShowSelectedPhotosTrashAction;

    [ObservableProperty]
    private bool _shouldShowSelectedPhotosUncategorizedAction;

    [ObservableProperty]
    private bool _shouldShowSelectedPhotosNiceAction;

    [ObservableProperty]
    private bool _shouldShowSelectedPhotosOkAction;

    [ObservableProperty]
    private bool _shouldShowSelectedPhotosTrashAction;

    [ObservableProperty]
    private bool _canDownloadSelectedPhotos;

    [ObservableProperty]
    private bool _canRotateSelectedPhotos;

    [ObservableProperty]
    private bool _canMarkSelectedPhotosAsDuplicates;

    [ObservableProperty]
    private bool _canRemoveCurrentPhotoFromDuplicates;

    [ObservableProperty]
    private bool _isPhotoViewerDuplicateStripVisible;

    [ObservableProperty]
    private bool _isCurrentPhotoBestInDuplicateGroup;

    [ObservableProperty]
    private bool _isAuthorFlowVisible;

    [ObservableProperty]
    private bool _isRegularFlowVisible;

    [ObservableProperty]
    private bool _isFinalizedFlowVisible;

    [ObservableProperty]
    private string _flowStatus = "";

    [ObservableProperty]
    private AlbumPhotoSourceViewModel? _selectedAlbumPhoto;

    [ObservableProperty]
    private int _selectedAlbumPhotoSourceCount;

    [ObservableProperty]
    private bool _isAlbumPhotoSourceActionPanelVisible;

    [ObservableProperty]
    private string _albumPhotoSourceSelectionStatus = "";

    public ObservableCollection<AlbumTypeOptionViewModel> AlbumTypes { get; } = new()
    {
        new AlbumTypeOptionViewModel(
            LocalToGoogleDriveAlbumTypeId,
            "Upload local photos to Google Drive",
            "Create a trusted shared Drive folder from local photos captured on the selected date."),
        new AlbumTypeOptionViewModel(
            LocalAlbumTypeId,
            "Create local album",
            "Create an album in a local or shared filesystem folder.")
    };

    public bool IsAlbumSettingsVisible => SelectedAlbumType is not null;

    public bool IsGoogleDriveAlbumSettingsVisible => SelectedAlbumType?.Id == LocalToGoogleDriveAlbumTypeId;

    public bool IsLocalAlbumSettingsVisible => SelectedAlbumType?.Id == LocalAlbumTypeId;

    public bool IsCreateResultVisible => !string.IsNullOrWhiteSpace(ShareLink);

    public bool IsGoogleSignInInstructionVisible => !string.IsNullOrWhiteSpace(GoogleSignInUrl) && IsGoogleSignInPending;

    public bool IsImportCandidatesVisible => ImportCandidates.Count > 0;

    public string GoogleConnectionStatus => IsGoogleSignedIn
        ? string.IsNullOrWhiteSpace(_googleTokenSet?.Email)
            ? "Connected"
            : $"Connected as {_googleTokenSet.Email}"
        : IsGoogleSignInPending
            ? "Waiting for consent"
            : "Not connected";

    public ObservableCollection<AlbumPhotoSourceViewModel> AlbumPhotos { get; } = new();

    public ObservableCollection<AlbumPhotoSourceRowViewModel> AlbumPhotoRows { get; } = new();

    public ObservableCollection<AlbumPhotoSourceViewModel> ImportCandidates { get; } = new();

    public ObservableCollection<AlbumPhotoSourceRowViewModel> ImportCandidateRows { get; } = new();

    public ObservableCollection<DriveItemViewModel> DriveItems { get; } = new();

    public ObservableCollection<AlbumPhotoViewModel> Photos { get; } = new();

    public ObservableCollection<AlbumPhotoViewModel> PhotoViewerDuplicatePhotos { get; } = new();

    public ObservableCollection<RecentAlbumViewModel> RecentAlbums { get; } = new();

    public ObservableCollection<object> UncategorizedPhotoGroups { get; } = new();

    public ObservableCollection<object> NicePhotoGroups { get; } = new();

    public ObservableCollection<object> OkPhotoGroups { get; } = new();

    public ObservableCollection<object> TrashPhotoGroups { get; } = new();

    public ObservableCollection<object> UnresolvedDuplicatePhotoGroups { get; } = new();

    public ObservableCollection<ReviewerFeedbackFlowItemViewModel> CommittedReviewers { get; } = new();

    public ObservableCollection<ReviewerFeedbackFlowItemViewModel> PassedReviewers { get; } = new();

    public ObservableCollection<ReviewerFeedbackFlowItemViewModel> LeftReviewers { get; } = new();

    public ObservableCollection<ReviewerFeedbackFlowItemViewModel> InProgressReviewers { get; } = new();

    public ObservableCollection<AlbumDownloadCategoryViewModel> AlbumDownloadCategories { get; } = new();

    public string UncategorizedTabHeader => $"Uncategorized ({GetVisiblePhotos().Count(photo => string.IsNullOrWhiteSpace(photo.Category))})";

    public string NiceTabHeader => $"Nice ({GetVisiblePhotos().Count(photo => string.Equals(photo.Category, "nice", StringComparison.Ordinal))})";

    public string OkTabHeader => $"Ok ({GetVisiblePhotos().Count(photo => string.Equals(photo.Category, "ok", StringComparison.Ordinal))})";

    public string TrashTabHeader => $"Trash ({GetVisiblePhotos().Count(photo => string.Equals(photo.Category, "trash", StringComparison.Ordinal))})";

    public string UnresolvedDuplicatesTabHeader => $"Unresolved duplicates ({GetUnresolvedDuplicatePhotos().Count()})";

    public bool HasUnresolvedDuplicatePhotos => GetUnresolvedDuplicatePhotos().Any();

    public string FlowTabHeader => "Flow";

    public string CommittedReviewersHeader => $"Committed reviewers ({CommittedReviewers.Count})";

    public string PassedReviewersHeader => $"Passed ({PassedReviewers.Count})";

    public string LeftReviewersHeader => $"Left ({LeftReviewers.Count})";

    public string InProgressReviewersHeader => $"In progress ({InProgressReviewers.Count})";

    private readonly GoogleDriveAlbumPublisher _publisher = new();
    private readonly LocalFileSystemAlbumPublisher _localPublisher = new();
    private readonly AlbumLoader _albumLoader = new();
    private readonly LocalPhotoScanner _photoScanner = new();
    private readonly GoogleOAuthClient _oauthClient = new();
    private readonly GoogleUserInfoClient _googleUserInfoClient = new();
    private readonly PicshareSettingsProvider _settingsProvider = new();
    private readonly LocalUserSettingsStore _localUserSettingsStore;
    private readonly AlbumOpenHistoryStore _albumOpenHistoryStore;
    private readonly PersistentAlbumCreationService _albumCreationService;
    private readonly GoogleOAuthTokenStore _tokenStore;
    private readonly ReviewerFeedbackService _reviewerFeedbackService;
    private readonly ImageCacheService _imageCache;
    private readonly AlbumDeletionService _albumDeletionService;
    private readonly HttpClient _imageHttpClient = new();
    private readonly List<IStorageFolder> _folderDateImportFolders = new();
    private readonly List<DriveFolderLocation> _driveFolderPath = new();
    private GoogleOAuthTokenSet? _googleTokenSet;
    private CancellationTokenSource? _googleSignInCancellation;
    private CancellationTokenSource? _photoViewerCancellation;
    private CancellationTokenSource? _feedbackSyncCancellation;
    private CancellationTokenSource? _flowMonitorCancellation;
    private CancellationTokenSource? _albumDownloadCancellation;
    private CancellationTokenSource? _albumCreationCancellation;
    private CancellationTokenSource? _albumDeletionCancellation;
    private PendingAlbumCreation? _activePendingAlbumCreation;
    private AlbumManifest? _activeDeletingAlbumManifest;
    private PendingAlbumCreation? _failedPendingAlbumCreation;
    private AlbumManifest? _failedDeletingAlbumManifest;
    private AlbumManifest? _currentManifest;
    private AlbumManifest? _pendingGoogleAuthorizationManifest;
    private AlbumPhotoViewModel? _selectedViewedPhoto;
    private string _selectedViewedPhotoSourceCategory = "";
    private string _selectedViewedPhotoSourceReviewTabId = UncategorizedReviewTabId;
    private ReviewerFeedbackSession? _feedbackSession;
    private ReviewerFeedbackDatabase? _feedbackDatabase;
    private ReviewerFeedbackStatus? _feedbackStatus;
    private FeedbackReviewerIdentity? _currentReviewerIdentity;
    private readonly Dictionary<string, IReadOnlyList<AlbumPhotoViewModel>> _duplicateGroupsById = new(StringComparer.Ordinal);
    private bool _isFlowTabActive;
    private string _activeReviewTabId = UncategorizedReviewTabId;
    private int _unfrozenCollectedPhotoCount;
    private bool _hasCollectedFeedback;
    private bool _isFeedbackFinalized;
    private string _lastOpenAlbumLink = "";
    private readonly SemaphoreSlim _feedbackSyncGate = new(1);
    private string? _driveNextPageToken;

    public bool HasMoreDriveItems => !string.IsNullOrWhiteSpace(_driveNextPageToken);

    public MainViewModel()
    {
        _localUserSettingsStore = new LocalUserSettingsStore(_settingsProvider.LocalStorageRootPath);
        _albumOpenHistoryStore = new AlbumOpenHistoryStore(_settingsProvider.LocalStorageRootPath);
        _albumCreationService = new PersistentAlbumCreationService(_settingsProvider.LocalStorageRootPath);
        _tokenStore = new GoogleOAuthTokenStore(_settingsProvider.LocalStorageRootPath);
        _reviewerFeedbackService = new ReviewerFeedbackService(_settingsProvider.LocalStorageRootPath);
        _imageCache = new ImageCacheService(_settingsProvider.LocalStorageRootPath);
        _albumDeletionService = new AlbumDeletionService(
            _reviewerFeedbackService,
            _imageCache,
            _settingsProvider.LocalStorageRootPath);
        var localSettings = _localUserSettingsStore.Load();
        var albumOpenHistory = _albumOpenHistoryStore.Load();
        AnonymousReviewerName = localSettings.AnonymousReviewerName;
        MaximumParallelism = NormalizeMaximumParallelism(localSettings.MaximumParallelism);
        CacheThumbnails = localSettings.CacheThumbnails;
        CacheOriginalImages = localSettings.CacheOriginalImages;
        ApplyImageCacheSettings();
        _lastOpenAlbumLink = albumOpenHistory.LastOpenAlbumLink;
        OpenAlbumLink = albumOpenHistory.LastOpenAlbumLink;
        PictureDefaultDownloadDirectoryPath = GetConfiguredOrDefaultDownloadDirectoryPath(localSettings.PictureDefaultDownloadDirectoryPath);
        UncategorizedDefaultDownloadDirectoryPath = GetConfiguredOrDefaultDownloadDirectoryPath(localSettings.UncategorizedDefaultDownloadDirectoryPath);
        NiceDefaultDownloadDirectoryPath = GetConfiguredOrDefaultDownloadDirectoryPath(localSettings.NiceDefaultDownloadDirectoryPath);
        OkDefaultDownloadDirectoryPath = GetConfiguredOrDefaultDownloadDirectoryPath(localSettings.OkDefaultDownloadDirectoryPath);
        TrashDefaultDownloadDirectoryPath = GetConfiguredOrDefaultDownloadDirectoryPath(localSettings.TrashDefaultDownloadDirectoryPath);
        LoadRecentAlbums(albumOpenHistory.RecentAlbums);
        _googleTokenSet = _tokenStore.Load();
        IsGoogleSignedIn = _googleTokenSet is not null;
        ResetCreateInputs();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await ResumePendingAlbumDeletionsAsync();
        await ResumePendingAlbumCreationAsync();
        await OpenLastAlbumOnStartupAsync(_lastOpenAlbumLink);
    }

    [RelayCommand]
    private void DismissFeedbackConflictNotice()
    {
        IsFeedbackConflictNoticeVisible = false;
        FeedbackConflictMessage = "";
    }

    [RelayCommand]
    private async Task MarkCurrentPhotoUncategorizedAsync()
    {
        await SetCurrentPhotoCategoryAsync("");
    }

    [RelayCommand]
    private async Task MarkCurrentPhotoNiceAsync()
    {
        await SetCurrentPhotoCategoryAsync("nice");
    }

    [RelayCommand]
    private async Task MarkCurrentPhotoOkAsync()
    {
        await SetCurrentPhotoCategoryAsync("ok");
    }

    [RelayCommand]
    private async Task MarkCurrentPhotoTrashAsync()
    {
        await SetCurrentPhotoCategoryAsync("trash");
    }

    [RelayCommand]
    private async Task RotateCurrentPhotoLeftAsync()
    {
        await RotateCurrentPhotoAsync(-90);
    }

    [RelayCommand]
    private async Task RotateCurrentPhotoRightAsync()
    {
        await RotateCurrentPhotoAsync(90);
    }

    [RelayCommand]
    private async Task MarkSelectedPhotosUncategorizedAsync()
    {
        await SetSelectedPhotosCategoryAsync("");
    }

    [RelayCommand]
    private async Task MarkSelectedPhotosNiceAsync()
    {
        await SetSelectedPhotosCategoryAsync("nice");
    }

    [RelayCommand]
    private async Task MarkSelectedPhotosOkAsync()
    {
        await SetSelectedPhotosCategoryAsync("ok");
    }

    [RelayCommand]
    private async Task MarkSelectedPhotosTrashAsync()
    {
        await SetSelectedPhotosCategoryAsync("trash");
    }

    [RelayCommand]
    private async Task RotateSelectedPhotosLeftAsync()
    {
        await RotateSelectedPhotosAsync(-90);
    }

    [RelayCommand]
    private async Task RotateSelectedPhotosRightAsync()
    {
        await RotateSelectedPhotosAsync(90);
    }

    [RelayCommand]
    private async Task MarkSelectedPhotosAsDuplicatesAsync()
    {
        await MarkSelectedPhotosAsDuplicatesCoreAsync();
    }

    [RelayCommand]
    private async Task RemoveCurrentPhotoFromDuplicatesAsync()
    {
        await RemoveCurrentPhotoFromDuplicatesCoreAsync();
    }

    [RelayCommand]
    private async Task ToggleCurrentPhotoBestInDuplicateGroupAsync()
    {
        await ToggleCurrentPhotoBestInDuplicateGroupCoreAsync();
    }

    [RelayCommand]
    private void ClearSelectedPhotos()
    {
        ClearBulkPhotoSelection();
    }

    [RelayCommand]
    private void SelectAllPhotosInCurrentTab()
    {
        foreach (var photo in GetPhotosForReviewTab(_activeReviewTabId))
        {
            photo.IsSelectedForBulk = true;
        }

        UpdateBulkPhotoSelectionState();
    }

    [RelayCommand]
    private void RemoveSelectedAlbumPhotoSources()
    {
        RemoveAlbumPhotos(GetSelectedAlbumPhotoSources());
    }

    [RelayCommand]
    private void SelectAllAlbumPhotoSources()
    {
        foreach (var photo in AlbumPhotos)
        {
            photo.IsSelected = true;
        }

        UpdateAlbumPhotoSourceSelectionState();
    }

    [RelayCommand]
    private void ClearSelectedAlbumPhotoSources()
    {
        ClearAlbumPhotoSourceSelection();
    }

    [RelayCommand]
    private void CommitFeedback()
    {
        if (!CanCommitFeedback)
        {
            return;
        }

        CommitConfirmationMessage = "Commit feedback? You will not be able to modify categories after this.";
        IsCommitConfirmationVisible = true;
    }

    [RelayCommand]
    private void CancelCommitFeedback()
    {
        IsCommitConfirmationVisible = false;
        CommitConfirmationMessage = "";
    }

    [RelayCommand]
    private async Task ConfirmCommitFeedbackAsync()
    {
        if (_feedbackSession is null || _currentReviewerIdentity is null || !CanCommitFeedback)
        {
            IsCommitConfirmationVisible = false;
            return;
        }

        IsCommitConfirmationVisible = false;
        CommitConfirmationMessage = "";

        try
        {
            var backend = await CreateFeedbackBackendAsync(CancellationToken.None);
            var result = await _reviewerFeedbackService.CommitAsync(
                _feedbackSession,
                _currentReviewerIdentity,
                backend,
                CancellationToken.None);

            _feedbackStatus = result.Status;
            IsFeedbackCommitted = true;
            UpdateFeedbackControlState();
            UpdateCurrentPhotoActionVisibility();
            await SyncFeedbackAsync();
            Status = result.RemoteWon
                ? "Feedback was already committed remotely. The remote commit was loaded."
                : "Feedback committed.";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    [RelayCommand]
    private void PassFeedback()
    {
        if (!CanPassFeedback)
        {
            return;
        }

        PassConfirmationMessage = "Pass on this album? Your feedback will not be taken into account.";
        IsPassConfirmationVisible = true;
    }

    [RelayCommand]
    private void CancelPassFeedback()
    {
        IsPassConfirmationVisible = false;
        PassConfirmationMessage = "";
    }

    [RelayCommand]
    private async Task ConfirmPassFeedbackAsync()
    {
        if (_feedbackSession is null || _currentReviewerIdentity is null || !CanPassFeedback)
        {
            IsPassConfirmationVisible = false;
            return;
        }

        IsPassConfirmationVisible = false;
        PassConfirmationMessage = "";

        try
        {
            var backend = await CreateFeedbackBackendAsync(CancellationToken.None);
            var result = await _reviewerFeedbackService.PassAsync(
                _feedbackSession,
                _currentReviewerIdentity,
                backend,
                CancellationToken.None);

            _feedbackStatus = result.Status;
            IsFeedbackPassed = true;
            UpdateFeedbackControlState();
            UpdateCurrentPhotoActionVisibility();
            await SyncFeedbackAsync();
            Status = result.RemoteWon
                ? "Feedback was already passed remotely. The remote pass was loaded."
                : "Feedback passed.";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenAlbumDownloadDialog()
    {
        if (Photos.Count == 0)
        {
            Status = "Open an album before downloading it.";
            return;
        }

        AlbumDownloadCategories.Clear();
        AddAlbumDownloadCategory("", "Uncategorized", UncategorizedDefaultDownloadDirectoryPath, "exclude");
        AddAlbumDownloadCategory("nice", "Nice", NiceDefaultDownloadDirectoryPath, "include");
        AddAlbumDownloadCategory("ok", "Ok", OkDefaultDownloadDirectoryPath, IsAuthorFlowVisible ? "archive" : "exclude");
        AddAlbumDownloadCategory("trash", "Trash", TrashDefaultDownloadDirectoryPath, "exclude");
        IsAlbumDownloadDialogVisible = true;
    }

    [RelayCommand]
    private void CancelAlbumDownloadDialog()
    {
        IsAlbumDownloadDialogVisible = false;
    }

    [RelayCommand]
    private async Task DownloadAlbumAsync()
    {
        var categories = AlbumDownloadCategories
            .Where(category => category.SelectedMode.Id != "exclude")
            .ToList();
        if (categories.Count == 0)
        {
            Status = "Select at least one category to download.";
            return;
        }

        foreach (var category in categories)
        {
            if (string.IsNullOrWhiteSpace(category.DestinationDirectoryPath))
            {
                Status = category.SelectedMode.Id == "archive"
                    ? $"Choose a destination archive file for {category.CategoryName}."
                    : $"Choose a destination directory for {category.CategoryName}.";
                return;
            }
        }

        _albumDownloadCancellation?.Cancel();
        _albumDownloadCancellation?.Dispose();
        _albumDownloadCancellation = new CancellationTokenSource();
        var cancellation = _albumDownloadCancellation;

        IsAlbumDownloadDialogVisible = false;
        IsAlbumDownloadProgressVisible = true;
        AlbumDownloadProgressValue = 0;
        AlbumDownloadProgressMaximum = Math.Max(1, categories.Sum(category => GetPhotosForCategory(category.CategoryKey).Count()));
        AlbumDownloadProgressMessage = "Preparing album download...";

        try
        {
            foreach (var category in categories)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var photos = GetPhotosForCategory(category.CategoryKey).ToList();
                if (photos.Count == 0)
                {
                    continue;
                }

                if (category.SelectedMode.Id == "archive")
                {
                    await DownloadAlbumCategoryArchiveAsync(category, photos, cancellation.Token);
                }
                else
                {
                    await DownloadAlbumCategoryFilesAsync(category, photos, cancellation.Token);
                }
            }

            AlbumDownloadProgressMessage = "Album download complete.";
            Status = "Album download complete.";
        }
        catch (OperationCanceledException)
        {
            AlbumDownloadProgressMessage = "Album download cancelled.";
            Status = "Album download cancelled.";
        }
        catch (Exception ex)
        {
            AlbumDownloadProgressMessage = ex.Message;
            Status = ex.Message;
        }
        finally
        {
            IsAlbumDownloadProgressVisible = false;
            if (ReferenceEquals(_albumDownloadCancellation, cancellation))
            {
                _albumDownloadCancellation.Dispose();
                _albumDownloadCancellation = null;
            }
        }
    }

    [RelayCommand]
    private void CancelAlbumDownloadProgress()
    {
        _albumDownloadCancellation?.Cancel();
    }

    [RelayCommand]
    private void LeaveFeedback()
    {
        if (!CanLeaveFeedback)
        {
            return;
        }

        LeaveConfirmationMessage = "Leave this album? The author will see that you left.";
        IsLeaveConfirmationVisible = true;
    }

    [RelayCommand]
    private void CancelLeaveFeedback()
    {
        IsLeaveConfirmationVisible = false;
        LeaveConfirmationMessage = "";
    }

    [RelayCommand]
    private async Task ConfirmLeaveFeedbackAsync()
    {
        if (_feedbackSession is null || _currentReviewerIdentity is null || _currentManifest is null || !CanLeaveFeedback)
        {
            IsLeaveConfirmationVisible = false;
            return;
        }

        IsLeaveConfirmationVisible = false;
        LeaveConfirmationMessage = "";
        var manifest = _currentManifest;

        try
        {
            var backend = await CreateFeedbackBackendAsync(CancellationToken.None);
            var result = await _reviewerFeedbackService.LeaveAsync(
                _feedbackSession,
                _currentReviewerIdentity,
                backend,
                CancellationToken.None);

            _feedbackStatus = result.Status;
            IsFeedbackLeft = true;
            UpdateFeedbackControlState();
            UpdateCurrentPhotoActionVisibility();
            await SyncFeedbackAsync();
            await CloseOpenedAlbumLocallyAsync(manifest);
            Status = result.RemoteWon
                ? "You had already left this album remotely. The remote state was loaded."
                : "You left the album.";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    [RelayCommand]
    private void CollectFeedback()
    {
        if (!CanCollectFeedback)
        {
            return;
        }

        CollectFeedbackConfirmationMessage = "Collect committed feedback and publish the merged categories?";
        IsCollectFeedbackConfirmationVisible = true;
    }

    [RelayCommand]
    private void CancelCollectFeedback()
    {
        IsCollectFeedbackConfirmationVisible = false;
        CollectFeedbackConfirmationMessage = "";
    }

    [RelayCommand]
    private async Task ConfirmCollectFeedbackAsync()
    {
        if (_currentManifest is null || !CanCollectFeedback)
        {
            IsCollectFeedbackConfirmationVisible = false;
            return;
        }

        IsCollectFeedbackConfirmationVisible = false;
        CollectFeedbackConfirmationMessage = "";

        try
        {
            IsBusy = true;
            var backend = await CreateFeedbackBackendAsync(CancellationToken.None);
            var result = await _reviewerFeedbackService.CollectFeedbackAsync(
                _currentManifest,
                backend,
                CancellationToken.None);

            await SyncFeedbackAsync();
            await RefreshFlowAsync(CancellationToken.None);
            _unfrozenCollectedPhotoCount = result.UnfrozenPhotoCount;
            _hasCollectedFeedback = true;
            UpdateFeedbackControlState();
            Status = $"Collected {result.ReviewerCount} feedback(s) for {result.PhotoCount} photo(s).";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task StartNextRoundAsync()
    {
        if (_currentManifest is null || !CanStartNextRound)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var backend = await CreateFeedbackBackendAsync(CancellationToken.None);
            var reviewerCount = await _reviewerFeedbackService.StartNextRoundAsync(
                _currentManifest,
                backend,
                CancellationToken.None);

            await SyncFeedbackAsync();
            await RefreshFlowAsync(CancellationToken.None);
            _unfrozenCollectedPhotoCount = 0;
            _hasCollectedFeedback = false;
            _isFeedbackFinalized = false;
            UpdateFeedbackControlState();
            Status = $"Started next round for {reviewerCount} reviewer(s).";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void FinalizeFeedback()
    {
        if (_currentManifest is null || !CanFinalizeFeedback)
        {
            return;
        }

        FinalizeConfirmationMessage = "Finalize this album? Reviewers will no longer be able to modify feedback.";
        IsFinalizeConfirmationVisible = true;
    }

    [RelayCommand]
    private void CancelFinalizeFeedback()
    {
        IsFinalizeConfirmationVisible = false;
        FinalizeConfirmationMessage = "";
    }

    [RelayCommand]
    private async Task ConfirmFinalizeFeedbackAsync()
    {
        if (_currentManifest is null || !CanFinalizeFeedback)
        {
            IsFinalizeConfirmationVisible = false;
            return;
        }

        IsFinalizeConfirmationVisible = false;
        FinalizeConfirmationMessage = "";

        try
        {
            IsBusy = true;
            var backend = await CreateFeedbackBackendAsync(CancellationToken.None);
            var result = await _reviewerFeedbackService.FinalizeAsync(
                _currentManifest,
                backend,
                CancellationToken.None);

            _hasCollectedFeedback = true;
            _isFeedbackFinalized = true;
            _unfrozenCollectedPhotoCount = 0;
            await SyncFeedbackAsync();
            await RefreshFlowAsync(CancellationToken.None);
            UpdateFeedbackControlState();
            UpdateCurrentPhotoActionVisibility();
            Status = $"Finalized album with {result.PhotoCount} photo(s).";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void DeleteAlbum()
    {
        if (_currentManifest is null || !CanDeleteAlbum)
        {
            return;
        }

        DeleteAlbumConfirmationMessage = "Delete this album permanently? This will remove the album photos, manifest, and feedback database from its storage backend.";
        IsDeleteAlbumConfirmationVisible = true;
    }

    [RelayCommand]
    private void CancelDeleteAlbum()
    {
        IsDeleteAlbumConfirmationVisible = false;
        DeleteAlbumConfirmationMessage = "";
    }

    [RelayCommand]
    private async Task ConfirmDeleteAlbumAsync()
    {
        if (_currentManifest is null || _currentReviewerIdentity is null || !CanDeleteAlbum)
        {
            IsDeleteAlbumConfirmationVisible = false;
            return;
        }

        IsDeleteAlbumConfirmationVisible = false;
        DeleteAlbumConfirmationMessage = "";
        var manifest = _currentManifest;
        var reviewer = _currentReviewerIdentity;

        try
        {
            IsBusy = true;
            Status = "Deleting album...";
            PrepareOpenAlbumForDeletion();
            var backend = await CreateFeedbackBackendAsync(manifest, CancellationToken.None);
            await ExecuteAlbumDeletionAsync(manifest, reviewer, backend, CancellationToken.None, showProgress: true);
        }
        catch (Exception ex) when (IsInfrastructureException(ex))
        {
            Status = ex.Message;
            _failedDeletingAlbumManifest = manifest;
            ShowInfrastructureError("Album deletion failed", ex);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelAlbumDeletionProgress()
    {
        if (_activeDeletingAlbumManifest is null)
        {
            return;
        }

        CancelAlbumDeletionConfirmationMessage = "Stop trying to delete this album? Picshare will forget the deletion and consider the album deleted, even if some backend files remain.";
        IsCancelAlbumDeletionConfirmationVisible = true;
    }

    [RelayCommand]
    private void KeepAlbumDeletionRunning()
    {
        IsCancelAlbumDeletionConfirmationVisible = false;
        CancelAlbumDeletionConfirmationMessage = "";
    }

    [RelayCommand]
    private async Task ForceCancelAlbumDeletionAsync()
    {
        var manifest = _activeDeletingAlbumManifest;
        IsCancelAlbumDeletionConfirmationVisible = false;
        CancelAlbumDeletionConfirmationMessage = "";
        _albumDeletionCancellation?.Cancel();
        await ApplyAlbumDeletionStoppedAsync(manifest);
        IsAlbumDeletionProgressVisible = false;
        _activeDeletingAlbumManifest = null;
    }

    [RelayCommand]
    private async Task AcknowledgeInfrastructureErrorAsync()
    {
        IsInfrastructureErrorVisible = false;
        InfrastructureErrorMessage = "";

        var failedCreation = _failedPendingAlbumCreation;
        var failedDeletion = _failedDeletingAlbumManifest;
        _failedPendingAlbumCreation = null;
        _failedDeletingAlbumManifest = null;

        if (failedCreation is not null)
        {
            await CancelPendingAlbumCreationAsync(failedCreation);
            return;
        }

        if (failedDeletion is not null)
        {
            _albumDeletionCancellation?.Cancel();
            await ApplyAlbumDeletionStoppedAsync(failedDeletion);
            IsAlbumDeletionProgressVisible = false;
            _activeDeletingAlbumManifest = null;
        }
    }

    private async Task ApplyAlbumDeletionStoppedAsync(AlbumManifest? manifest)
    {
        if (manifest is not null)
        {
            _albumDeletionService.ForgetPendingDeletion(manifest.AlbumId);
            await CloseOpenedAlbumLocallyAsync(manifest);
        }
        else
        {
            ClearOpenedAlbumState();
        }

        Status = "Album deletion was stopped and the album is considered deleted.";
    }

    private void ShowInfrastructureError(string title, Exception exception)
    {
        InfrastructureErrorMessage = $"{title}: {exception.Message}";
        IsInfrastructureErrorVisible = true;
    }

    private static bool IsInfrastructureException(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or HttpRequestException or TimeoutException ||
            exception is InvalidOperationException invalidOperation &&
            invalidOperation.Message.StartsWith("Google Drive request failed:", StringComparison.Ordinal);
    }

    [RelayCommand]
    private async Task ShowPreviousPhotoInCategoryAsync()
    {
        await MovePhotoViewerInCategoryAsync(-1);
    }

    [RelayCommand]
    private async Task ShowNextPhotoInCategoryAsync()
    {
        await MovePhotoViewerInCategoryAsync(1);
    }

    [RelayCommand]
    private void ClosePhotoViewer()
    {
        _photoViewerCancellation?.Cancel();
        _photoViewerCancellation?.Dispose();
        _photoViewerCancellation = null;
        PhotoViewerImage = null;
        PhotoViewerRotationDegrees = 0;
        PhotoViewerTitle = "";
        PhotoViewerStatus = "";
        IsPhotoViewerVisible = false;
        UpdatePhotoViewerDuplicateStripVisibility();
    }

    [RelayCommand]
    private void TogglePhotoViewerActions()
    {
        IsPhotoViewerActionsVisible = !IsPhotoViewerActionsVisible;
        UpdatePhotoViewerDuplicateStripVisibility();
    }

    public async Task OpenPhotoViewerAsync(AlbumPhotoViewModel photo)
    {
        _photoViewerCancellation?.Cancel();
        _photoViewerCancellation?.Dispose();
        _photoViewerCancellation = new CancellationTokenSource();
        var cancellation = _photoViewerCancellation;

        try
        {
            SelectViewedPhoto(photo);
            IsPhotoViewerVisible = true;
            IsPhotoViewerActionsVisible = true;
            UpdatePhotoViewerDuplicateStripVisibility();
            PhotoViewerTitle = photo.FileName;
            PhotoViewerRotationDegrees = photo.RotationDegrees;
            PhotoViewerStatus = "Loading";
            PhotoViewerImage = null;

            var viewerImage = await _imageCache.LoadOriginalBitmapAsync(
                photo.AlbumId,
                GetFullPhotoCacheFileName(photo),
                photo.DownloadUrl,
                _imageHttpClient,
                cancellation.Token);

            if (ReferenceEquals(_photoViewerCancellation, cancellation))
            {
                PhotoViewerImage = viewerImage;
                PhotoViewerStatus = "";
                _ = LoadViewedPhotoDisplayImageAsync(photo);
                _ = PreloadPhotoViewerDuplicatePhotosAsync();
            }
            else
            {
                viewerImage.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_photoViewerCancellation, cancellation))
            {
                PhotoViewerStatus = ex.Message;
            }
        }
    }

    public string CurrentPhotoDownloadFileName => _selectedViewedPhoto is { } photo
        ? GetSafeDownloadFileName(photo)
        : "photo";

    public string SelectedPhotosArchiveFileName => GetSafeArchiveFileName($"{CurrentAlbumTitle}_selected.zip");

    public string GetAlbumCategoryArchiveFileName(AlbumDownloadCategoryViewModel category)
    {
        return GetSafeArchiveFileName($"{CurrentAlbumTitle}_{category.CategoryName}.zip");
    }

    public async Task DownloadCurrentPhotoAsync(string destinationFilePath)
    {
        if (string.IsNullOrWhiteSpace(destinationFilePath))
        {
            Status = "Choose a destination file.";
            return;
        }

        await CopyCurrentOriginalPhotoAsync(destinationFilePath, "Downloaded");
    }

    [RelayCommand]
    private void CancelGoogleAuthorization()
    {
        _pendingGoogleAuthorizationManifest = null;
        IsGoogleAuthorizationRequired = false;
        GoogleAuthorizationMessage = "";
        Status = "Google authorization cancelled.";
    }

    [RelayCommand]
    private void ClearGoogleAuthorization()
    {
        _googleSignInCancellation?.Cancel();
        _tokenStore.Clear();
        _googleTokenSet = null;
        IsGoogleSignedIn = false;
        Status = "Google authorization cleared.";
    }

    [RelayCommand]
    private void ShowSettingsPanel()
    {
        IsSettingsPanelVisible = true;
    }

    [RelayCommand]
    private void CloseSettingsPanel()
    {
        SaveLocalUserSettings();
        IsSettingsPanelVisible = false;
    }

    [RelayCommand]
    private async Task ClearImageCacheAsync()
    {
        try
        {
            IsBusy = true;
            foreach (var photo in Photos)
            {
                photo.StopViewportLoad();
                photo.ReleaseCachedImage();
            }

            ClosePhotoViewer();
            await _imageCache.ClearAsync();
            Status = "Image cache cleared.";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CreateAlbumAsync()
    {
        await CreateAlbumCoreAsync(clearExistingDestination: false);
    }

    [RelayCommand]
    private void CancelClearAlbumDestination()
    {
        IsClearAlbumDestinationConfirmationVisible = false;
        ClearAlbumDestinationConfirmationMessage = "";
        Status = "Album creation cancelled.";
    }

    [RelayCommand]
    private async Task ConfirmClearAlbumDestinationAsync()
    {
        IsClearAlbumDestinationConfirmationVisible = false;
        ClearAlbumDestinationConfirmationMessage = "";
        await CreateAlbumCoreAsync(clearExistingDestination: true);
    }

    private async Task CreateAlbumCoreAsync(bool clearExistingDestination)
    {
        if (IsBusy)
        {
            return;
        }

        if (SelectedAlbumType is null)
        {
            Status = "Choose the album type before creating the album.";
            return;
        }

        if (SelectedAlbumType.Id == LocalToGoogleDriveAlbumTypeId && !IsGoogleSignedIn)
        {
            Status = "Sign in with Google before creating the album.";
            return;
        }

        if (SelectedAlbumType.Id == LocalToGoogleDriveAlbumTypeId && !IsDriveFolderSelected)
        {
            Status = "Select a Google Drive destination folder before creating the album.";
            return;
        }

        if (SelectedAlbumType.Id == LocalAlbumTypeId && !IsLocalAlbumDestinationSelected)
        {
            Status = "Select a local destination folder before creating the album.";
            return;
        }

        if (SelectedAlbumType.Id == LocalAlbumTypeId && CreateLocalReviewerIdentity() is null)
        {
            Status = "Set an anonymous reviewer name in settings before creating a local album.";
            IsSettingsPanelVisible = true;
            return;
        }

        try
        {
            IsBusy = true;
            var title = string.IsNullOrWhiteSpace(AlbumTitle) ? "Picshare album" : AlbumTitle.Trim();
            if (!clearExistingDestination)
            {
                Status = "Checking album destination...";
                var inspection = await _albumCreationService.InspectDestinationAsync(
                    SelectedAlbumType.Id,
                    title,
                    string.IsNullOrWhiteSpace(ParentDriveFolderId) ? null : ParentDriveFolderId.Trim(),
                    LocalAlbumDestinationPath,
                    GetGoogleAccessTokenAsync,
                    CancellationToken.None);
                if (inspection.HasItems)
                {
                    ClearAlbumDestinationConfirmationMessage =
                        $"The destination album folder \"{inspection.DisplayName}\" already exists. Delete it and start the upload in a new folder?";
                    IsClearAlbumDestinationConfirmationVisible = true;
                    return;
                }
            }
            else
            {
                Status = "Deleting existing album destination...";
                await _albumCreationService.ClearDestinationAsync(
                    SelectedAlbumType.Id,
                    title,
                    string.IsNullOrWhiteSpace(ParentDriveFolderId) ? null : ParentDriveFolderId.Trim(),
                    LocalAlbumDestinationPath,
                    GetGoogleAccessTokenAsync,
                    GetMaximumParallelism(),
                    CancellationToken.None);
            }

            Photos.Clear();
            ShareLink = "";
            DriveFolderLink = "";

            _albumCreationCancellation?.Cancel();
            _albumCreationCancellation?.Dispose();
            _albumCreationCancellation = new CancellationTokenSource();
            var cancellation = _albumCreationCancellation;
            _activePendingAlbumCreation = await _albumCreationService.CreatePendingCreationAsync(
                SelectedAlbumType.Id,
                title,
                Math.Max(0, TargetNicePhotoCount),
                string.IsNullOrWhiteSpace(ParentDriveFolderId) ? null : ParentDriveFolderId.Trim(),
                LocalAlbumDestinationPath,
                SelectedAlbumType.Id == LocalAlbumTypeId
                    ? CreateLocalReviewerIdentity()!
                    : CreateGoogleReviewerIdentity(_googleTokenSet!),
                AlbumPhotos.Select(photo => photo.Source).ToList(),
                cancellation.Token);

            IsAlbumCreationProgressVisible = true;
            AlbumCreationProgressValue = 0;
            AlbumCreationProgressMaximum = Math.Max(1, AlbumPhotos.Count + 3);
            Status = SelectedAlbumType.Id == LocalAlbumTypeId
                ? "Creating local album..."
                : "Uploading selected local photos to Google Drive...";
            var result = await ResumePendingAlbumCreationCoreAsync(_activePendingAlbumCreation, cancellation.Token);
            await CompleteAlbumCreationAsync(result);
        }
        catch (OperationCanceledException)
        {
            Status = "Album creation cancellation requested.";
            if (_activePendingAlbumCreation is not null)
            {
                await CancelPendingAlbumCreationAsync(_activePendingAlbumCreation);
            }
        }
        catch (Exception ex) when (IsInfrastructureException(ex))
        {
            Status = ex.Message;
            _failedPendingAlbumCreation = _activePendingAlbumCreation;
            ShowInfrastructureError("Album creation failed", ex);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            _albumCreationCancellation?.Dispose();
            _albumCreationCancellation = null;
            _activePendingAlbumCreation = null;
            IsAlbumCreationProgressVisible = false;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelAlbumCreationProgress()
    {
        _albumCreationCancellation?.Cancel();
    }

    private async Task ResumePendingAlbumCreationAsync()
    {
        var pendingCreation = await _albumCreationService.LoadPendingCreationAsync(CancellationToken.None);
        if (pendingCreation is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            IsAlbumCreationProgressVisible = true;
            _activePendingAlbumCreation = pendingCreation;
            _albumCreationCancellation?.Cancel();
            _albumCreationCancellation?.Dispose();
            _albumCreationCancellation = new CancellationTokenSource();
            Status = "Resuming album creation...";
            var result = await ResumePendingAlbumCreationCoreAsync(pendingCreation, _albumCreationCancellation.Token);
            await CompleteAlbumCreationAsync(result);
        }
        catch (OperationCanceledException)
        {
            if (_activePendingAlbumCreation is not null)
            {
                await CancelPendingAlbumCreationAsync(_activePendingAlbumCreation);
            }
        }
        catch (Exception ex) when (IsInfrastructureException(ex))
        {
            Status = ex.Message;
            _failedPendingAlbumCreation = _activePendingAlbumCreation;
            ShowInfrastructureError("Album creation failed", ex);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            _albumCreationCancellation?.Dispose();
            _albumCreationCancellation = null;
            _activePendingAlbumCreation = null;
            IsAlbumCreationProgressVisible = false;
            IsBusy = false;
        }
    }

    private async Task<AlbumCreationResumeResult> ResumePendingAlbumCreationCoreAsync(
        PendingAlbumCreation pendingCreation,
        CancellationToken cancellationToken)
    {
        var progress = new Progress<AlbumCreationProgress>(progress =>
        {
            AlbumCreationProgressMessage = progress.Message;
            AlbumCreationProgressValue = progress.Value;
            AlbumCreationProgressMaximum = Math.Max(1, progress.Maximum);
            Status = progress.Message;
        });

        return await _albumCreationService.ResumeAsync(
            pendingCreation,
            GetGoogleAccessTokenAsync,
            progress,
            GetMaximumParallelism(),
            cancellationToken);
    }

    private async Task CompleteAlbumCreationAsync(AlbumCreationResumeResult result)
    {
        ShareLink = result.PicshareLink;
        DriveFolderLink = result.AlbumLocation;
        OpenAlbumLink = result.PicshareLink;
        Status = $"Album created with {result.Manifest.Photos.Count} photo(s).";
        await LoadAlbumAsync(result.Manifest);
        SaveOpenedAlbumReference(result.Manifest, result.PicshareLink);
    }

    private async Task CancelPendingAlbumCreationAsync(PendingAlbumCreation pendingCreation)
    {
        Status = "Cancelling album creation...";
        var manifest = _albumCreationService.CreateDeletionManifest(pendingCreation);
        _albumCreationService.ForgetPendingCreation(pendingCreation.AlbumId);
        if (manifest is null)
        {
            Status = "Album creation cancelled.";
            return;
        }

        try
        {
            IsAlbumCreationProgressVisible = false;
            var backend = await CreateFeedbackBackendAsync(manifest, CancellationToken.None);
            await ExecuteAlbumDeletionAsync(manifest, pendingCreation.Author, backend, CancellationToken.None, showProgress: true);
            Status = "Album creation cancelled and created storage was deleted.";
        }
        catch (OperationCanceledException)
        {
            Status = "Album deletion was stopped and the album is considered deleted.";
        }
        catch (Exception ex) when (IsInfrastructureException(ex))
        {
            Status = ex.Message;
            _failedDeletingAlbumManifest = manifest;
            ShowInfrastructureError("Album deletion failed", ex);
        }
        catch (Exception ex)
        {
            Status = $"Album creation was cancelled. Deletion will continue later: {ex.Message}";
        }
    }

    public async Task SignInGoogleAsync(Func<Uri, Task> launchBrowserAsync, bool loadDriveFoldersAfterSignIn = false)
    {
        if (IsGoogleSignInPending)
        {
            return;
        }

        var clientId = _settingsProvider.GoogleOAuthClientId;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            Status = _settingsProvider.MissingGoogleOAuthClientIdMessage;
            return;
        }

        var previousTokenSet = _googleTokenSet;
        var wasGoogleSignedIn = IsGoogleSignedIn;

        try
        {
            _googleSignInCancellation?.Cancel();
            _googleSignInCancellation?.Dispose();
            _googleSignInCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));

            IsGoogleSignInPending = true;
            IsGoogleSignedIn = false;
            GoogleSignInUrl = "";
            GoogleSignInMessage = "Complete Google sign-in in your browser. This window will update automatically.";
            Status = "Waiting for Google sign-in in the browser...";

            var signedInToken = await _oauthClient.SignInWithLoopbackAsync(
                clientId.Trim(),
                _settingsProvider.GoogleOAuthClientSecret,
                async launch =>
                {
                    GoogleSignInUrl = launch.AuthorizationUrl.ToString();
                    await launchBrowserAsync(launch.AuthorizationUrl);
                },
                _googleSignInCancellation.Token);
            _googleTokenSet = await PrepareGoogleTokenAsync(signedInToken, _googleSignInCancellation.Token);
            _tokenStore.Save(_googleTokenSet);
            IsGoogleSignedIn = true;
            OnPropertyChanged(nameof(GoogleConnectionStatus));
            IsGoogleAuthorizationRequired = false;
            GoogleAuthorizationMessage = "";

            if (_pendingGoogleAuthorizationManifest is not null)
            {
                await CompletePendingGoogleAuthorizedAlbumOpenAsync();
            }
            else
            {
                Status = "Google Drive is connected.";
            }

            if (loadDriveFoldersAfterSignIn)
            {
                await LoadDriveFoldersAsync();
            }
        }
        catch (OperationCanceledException)
        {
            _googleTokenSet = previousTokenSet;
            IsGoogleSignedIn = wasGoogleSignedIn && previousTokenSet is not null;
            Status = "Google sign-in timed out or was cancelled.";
        }
        catch (Exception ex)
        {
            _googleTokenSet = previousTokenSet;
            IsGoogleSignedIn = wasGoogleSignedIn && previousTokenSet is not null;
            Status = ex.Message;
        }
        finally
        {
            IsGoogleSignInPending = false;
            _googleSignInCancellation?.Dispose();
            _googleSignInCancellation = null;
        }
    }

    [RelayCommand]
    private void CancelGoogleSignIn()
    {
        _googleSignInCancellation?.Cancel();
    }

    [RelayCommand]
    private async Task LoadDriveFoldersAsync()
    {
        if (!IsGoogleSignedIn)
        {
            Status = "Sign in with Google before choosing a Drive folder.";
            return;
        }

        try
        {
            IsCreateDriveFolderDialogVisible = false;
            IsDriveFolderPickerVisible = true;
            _driveFolderPath.Clear();
            _driveFolderPath.Add(new DriveFolderLocation("root", "My Drive", true));
            await OpenDriveFolderAsync(_driveFolderPath[0]);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    [RelayCommand]
    private async Task LoadMoreDriveItemsAsync()
    {
        if (!IsDriveFolderPickerVisible || !HasMoreDriveItems)
        {
            return;
        }

        await LoadDriveItemsPageAsync(clearExisting: false);
    }

    public async Task OpenDriveItemAsync(DriveItemViewModel item)
    {
        if (!item.IsFolder)
        {
            Status = "Files are shown for context but cannot be selected.";
            return;
        }

        await OpenDriveFolderAsync(new DriveFolderLocation(item.Id, item.Name, item.CanAddChildren));
    }

    [RelayCommand]
    private async Task GoToParentDriveFolderAsync()
    {
        if (_driveFolderPath.Count <= 1)
        {
            return;
        }

        _driveFolderPath.RemoveAt(_driveFolderPath.Count - 1);
        await OpenDriveFolderAsync(_driveFolderPath[^1], addToPath: false);
    }

    [RelayCommand]
    private void ShowCreateDriveFolderDialog()
    {
        if (!CurrentDriveFolderCanBeUsed)
        {
            Status = "You do not have permission to create a folder here.";
            return;
        }

        NewDriveFolderName = "";
        IsCreateDriveFolderDialogVisible = true;
    }

    [RelayCommand]
    private void CancelCreateDriveFolder()
    {
        NewDriveFolderName = "";
        IsCreateDriveFolderDialogVisible = false;
    }

    [RelayCommand]
    private async Task CreateDriveFolderAsync()
    {
        if (!IsGoogleSignedIn)
        {
            Status = "Sign in with Google before creating a Drive folder.";
            return;
        }

        if (!CurrentDriveFolderCanBeUsed)
        {
            Status = "You do not have permission to create a folder here.";
            return;
        }

        var folderName = NewDriveFolderName.Trim();
        if (string.IsNullOrWhiteSpace(folderName))
        {
            Status = "Enter a folder name.";
            return;
        }

        try
        {
            IsBusy = true;
            Status = $"Creating Drive folder {folderName}...";

            var accessToken = await GetGoogleAccessTokenAsync(CancellationToken.None);
            var client = new GoogleDriveRestClient(accessToken);
            var parentFolderId = string.IsNullOrWhiteSpace(CurrentDriveFolderId) ? "root" : CurrentDriveFolderId;
            var folder = await client.CreateFolderAsync(folderName, parentFolderId, CancellationToken.None);
            var folderViewModel = new DriveItemViewModel(
                folder.Id,
                folder.Name,
                "application/vnd.google-apps.folder",
                true);

            DriveItems.Insert(0, folderViewModel);
            NewDriveFolderName = "";
            IsCreateDriveFolderDialogVisible = false;
            Status = $"Created Drive folder {folder.Name}.";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void UseCurrentDriveFolder()
    {
        if (!CurrentDriveFolderCanBeUsed)
        {
            Status = "You do not have permission to use this folder as an album destination.";
            return;
        }

        ParentDriveFolderId = CurrentDriveFolderId == "root" ? "" : CurrentDriveFolderId;
        SelectedDriveFolderName = string.Join(" / ", _driveFolderPath.Select(folder => folder.Name));
        IsDriveFolderSelected = true;
        IsDriveFolderPickerVisible = false;
        IsCreateDriveFolderDialogVisible = false;
        Status = $"Album destination set to {SelectedDriveFolderName}.";
    }

    [RelayCommand]
    private void CancelDriveFolderPicker()
    {
        IsDriveFolderPickerVisible = false;
        IsCreateDriveFolderDialogVisible = false;
        DriveItems.Clear();
        _driveNextPageToken = null;
        OnPropertyChanged(nameof(HasMoreDriveItems));
        Status = "Drive folder selection cancelled.";
    }

    public void SetLocalAlbumDestination(IStorageFolder folder)
    {
        var localPath = folder.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(localPath))
        {
            Status = "The selected destination must be a local filesystem folder.";
            return;
        }

        LocalAlbumDestinationPath = Path.GetFullPath(localPath);
        SelectedLocalAlbumDestinationName = LocalAlbumDestinationPath;
        IsLocalAlbumDestinationSelected = true;
        Status = $"Local album destination set to {SelectedLocalAlbumDestinationName}.";
    }

    public void SetPictureDefaultDownloadDirectory(IStorageFolder folder)
    {
        SetDefaultDownloadDirectory(folder, value => PictureDefaultDownloadDirectoryPath = value, "Picture default download directory");
    }

    public void SetUncategorizedDefaultDownloadDirectory(IStorageFolder folder)
    {
        SetDefaultDownloadDirectory(folder, value => UncategorizedDefaultDownloadDirectoryPath = value, "Uncategorized default download directory");
    }

    public void SetNiceDefaultDownloadDirectory(IStorageFolder folder)
    {
        SetDefaultDownloadDirectory(folder, value => NiceDefaultDownloadDirectoryPath = value, "Nice default download directory");
    }

    public void SetOkDefaultDownloadDirectory(IStorageFolder folder)
    {
        SetDefaultDownloadDirectory(folder, value => OkDefaultDownloadDirectoryPath = value, "Ok default download directory");
    }

    public void SetTrashDefaultDownloadDirectory(IStorageFolder folder)
    {
        SetDefaultDownloadDirectory(folder, value => TrashDefaultDownloadDirectoryPath = value, "Trash default download directory");
    }

    [RelayCommand]
    private void SetPictureDefaultDownloadDirectoryToDefault()
    {
        SetDefaultDownloadDirectory(value => PictureDefaultDownloadDirectoryPath = value, "Picture default download directory");
    }

    [RelayCommand]
    private void SetUncategorizedDefaultDownloadDirectoryToDefault()
    {
        SetDefaultDownloadDirectory(value => UncategorizedDefaultDownloadDirectoryPath = value, "Uncategorized default download directory");
    }

    [RelayCommand]
    private void SetNiceDefaultDownloadDirectoryToDefault()
    {
        SetDefaultDownloadDirectory(value => NiceDefaultDownloadDirectoryPath = value, "Nice default download directory");
    }

    [RelayCommand]
    private void SetOkDefaultDownloadDirectoryToDefault()
    {
        SetDefaultDownloadDirectory(value => OkDefaultDownloadDirectoryPath = value, "Ok default download directory");
    }

    [RelayCommand]
    private void SetTrashDefaultDownloadDirectoryToDefault()
    {
        SetDefaultDownloadDirectory(value => TrashDefaultDownloadDirectoryPath = value, "Trash default download directory");
    }

    private async Task OpenDriveFolderAsync(DriveFolderLocation folder, bool addToPath = true)
    {
        if (addToPath && (_driveFolderPath.Count == 0 || _driveFolderPath[^1].Id != folder.Id))
        {
            _driveFolderPath.Add(folder);
        }

        CurrentDriveFolderId = folder.Id;
        CurrentDriveFolderCanBeUsed = folder.CanAddChildren;
        CurrentDriveFolderName = string.Join(" / ", _driveFolderPath.Select(location => location.Name));
        NewDriveFolderName = "";
        await LoadDriveItemsPageAsync(clearExisting: true);
    }

    private async Task LoadDriveItemsPageAsync(bool clearExisting)
    {
        try
        {
            IsBusy = true;

            if (clearExisting)
            {
                DriveItems.Clear();
                _driveNextPageToken = null;
                OnPropertyChanged(nameof(HasMoreDriveItems));
            }

            var accessToken = await GetGoogleAccessTokenAsync(CancellationToken.None);
            var client = new GoogleDriveRestClient(accessToken);
            var page = await client.ListChildrenAsync(CurrentDriveFolderId, _driveNextPageToken, 100, CancellationToken.None);

            foreach (var item in page.Files)
            {
                DriveItems.Add(new DriveItemViewModel(item));
            }

            _driveNextPageToken = page.NextPageToken;
            OnPropertyChanged(nameof(HasMoreDriveItems));
            Status = $"Loaded {DriveItems.Count} Drive item(s).";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SetFolderDateImportFolders(IEnumerable<IStorageFolder> folders)
    {
        _folderDateImportFolders.Clear();
        _folderDateImportFolders.AddRange(folders);
        FolderDateImportSourceText = string.Join(Environment.NewLine, _folderDateImportFolders.Select(folder =>
            folder.TryGetLocalPath() ?? folder.Path.ToString()));
        IsFolderDateImportVisible = _folderDateImportFolders.Count > 0;
        ImportCandidates.Clear();
        RefreshImportCandidateRows();
        OnPropertyChanged(nameof(IsImportCandidatesVisible));
        Status = "Choose a date and scan the selected folders.";
    }

    public void CloseFolderDateImport()
    {
        IsFolderDateImportVisible = false;
        FolderDateImportSourceText = "";
        _folderDateImportFolders.Clear();
        ImportCandidates.Clear();
        RefreshImportCandidateRows();
        OnPropertyChanged(nameof(IsImportCandidatesVisible));
    }

    [RelayCommand]
    private void CancelFolderDateImport()
    {
        CloseFolderDateImport();
        Status = "Folder/date import cancelled.";
    }

    [RelayCommand]
    private async Task ScanFolderDateImportAsync()
    {
        try
        {
            if (_folderDateImportFolders.Count == 0)
            {
                Status = "Choose one or more folders before scanning.";
                return;
            }

            IsBusy = true;
            Status = "Scanning photos...";

            var importDate = DateOnly.FromDateTime((ImportDate ?? DateTimeOffset.Now).Date);
            var photos = await _photoScanner.FindPhotosAsync(_folderDateImportFolders, importDate, CancellationToken.None);
            var existingSortKeys = AlbumPhotos
                .Select(photo => photo.Source.SortKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            ImportCandidates.Clear();
            foreach (var photo in photos)
            {
                if (existingSortKeys.Contains(photo.SortKey))
                {
                    continue;
                }

                var photoViewModel = new AlbumPhotoSourceViewModel(photo);
                ImportCandidates.Add(photoViewModel);
            }

            RefreshImportCandidateRows();
            OnPropertyChanged(nameof(IsImportCandidatesVisible));
            Status = $"Found {ImportCandidates.Count} photo(s) for {importDate:yyyy-MM-dd}.";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void AddManualPhotoFiles(IEnumerable<IStorageFile> files)
    {
        var added = 0;
        var existingSortKeys = AlbumPhotos
            .Select(photo => photo.Source.SortKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var localPath = file.TryGetLocalPath();
            var key = !string.IsNullOrWhiteSpace(localPath)
                ? Path.GetFullPath(localPath)
                : file.Path.AbsoluteUri;

            if (!existingSortKeys.Add(key))
            {
                continue;
            }

            var photoViewModel = new AlbumPhotoSourceViewModel(new PhotoUploadSource(
                file.Name,
                key,
                async () => await file.OpenReadAsync(),
                string.IsNullOrWhiteSpace(localPath) ? null : Path.GetFullPath(localPath)));
            AlbumPhotos.Add(photoViewModel);
            added++;
        }

        RefreshAlbumPhotoRows();
        CloseFolderDateImport();
        Status = $"Added {added} photo(s) to the album.";
    }

    [RelayCommand]
    private void AddAllImportCandidates()
    {
        AddImportCandidates(ImportCandidates);
    }

    [RelayCommand]
    private void ClearImportCandidates()
    {
        ImportCandidates.Clear();
        RefreshImportCandidateRows();
        OnPropertyChanged(nameof(IsImportCandidatesVisible));
    }

    public void AddImportCandidates(IEnumerable<AlbumPhotoSourceViewModel> candidates)
    {
        var added = 0;
        var existingSortKeys = AlbumPhotos
            .Select(photo => photo.Source.SortKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates.ToList())
        {
            if (!existingSortKeys.Add(candidate.Source.SortKey))
            {
                continue;
            }

            candidate.IsSelected = false;
            AlbumPhotos.Add(candidate);
            added++;
        }

        RefreshAlbumPhotoRows();
        CloseFolderDateImport();
        Status = $"Added {added} photo(s) to the album.";
    }

    public void RemoveAlbumPhotos(IEnumerable<AlbumPhotoSourceViewModel> photos)
    {
        var removed = 0;
        foreach (var photo in photos.ToList())
        {
            if (AlbumPhotos.Remove(photo))
            {
                removed++;
            }
        }

        SelectedAlbumPhoto = null;
        RefreshAlbumPhotoRows();
        Status = $"Removed {removed} photo(s) from the album.";
        UpdateAlbumPhotoSourceSelectionState();
    }

    public void ToggleAlbumPhotoSourceSelection(AlbumPhotoSourceViewModel photo)
    {
        photo.IsSelected = !photo.IsSelected;
        UpdateAlbumPhotoSourceSelectionState();
    }

    public void ToggleImportCandidateSelection(AlbumPhotoSourceViewModel photo)
    {
        photo.IsSelected = !photo.IsSelected;
    }

    [RelayCommand]
    private void CancelAlbumCreation()
    {
        ResetCreateInputs();
        SelectedAlbumType = null;
        ShareLink = "";
        DriveFolderLink = "";
        IsDriveFolderPickerVisible = false;
        Status = "Create a Google Drive album or open a shared Picshare link.";
    }

    [RelayCommand]
    private async Task OpenAlbumAsync()
    {
        await OpenAlbumFromLinkAsync(OpenAlbumLink, saveHistory: true, showBusy: true);
    }

    [RelayCommand]
    private async Task OpenRecentAlbumAsync(RecentAlbumViewModel? recentAlbum)
    {
        if (recentAlbum is null)
        {
            return;
        }

        OpenAlbumLink = recentAlbum.Link;
        await OpenAlbumFromLinkAsync(recentAlbum.Link, saveHistory: true, showBusy: true);
    }

    private async Task OpenAlbumFromLinkAsync(string link, bool saveHistory, bool showBusy)
    {
        if (IsBusy)
        {
            return;
        }

        var localManifestPath = AlbumLinkParser.TryGetLocalManifestPath(link);
        var manifestFileId = localManifestPath is null
            ? AlbumLinkParser.TryGetManifestFileId(link)
            : null;
        if (string.IsNullOrWhiteSpace(manifestFileId) && string.IsNullOrWhiteSpace(localManifestPath))
        {
            Status = "Paste a Picshare link, Google Drive file link, manifest file id, or local album.json path.";
            return;
        }

        try
        {
            if (showBusy)
            {
                IsBusy = true;
            }

            Status = "Opening album...";
            var manifest = localManifestPath is not null
                ? await _albumLoader.LoadFromLocalFileAsync(localManifestPath, CancellationToken.None)
                : await _albumLoader.LoadFromPublicDriveFileAsync(manifestFileId!, CancellationToken.None);
            DriveFolderLink = manifest.GoogleDrive?.AlbumFolderUrl ?? manifest.LocalFileSystem?.RootPath ?? "";
            ShareLink = GetAlbumShareLink(manifest);

            if (await RequiresGoogleAuthorizationPromptAsync(manifest))
            {
                PreparePendingAuthorizedAlbumOpen(manifest);
                return;
            }

            if (UsesLocalFileSystemBackend(manifest) && CreateLocalReviewerIdentity() is null)
            {
                StopFeedbackSync();
                StopFlowMonitor();
                _currentManifest = null;
                _feedbackSession = null;
                _feedbackDatabase = null;
                _feedbackStatus = null;
                _currentReviewerIdentity = null;
                CurrentAlbumTitle = manifest.Title;
                Photos.Clear();
                ClearCategoryRows();
                SelectViewedPhoto(null);
                ClosePhotoViewer();
                IsSettingsPanelVisible = true;
                Status = "Set an anonymous reviewer name in settings before opening this local album.";
                return;
            }

            await LoadAlbumAsync(manifest);
            if (_currentManifest is not null)
            {
                if (saveHistory)
                {
                    SaveOpenedAlbumReference(manifest, ShareLink);
                }

                Status = $"Opened {manifest.Title} with {manifest.Photos.Count} photo(s).";
            }
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            if (showBusy)
            {
                IsBusy = false;
            }
        }
    }

    private async Task OpenLastAlbumOnStartupAsync(string link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return;
        }

        await OpenAlbumFromLinkAsync(link, saveHistory: false, showBusy: true);
    }

    private void PreparePendingAuthorizedAlbumOpen(AlbumManifest manifest)
    {
        StopFeedbackSync();
        StopFlowMonitor();
        _currentManifest = null;
        _feedbackSession = null;
        _feedbackDatabase = null;
        _feedbackStatus = null;
        _currentReviewerIdentity = null;
        IsFeedbackCommitted = false;
        IsFeedbackPassed = false;
        IsCommitConfirmationVisible = false;
        CommitConfirmationMessage = "";
        IsPassConfirmationVisible = false;
        PassConfirmationMessage = "";
        IsLeaveConfirmationVisible = false;
        LeaveConfirmationMessage = "";
        IsFeedbackLeft = false;
        IsCollectFeedbackConfirmationVisible = false;
        CollectFeedbackConfirmationMessage = "";
        CanCollectFeedback = false;
        IsCollectFeedbackVisible = false;
        CanStartNextRound = false;
        CanFinalizeFeedback = false;
        _unfrozenCollectedPhotoCount = 0;
        _hasCollectedFeedback = false;
        _isFeedbackFinalized = false;
        IsAuthorFlowVisible = false;
        ClearFlowReviewers();
        _pendingGoogleAuthorizationManifest = manifest;
        CurrentAlbumTitle = manifest.Title;
        Photos.Clear();
        ClearCategoryRows();
        SelectViewedPhoto(null);
        UpdateFeedbackControlState();
        ClosePhotoViewer();
        GoogleAuthorizationMessage = "Sign in with Google to open this Google Drive album.";
        IsGoogleAuthorizationRequired = true;
        Status = "Google authorization is required before this album can be opened.";
    }

    private async Task LoadAlbumAsync(AlbumManifest manifest)
    {
        StopFeedbackSync();
        StopFlowMonitor();
        _currentManifest = manifest;
        _feedbackSession = null;
        _feedbackDatabase = null;
        _feedbackStatus = null;
        _currentReviewerIdentity = null;
        IsFeedbackCommitted = false;
        IsFeedbackPassed = false;
        IsCommitConfirmationVisible = false;
        CommitConfirmationMessage = "";
        IsPassConfirmationVisible = false;
        PassConfirmationMessage = "";
        IsLeaveConfirmationVisible = false;
        LeaveConfirmationMessage = "";
        IsFeedbackLeft = false;
        IsCollectFeedbackConfirmationVisible = false;
        CollectFeedbackConfirmationMessage = "";
        IsFinalizeConfirmationVisible = false;
        FinalizeConfirmationMessage = "";
        IsDeleteAlbumConfirmationVisible = false;
        DeleteAlbumConfirmationMessage = "";
        CanCollectFeedback = false;
        IsCollectFeedbackVisible = false;
        CanStartNextRound = false;
        CanFinalizeFeedback = false;
        CanDeleteAlbum = false;
        _unfrozenCollectedPhotoCount = 0;
        _hasCollectedFeedback = false;
        _isFeedbackFinalized = false;
        IsAuthorFlowVisible = false;
        FlowStatus = "";
        ClearFlowReviewers();
        UpdateFeedbackControlState();

        CurrentAlbumTitle = manifest.Title;
        Photos.Clear();
        ClearCategoryRows();
        SelectViewedPhoto(null);
        ClosePhotoViewer();
        ClearBulkPhotoSelection();

        foreach (var photo in manifest.Photos)
        {
            Photos.Add(new AlbumPhotoViewModel(
                manifest.AlbumId,
                photo.Id,
                photo.FileName,
                photo.DownloadUrl,
                photo.ThumbnailDownloadUrl));
        }

        if (UsesReviewerFeedbackBackend(manifest))
        {
            await LoadReviewerFeedbackAsync(manifest);
        }

        ApplyFeedbackDatabaseToPhotos();
    }

    public async Task StartPhotoViewportLoadAsync(AlbumPhotoViewModel photo)
    {
        await photo.StartViewportLoadAsync(_imageCache, _imageHttpClient);
        if (photo.DuplicateStackPhoto is not null && !ReferenceEquals(photo.DuplicateStackPhoto, photo))
        {
            await photo.DuplicateStackPhoto.StartViewportLoadAsync(_imageCache, _imageHttpClient);
        }
    }

    public void StopPhotoViewportLoad(AlbumPhotoViewModel photo)
    {
        photo.StopViewportLoad();
    }

    public void PrioritizePhotoViewportLoads(IReadOnlyList<AlbumPhotoViewModel> photos)
    {
        var priorityPhotos = photos
            .SelectMany(photo => photo.DuplicateStackPhoto is not null && !ReferenceEquals(photo.DuplicateStackPhoto, photo)
                ? new[] { photo, photo.DuplicateStackPhoto }
                : new[] { photo })
            .Where(photo => !photo.IsFullImageLoaded)
            .ToList();

        AlbumPhotoViewModel.PrioritizeViewportLoads(priorityPhotos);
    }

    public void TogglePhotoSelection(AlbumPhotoViewModel photo)
    {
        photo.IsSelectedForBulk = !photo.IsSelectedForBulk;
        UpdateBulkPhotoSelectionState();
    }

    public async Task ShowDuplicatePhotoInViewerAsync(AlbumPhotoViewModel photo)
    {
        if (string.IsNullOrWhiteSpace(photo.DuplicateGroupId) || !_duplicateGroupsById.ContainsKey(photo.DuplicateGroupId))
        {
            return;
        }

        SelectViewedPhoto(photo);
        IsPhotoViewerVisible = true;
        IsPhotoViewerActionsVisible = true;
        UpdatePhotoViewerDuplicateStripVisibility();
        PhotoViewerTitle = photo.FileName;
        PhotoViewerRotationDegrees = photo.RotationDegrees;
        PhotoViewerStatus = "Loading";

        try
        {
            _photoViewerCancellation?.Cancel();
            _photoViewerCancellation?.Dispose();
            _photoViewerCancellation = new CancellationTokenSource();
            var cancellation = _photoViewerCancellation.Token;
            PhotoViewerImage = await _imageCache.LoadOriginalBitmapAsync(
                photo.AlbumId,
                GetFullPhotoCacheFileName(photo),
                photo.DownloadUrl,
                _imageHttpClient,
                cancellation);
            PhotoViewerStatus = "Original loaded";
            _ = LoadViewedPhotoDisplayImageAsync(photo);
            _ = PreloadPhotoViewerDuplicatePhotosAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            PhotoViewerStatus = ex.Message;
        }
    }

    private async Task PreloadPhotoViewerDuplicatePhotosAsync()
    {
        var photos = PhotoViewerDuplicatePhotos.ToList();
        if (_selectedViewedPhoto is not null)
        {
            photos = photos
                .OrderBy(photo => ReferenceEquals(photo, _selectedViewedPhoto) ? 0 : 1)
                .ToList();
        }

        foreach (var photo in photos)
        {
            await photo.StartViewportLoadAsync(_imageCache, _imageHttpClient);
        }
    }

    private async Task LoadViewedPhotoDisplayImageAsync(AlbumPhotoViewModel photo)
    {
        try
        {
            await photo.StartViewportLoadAsync(_imageCache, _imageHttpClient);
        }
        catch
        {
            // The full viewer already reports original-image loading errors.
        }
    }

    public async Task DownloadSelectedPhotosAsync(string destinationPath, bool asArchive)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            Status = asArchive ? "Choose a destination archive file." : "Choose a destination folder.";
            return;
        }

        var photos = GetSelectedPhotos().ToList();
        if (photos.Count == 0)
        {
            Status = "Select at least one photo.";
            return;
        }

        _albumDownloadCancellation?.Cancel();
        _albumDownloadCancellation?.Dispose();
        _albumDownloadCancellation = new CancellationTokenSource();
        var cancellation = _albumDownloadCancellation;

        IsAlbumDownloadProgressVisible = true;
        AlbumDownloadProgressValue = 0;
        AlbumDownloadProgressMaximum = Math.Max(1, photos.Count);
        AlbumDownloadProgressMessage = asArchive
            ? "Preparing selected photo archive..."
            : "Preparing selected photo download...";

        try
        {
            if (asArchive)
            {
                await DownloadPhotoArchiveAsync(
                    photos,
                    destinationPath,
                    "selected photos",
                    cancellation.Token);
            }
            else
            {
                await DownloadPhotoFilesAsync(
                    photos,
                    destinationPath,
                    "selected photos",
                    cancellation.Token);
            }

            AlbumDownloadProgressMessage = asArchive
                ? "Selected photo archive complete."
                : "Selected photo download complete.";
            Status = AlbumDownloadProgressMessage;
        }
        catch (OperationCanceledException)
        {
            AlbumDownloadProgressMessage = "Selected photo download cancelled.";
            Status = "Selected photo download cancelled.";
        }
        catch (Exception ex)
        {
            AlbumDownloadProgressMessage = ex.Message;
            Status = ex.Message;
        }
        finally
        {
            IsAlbumDownloadProgressVisible = false;
            if (ReferenceEquals(_albumDownloadCancellation, cancellation))
            {
                _albumDownloadCancellation.Dispose();
                _albumDownloadCancellation = null;
            }
        }
    }

    private async Task SetCurrentPhotoCategoryAsync(string category)
    {
        if (_selectedViewedPhoto is null || _selectedViewedPhoto.IsFrozen || IsFeedbackCommitted || IsFeedbackPassed)
        {
            return;
        }

        var changedPhoto = _selectedViewedPhoto;
        var sourceCategory = _selectedViewedPhotoSourceCategory;
        var sourceReviewTabId = _selectedViewedPhotoSourceReviewTabId;
        var sourcePhotosBeforeChange = GetPhotosForCategory(sourceCategory).ToList();
        var changedPhotoIndex = sourcePhotosBeforeChange.FindIndex(photo => ReferenceEquals(photo, changedPhoto));
        var photosToUpdate = GetDuplicateGroupMembers(changedPhoto).ToList();
        foreach (var photo in photosToUpdate)
        {
            photo.Category = category;
        }

        if (_feedbackSession is not null && _feedbackDatabase is not null)
        {
            foreach (var photo in photosToUpdate)
            {
                if (string.IsNullOrWhiteSpace(category))
                {
                    await _reviewerFeedbackService.RemoveLocalDecisionAsync(
                        _feedbackSession,
                        _feedbackDatabase,
                        photo.PhotoId,
                        CancellationToken.None);
                }
                else
                {
                    await _reviewerFeedbackService.SaveLocalDecisionAsync(
                        _feedbackSession,
                        _feedbackDatabase,
                        photo.PhotoId,
                        category,
                        CancellationToken.None);
                }
            }
            _ = SyncFeedbackAsync();
        }

        RebuildCategoryRows();
        UpdateCurrentPhotoActionVisibility();
        Status = string.IsNullOrWhiteSpace(category)
            ? $"{changedPhoto.FileName} moved to uncategorized."
            : $"{changedPhoto.FileName} marked as {category}.";

        if (string.Equals(sourceReviewTabId, UnresolvedDuplicatesReviewTabId, StringComparison.Ordinal))
        {
            SelectViewedPhoto(changedPhoto);
        }
        else
        {
            await AdvanceFullImageViewerAfterCategoryChangeAsync(sourceCategory, changedPhotoIndex);
        }
    }

    private async Task RotateCurrentPhotoAsync(int degrees)
    {
        if (_selectedViewedPhoto is null)
        {
            return;
        }

        var photo = _selectedViewedPhoto;
        var rotation = NormalizeRotationDegrees(photo.RotationDegrees + degrees);
        photo.RotationDegrees = rotation;
        PhotoViewerRotationDegrees = rotation;

        if (_feedbackSession is not null && _feedbackDatabase is not null)
        {
            await _reviewerFeedbackService.SaveLocalPhotoRotationAsync(
                _feedbackSession,
                _feedbackDatabase,
                photo.PhotoId,
                rotation,
                CancellationToken.None);
            _ = SyncFeedbackAsync();
        }

        Status = rotation == 0
            ? $"{photo.FileName} rotation reset."
            : $"{photo.FileName} rotated {rotation} degrees.";
    }

    private async Task SetSelectedPhotosCategoryAsync(string category)
    {
        var photos = GetSelectedPhotos().ToList();
        if (photos.Count == 0 || !CanApplyBulkCategory(category, photos))
        {
            return;
        }

        var photosToUpdate = photos
            .SelectMany(GetDuplicateGroupMembers)
            .Distinct()
            .ToList();

        foreach (var photo in photosToUpdate)
        {
            photo.Category = category;
            if (_feedbackSession is null || _feedbackDatabase is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(category))
            {
                await _reviewerFeedbackService.RemoveLocalDecisionAsync(
                    _feedbackSession,
                    _feedbackDatabase,
                    photo.PhotoId,
                    CancellationToken.None);
            }
            else
            {
                await _reviewerFeedbackService.SaveLocalDecisionAsync(
                    _feedbackSession,
                    _feedbackDatabase,
                    photo.PhotoId,
                    category,
                    CancellationToken.None);
            }
        }

        if (_feedbackSession is not null && _feedbackDatabase is not null)
        {
            _ = SyncFeedbackAsync();
        }

        ClearBulkPhotoSelection();
        RebuildCategoryRows();
        UpdateCurrentPhotoActionVisibility();
        Status = string.IsNullOrWhiteSpace(category)
            ? $"{photos.Count} photo(s) moved to uncategorized."
            : $"{photos.Count} photo(s) marked as {category}.";
    }

    private async Task RotateSelectedPhotosAsync(int degrees)
    {
        var photos = GetSelectedPhotos().ToList();
        if (photos.Count == 0)
        {
            return;
        }

        foreach (var photo in photos)
        {
            var rotation = NormalizeRotationDegrees(photo.RotationDegrees + degrees);
            photo.RotationDegrees = rotation;

            if (_feedbackSession is null || _feedbackDatabase is null)
            {
                continue;
            }

            await _reviewerFeedbackService.SaveLocalPhotoRotationAsync(
                _feedbackSession,
                _feedbackDatabase,
                photo.PhotoId,
                rotation,
                CancellationToken.None);
        }

        if (_selectedViewedPhoto is not null && photos.Contains(_selectedViewedPhoto))
        {
            PhotoViewerRotationDegrees = _selectedViewedPhoto.RotationDegrees;
        }

        if (_feedbackSession is not null && _feedbackDatabase is not null)
        {
            _ = SyncFeedbackAsync();
        }

        Status = degrees < 0
            ? $"Rotated {photos.Count} selected photo(s) left."
            : $"Rotated {photos.Count} selected photo(s) right.";
    }

    private async Task MarkSelectedPhotosAsDuplicatesCoreAsync()
    {
        var photos = GetSelectedPhotos()
            .SelectMany(GetDuplicateGroupMembers)
            .Distinct()
            .ToList();
        if (photos.Count < 2 || photos.Any(photo => photo.IsFrozen) || _feedbackSession is null || _feedbackDatabase is null)
        {
            return;
        }

        var category = GetHighestPriorityCategory(photos.Select(photo => photo.Category));
        foreach (var photo in photos)
        {
            photo.Category = category;
            if (string.IsNullOrWhiteSpace(category))
            {
                await _reviewerFeedbackService.RemoveLocalDecisionAsync(
                    _feedbackSession,
                    _feedbackDatabase,
                    photo.PhotoId,
                    CancellationToken.None);
            }
            else
            {
                await _reviewerFeedbackService.SaveLocalDecisionAsync(
                    _feedbackSession,
                    _feedbackDatabase,
                    photo.PhotoId,
                    category,
                    CancellationToken.None);
            }
        }

        await _reviewerFeedbackService.SaveLocalDuplicateGroupAsync(
            _feedbackSession,
            _feedbackDatabase,
            photos.Select(photo => photo.PhotoId).ToList(),
            CancellationToken.None);

        _ = SyncFeedbackAsync();
        ClearBulkPhotoSelection();
        ApplyDuplicateGroupsToPhotos();
        RebuildCategoryRows();
        UpdateCurrentPhotoActionVisibility();
        Status = $"Marked {photos.Count} photo(s) as duplicates.";
    }

    private async Task RemoveCurrentPhotoFromDuplicatesCoreAsync()
    {
        if (_selectedViewedPhoto is null || _feedbackSession is null || _feedbackDatabase is null || string.IsNullOrWhiteSpace(_selectedViewedPhoto.DuplicateGroupId))
        {
            return;
        }

        var photo = _selectedViewedPhoto;
        var sourceGroupId = photo.DuplicateGroupId;
        var sourceReviewTabId = _selectedViewedPhotoSourceReviewTabId;
        var sourceMembers = _duplicateGroupsById.TryGetValue(sourceGroupId, out var members)
            ? members.ToList()
            : [photo];
        var sourceMemberIndex = sourceMembers.FindIndex(member => ReferenceEquals(member, photo));
        var sourceTabPhotos = GetPhotosForReviewTab(sourceReviewTabId).ToList();
        var sourceTabIndex = sourceTabPhotos.FindIndex(candidate =>
            ReferenceEquals(candidate, GetPhotoViewerNavigationAnchor(photo)));

        await _reviewerFeedbackService.RemoveLocalPhotoFromDuplicateGroupAsync(
            _feedbackSession,
            _feedbackDatabase,
            photo.PhotoId,
            CancellationToken.None);

        _ = SyncFeedbackAsync();
        ApplyDuplicateGroupsToPhotos();
        RebuildCategoryRows();
        Status = $"{photo.FileName} removed from duplicates.";
        if (string.Equals(sourceReviewTabId, UnresolvedDuplicatesReviewTabId, StringComparison.Ordinal))
        {
            await OpenNextPhotoAfterDuplicateRemovalAsync(
                sourceGroupId,
                sourceMembers,
                sourceMemberIndex,
                sourceReviewTabId,
                sourceTabIndex);
        }
        else
        {
            SelectViewedPhoto(photo);
        }
    }

    private async Task ToggleCurrentPhotoBestInDuplicateGroupCoreAsync()
    {
        if (_selectedViewedPhoto is null || _feedbackSession is null || _feedbackDatabase is null || string.IsNullOrWhiteSpace(_selectedViewedPhoto.DuplicateGroupId))
        {
            return;
        }

        var photo = _selectedViewedPhoto;
        var groupId = photo.DuplicateGroupId;
        var isBest = !photo.IsBestInDuplicateGroup;
        var shouldAdvanceAfterBestSelection = isBest &&
            string.Equals(_selectedViewedPhotoSourceReviewTabId, UnresolvedDuplicatesReviewTabId, StringComparison.Ordinal);
        var sourcePhotosBeforeChange = shouldAdvanceAfterBestSelection
            ? GetPhotosForReviewTab(_selectedViewedPhotoSourceReviewTabId).ToList()
            : [];
        var changedPhotoIndex = sourcePhotosBeforeChange.FindIndex(candidate =>
            ReferenceEquals(candidate, GetPhotoViewerNavigationAnchor(photo)));
        await _reviewerFeedbackService.SetLocalDuplicateGroupBestPhotoAsync(
            _feedbackSession,
            _feedbackDatabase,
            groupId,
            photo.PhotoId,
            isBest,
            CancellationToken.None);

        _ = SyncFeedbackAsync();
        ApplyDuplicateGroupsToPhotos();
        RebuildCategoryRows();
        SelectViewedPhoto(photo);
        Status = isBest
            ? $"{photo.FileName} marked as best in the group."
            : $"{photo.FileName} unmarked as best in the group.";

        if (shouldAdvanceAfterBestSelection)
        {
            await AdvanceFullImageViewerAfterReviewTabItemResolvedAsync(_selectedViewedPhotoSourceReviewTabId, changedPhotoIndex);
        }
    }

    partial void OnPhotoViewerImageChanging(Bitmap? value)
    {
        if (PhotoViewerImage is not null && !ReferenceEquals(PhotoViewerImage, value))
        {
            PhotoViewerImage.Dispose();
        }
    }

    private async Task CopyCurrentOriginalPhotoAsync(string destinationFilePath, string successVerb)
    {
        if (_selectedViewedPhoto is not { } photo)
        {
            Status = "No photo is selected.";
            return;
        }

        try
        {
            IsBusy = true;
            var destinationPath = Path.GetFullPath(destinationFilePath.Trim());
            var destinationDirectoryPath = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrWhiteSpace(destinationDirectoryPath))
            {
                Status = "Choose a destination file.";
                return;
            }

            Directory.CreateDirectory(destinationDirectoryPath);
            var tempPath = Path.Combine(destinationDirectoryPath, $".{Guid.NewGuid():N}.tmp");

            try
            {
                await using (var destination = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                {
                    await _imageCache.CopyOriginalToAsync(
                        photo.AlbumId,
                        GetFullPhotoCacheFileName(photo),
                        photo.DownloadUrl,
                        _imageHttpClient,
                        destination,
                        CancellationToken.None);
                }

                File.Move(tempPath, destinationPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }

            Status = $"{successVerb} {photo.FileName} to {destinationPath}.";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AddAlbumDownloadCategory(string categoryKey, string categoryName, string defaultDestination, string defaultModeId)
    {
        AlbumDownloadCategories.Add(new AlbumDownloadCategoryViewModel(
            categoryKey,
            categoryName,
            GetPhotosForCategory(categoryKey).Count(),
            defaultDestination,
            GetSafeArchiveFileName($"{CurrentAlbumTitle}_{categoryName}.zip"),
            defaultModeId));
    }

    private async Task DownloadAlbumCategoryFilesAsync(
        AlbumDownloadCategoryViewModel category,
        IReadOnlyList<AlbumPhotoViewModel> photos,
        CancellationToken cancellationToken)
    {
        await DownloadPhotoFilesAsync(
            photos,
            category.DestinationDirectoryPath,
            category.CategoryName,
            cancellationToken);
    }

    private async Task DownloadAlbumCategoryArchiveAsync(
        AlbumDownloadCategoryViewModel category,
        IReadOnlyList<AlbumPhotoViewModel> photos,
        CancellationToken cancellationToken)
    {
        await DownloadPhotoArchiveAsync(
            photos,
            category.DestinationDirectoryPath,
            category.CategoryName,
            cancellationToken);
    }

    private async Task DownloadPhotoFilesAsync(
        IReadOnlyList<AlbumPhotoViewModel> photos,
        string destinationDirectoryPath,
        string progressScope,
        CancellationToken cancellationToken)
    {
        var destinationDirectory = Path.GetFullPath(destinationDirectoryPath.Trim());
        Directory.CreateDirectory(destinationDirectory);
        var reservedDestinationPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var destinationLock = new object();
        var completed = 0;

        await Parallel.ForEachAsync(
            photos,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = GetMaximumParallelism(),
                CancellationToken = cancellationToken
            },
            async (photo, token) =>
            {
                Dispatcher.UIThread.Post(() => AlbumDownloadProgressMessage = $"Downloading {progressScope}: {photo.FileName}");

                string destinationPath;
                lock (destinationLock)
                {
                    destinationPath = GetAvailableDestinationPath(destinationDirectory, GetSafeDownloadFileName(photo), reservedDestinationPaths);
                    reservedDestinationPaths.Add(destinationPath);
                }

                var tempPath = Path.Combine(destinationDirectory, $".{Guid.NewGuid():N}.tmp");
                try
                {
                    await using (var destination = new FileStream(
                        tempPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None))
                    {
                        await _imageCache.CopyOriginalToAsync(
                            photo.AlbumId,
                            GetFullPhotoCacheFileName(photo),
                            photo.DownloadUrl,
                            _imageHttpClient,
                            destination,
                            token);
                    }

                    File.Move(tempPath, destinationPath);
                }
                finally
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }

                var value = Interlocked.Increment(ref completed);
                Dispatcher.UIThread.Post(() => AlbumDownloadProgressValue = value);
            });
    }

    private async Task DownloadPhotoArchiveAsync(
        IReadOnlyList<AlbumPhotoViewModel> photos,
        string archiveFilePath,
        string progressScope,
        CancellationToken cancellationToken)
    {
        var archivePath = Path.GetFullPath(archiveFilePath.Trim());
        var destinationDirectory = Path.GetDirectoryName(archivePath);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new InvalidOperationException("Choose a destination archive file.");
        }

        Directory.CreateDirectory(destinationDirectory);
        var tempArchivePath = Path.Combine(destinationDirectory, $".{Guid.NewGuid():N}.zip.tmp");
        var entryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(tempArchivePath, System.IO.Compression.ZipArchiveMode.Create))
            {
                foreach (var photo in photos)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AlbumDownloadProgressMessage = $"Archiving {progressScope}: {photo.FileName}";

                    var entry = archive.CreateEntry(GetAvailableArchiveEntryName(entryNames, GetSafeDownloadFileName(photo)));
                    await using var destination = entry.Open();
                    await _imageCache.CopyOriginalToAsync(
                        photo.AlbumId,
                        GetFullPhotoCacheFileName(photo),
                        photo.DownloadUrl,
                        _imageHttpClient,
                        destination,
                        cancellationToken);

                    AlbumDownloadProgressValue++;
                }
            }

            File.Move(tempArchivePath, archivePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempArchivePath))
            {
                File.Delete(tempArchivePath);
            }
        }
    }

    private static string GetFullPhotoCacheFileName(AlbumPhotoViewModel photo)
    {
        return $"{photo.PhotoId}-full{photo.FileExtension}";
    }

    private static string GetSafeDownloadFileName(AlbumPhotoViewModel photo)
    {
        var fileName = Path.GetFileName(photo.FileName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = $"{photo.PhotoId}{photo.FileExtension}";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? $"{photo.PhotoId}{photo.FileExtension}" : sanitized;
    }

    private static string GetSafeArchiveFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "album.zip" : sanitized;
    }

    private static string GetAvailableDestinationPath(string destinationDirectoryPath, string fileName)
    {
        return GetAvailableDestinationPath(destinationDirectoryPath, fileName, null);
    }

    private static string GetAvailableDestinationPath(
        string destinationDirectoryPath,
        string fileName,
        HashSet<string>? reservedDestinationPaths)
    {
        var targetPath = Path.Combine(destinationDirectoryPath, fileName);
        if (!File.Exists(targetPath) && reservedDestinationPaths?.Contains(targetPath) != true)
        {
            return targetPath;
        }

        var extension = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        for (var index = 1; ; index++)
        {
            targetPath = Path.Combine(destinationDirectoryPath, $"{baseName} ({index}){extension}");
            if (!File.Exists(targetPath) && reservedDestinationPaths?.Contains(targetPath) != true)
            {
                return targetPath;
            }
        }
    }

    private static string GetAvailableArchiveEntryName(HashSet<string> existingNames, string fileName)
    {
        if (existingNames.Add(fileName))
        {
            return fileName;
        }

        var extension = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        for (var index = 1; ; index++)
        {
            var candidate = $"{baseName} ({index}){extension}";
            if (existingNames.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private void SetDefaultDownloadDirectory(
        IStorageFolder folder,
        Action<string> setDirectoryPath,
        string label)
    {
        var localPath = folder.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(localPath))
        {
            Status = $"The selected {label.ToLowerInvariant()} must be a local filesystem folder.";
            return;
        }

        var fullPath = Path.GetFullPath(localPath);
        setDirectoryPath(fullPath);
        SaveLocalUserSettings();
        Status = $"{label} set to {fullPath}.";
    }

    private void SetDefaultDownloadDirectory(Action<string> setDirectoryPath, string label)
    {
        var defaultPath = GetDefaultDownloadDirectoryPath();
        setDirectoryPath(defaultPath);
        SaveLocalUserSettings();
        Status = string.IsNullOrWhiteSpace(defaultPath)
            ? $"{label} default is not available on this platform."
            : $"{label} set to default: {defaultPath}.";
    }

    private static string GetConfiguredOrDefaultDownloadDirectoryPath(string configuredPath)
    {
        return string.IsNullOrWhiteSpace(configuredPath) ? GetDefaultDownloadDirectoryPath() : configuredPath;
    }

    private static string GetDefaultDownloadDirectoryPath()
    {
        var picturesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        return string.IsNullOrWhiteSpace(picturesPath) ? "" : picturesPath;
    }

    private async Task<bool> RequiresGoogleAuthorizationPromptAsync(AlbumManifest manifest)
    {
        if (!UsesGoogleDriveBackend(manifest))
        {
            return false;
        }

        if (_googleTokenSet is null)
        {
            return true;
        }

        try
        {
            await GetGoogleAccessTokenAsync(CancellationToken.None);
            return false;
        }
        catch
        {
            _tokenStore.Clear();
            _googleTokenSet = null;
            IsGoogleSignedIn = false;
            OnPropertyChanged(nameof(GoogleConnectionStatus));
            return true;
        }
    }

    private async Task CompletePendingGoogleAuthorizedAlbumOpenAsync()
    {
        if (_pendingGoogleAuthorizationManifest is null)
        {
            return;
        }

        var manifest = _pendingGoogleAuthorizationManifest;
        _pendingGoogleAuthorizationManifest = null;
        IsGoogleAuthorizationRequired = false;
        GoogleAuthorizationMessage = "";
        await LoadAlbumAsync(manifest);
        if (_currentManifest is not null)
        {
            SaveOpenedAlbumReference(manifest, GetAlbumShareLink(manifest));
            Status = $"Opened {manifest.Title} with {manifest.Photos.Count} photo(s).";
        }
    }

    private void SaveOpenedAlbumReference(AlbumManifest manifest, string link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return;
        }

        _lastOpenAlbumLink = link;
        OpenAlbumLink = link;
        var settings = new RecentAlbumSettings
        {
            Title = manifest.Title,
            Link = link,
            Location = manifest.GoogleDrive?.AlbumFolderUrl ?? manifest.LocalFileSystem?.RootPath ?? "",
            OpenedAt = DateTimeOffset.UtcNow
        };

        var existing = RecentAlbums
            .Where(album => !string.Equals(album.Link, link, StringComparison.Ordinal))
            .Select(album => new RecentAlbumSettings
            {
                Title = album.Title,
                Link = album.Link,
                Location = album.Location,
                OpenedAt = album.OpenedAt
            })
            .Prepend(settings)
            .Take(MaxRecentAlbumCount)
            .ToList();

        LoadRecentAlbums(existing);
        SaveAlbumOpenHistory();
    }

    private void ForgetOpenedAlbumReference(AlbumManifest manifest)
    {
        var link = GetAlbumShareLink(manifest);
        if (string.IsNullOrWhiteSpace(link))
        {
            return;
        }

        if (string.Equals(_lastOpenAlbumLink, link, StringComparison.Ordinal))
        {
            _lastOpenAlbumLink = "";
        }

        if (string.Equals(OpenAlbumLink, link, StringComparison.Ordinal))
        {
            OpenAlbumLink = "";
        }

        var remaining = RecentAlbums
            .Where(album => !string.Equals(album.Link, link, StringComparison.Ordinal))
            .Select(album => new RecentAlbumSettings
            {
                Title = album.Title,
                Link = album.Link,
                Location = album.Location,
                OpenedAt = album.OpenedAt
            })
            .ToList();

        LoadRecentAlbums(remaining);
        SaveAlbumOpenHistory();
    }

    private void LoadRecentAlbums(IEnumerable<RecentAlbumSettings> settings)
    {
        RecentAlbums.Clear();
        foreach (var album in settings
            .Where(album => !string.IsNullOrWhiteSpace(album.Link))
            .OrderByDescending(album => album.OpenedAt)
            .Take(MaxRecentAlbumCount))
        {
            RecentAlbums.Add(new RecentAlbumViewModel(album));
        }

        HasRecentAlbums = RecentAlbums.Count > 0;
    }

    private static string GetAlbumShareLink(AlbumManifest manifest)
    {
        return manifest.GoogleDrive is not null
            ? AlbumLinkParser.CreatePicshareLink(manifest.GoogleDrive.ManifestFileId, manifest.GoogleDrive.AlbumFolderId)
            : AlbumLinkParser.CreateLocalPicshareLink(manifest.LocalFileSystem!.ManifestFilePath);
    }

    private async Task LoadReviewerFeedbackAsync(AlbumManifest manifest)
    {
        var reviewerIdentity = CreateReviewerIdentity(manifest);
        if (reviewerIdentity is null)
        {
            if (UsesLocalFileSystemBackend(manifest))
            {
                Status = "Set an anonymous reviewer name in settings before opening this local album.";
                IsSettingsPanelVisible = true;
            }

            return;
        }

        var backend = await CreateFeedbackBackendAsync(manifest, CancellationToken.None);
        var deletionMarker = await backend.LoadAlbumDeletionMarkerAsync(CancellationToken.None);
        if (deletionMarker is not null)
        {
            if (IsCurrentReviewerAuthor(manifest))
            {
                _currentReviewerIdentity = reviewerIdentity;
                IsAuthorFlowVisible = true;
                Status = "Album deletion was already started. Continuing deletion...";
                PrepareOpenAlbumForDeletion();
                await ExecuteAlbumDeletionAsync(manifest, reviewerIdentity, backend, CancellationToken.None, showProgress: true);
            }
            else
            {
                await CloseOpenedAlbumLocallyAsync(manifest);
                Status = "This album is being deleted by its author.";
            }

            return;
        }

        var result = await _reviewerFeedbackService.LoadAsync(
            manifest,
            reviewerIdentity,
            backend,
            CancellationToken.None);

        _currentReviewerIdentity = reviewerIdentity;
        _feedbackSession = result.Session;
        _feedbackDatabase = result.Database;
        _feedbackStatus = result.Status;
        IsFeedbackCommitted = _feedbackStatus.Status == ReviewerFeedbackStatusKind.Committed;
        IsFeedbackPassed = _feedbackStatus.Status == ReviewerFeedbackStatusKind.Passed;
        IsFeedbackLeft = _feedbackStatus.Status == ReviewerFeedbackStatusKind.Left;
        IsAuthorFlowVisible = IsCurrentReviewerAuthor(manifest);
        CanCollectFeedback = IsAuthorFlowVisible;
        CanFinalizeFeedback = IsAuthorFlowVisible;
        UpdateFeedbackControlState();

        if (result.ConcurrentRemoteUpdate)
        {
            ShowFeedbackConflictNotice();
        }

        StartFeedbackSync();
    }

    private async Task ExecuteAlbumDeletionAsync(
        AlbumManifest manifest,
        FeedbackReviewerIdentity reviewer,
        IReviewerFeedbackBackend backend,
        CancellationToken cancellationToken,
        bool showProgress = false)
    {
        _activeDeletingAlbumManifest = manifest;
        if (showProgress)
        {
            _albumDeletionCancellation?.Cancel();
            _albumDeletionCancellation?.Dispose();
            _albumDeletionCancellation = new CancellationTokenSource();
            cancellationToken = _albumDeletionCancellation.Token;
            IsAlbumDeletionProgressVisible = true;
            AlbumDeletionProgressValue = 0;
            AlbumDeletionProgressMaximum = 4;
            AlbumDeletionProgressMessage = "Deleting album...";
        }

        var progress = new Progress<AlbumDeletionProgress>(progress =>
        {
            AlbumDeletionProgressMessage = progress.Message;
            AlbumDeletionProgressValue = progress.Value;
            AlbumDeletionProgressMaximum = Math.Max(1, progress.Maximum);
            Status = progress.Message;
        });

        try
        {
            await _albumDeletionService.RequestDeletionAsync(
                manifest,
                reviewer,
                backend,
                GetGoogleAccessTokenAsync,
                cancellationToken,
                progress,
                GetMaximumParallelism());

            await CloseOpenedAlbumLocallyAsync(manifest);
            Status = "Album deleted.";
        }
        finally
        {
            if (showProgress)
            {
                IsAlbumDeletionProgressVisible = false;
                _activeDeletingAlbumManifest = null;
                _albumDeletionCancellation?.Dispose();
                _albumDeletionCancellation = null;
            }
        }
    }

    private void PrepareOpenAlbumForDeletion()
    {
        StopFeedbackSync();
        StopFlowMonitor();
        _albumDownloadCancellation?.Cancel();
        IsAlbumDownloadDialogVisible = false;
        IsAlbumDownloadProgressVisible = false;

        foreach (var photo in Photos)
        {
            photo.StopViewportLoad();
            photo.ReleaseCachedImage();
        }

        ClosePhotoViewer();
    }

    private void ClearOpenedAlbumState()
    {
        StopFeedbackSync();
        StopFlowMonitor();
        _currentManifest = null;
        _feedbackSession = null;
        _feedbackDatabase = null;
        _feedbackStatus = null;
        _currentReviewerIdentity = null;
        _hasCollectedFeedback = false;
        _isFeedbackFinalized = false;
        _unfrozenCollectedPhotoCount = 0;
        IsFeedbackCommitted = false;
        IsFeedbackPassed = false;
        IsFeedbackLeft = false;
        IsAuthorFlowVisible = false;
        CanCollectFeedback = false;
        IsCollectFeedbackVisible = false;
        CanStartNextRound = false;
        CanFinalizeFeedback = false;
        CanDeleteAlbum = false;
        CurrentAlbumTitle = "";
        FlowStatus = "";
        ClearBulkPhotoSelection();
        Photos.Clear();
        ClearCategoryRows();
        NotifyReviewTabHeadersChanged();
        SelectViewedPhoto(null);
        ClosePhotoViewer();
        ClearFlowReviewers();
        UpdateFeedbackControlState();
        UpdateCurrentPhotoActionVisibility();
    }

    private async Task CloseOpenedAlbumLocallyAsync(AlbumManifest manifest)
    {
        ForgetOpenedAlbumReference(manifest);
        ClearOpenedAlbumState();
        await _imageCache.ClearAlbumAsync(manifest.AlbumId);
    }

    private async Task ResumePendingAlbumDeletionsAsync()
    {
        try
        {
            var pendingDeletions = await _albumDeletionService.LoadPendingDeletionsAsync(CancellationToken.None);
            foreach (var pendingDeletion in pendingDeletions)
            {
                try
                {
                    var manifest = pendingDeletion.Manifest;
                    if (manifest.LocalFileSystem is not null &&
                        !Directory.Exists(manifest.LocalFileSystem.RootPath))
                    {
                        await _albumDeletionService.DeleteLocalStateAsync(manifest.AlbumId);
                        _albumDeletionService.ForgetPendingDeletion(manifest.AlbumId);
                        continue;
                    }

                    var backend = await CreateFeedbackBackendAsync(manifest, CancellationToken.None);
                    _albumDeletionCancellation?.Cancel();
                    _albumDeletionCancellation?.Dispose();
                    _albumDeletionCancellation = new CancellationTokenSource();
                    _activeDeletingAlbumManifest = manifest;
                    IsAlbumDeletionProgressVisible = true;
                    AlbumDeletionProgressValue = 0;
                    AlbumDeletionProgressMaximum = 4;
                    AlbumDeletionProgressMessage = "Deleting album...";
                    var progress = new Progress<AlbumDeletionProgress>(progress =>
                    {
                        AlbumDeletionProgressMessage = progress.Message;
                        AlbumDeletionProgressValue = progress.Value;
                        AlbumDeletionProgressMaximum = Math.Max(1, progress.Maximum);
                        Status = progress.Message;
                    });
                    await _albumDeletionService.RequestDeletionAsync(
                        manifest,
                        pendingDeletion.RequestedBy,
                        backend,
                        GetGoogleAccessTokenAsync,
                        _albumDeletionCancellation.Token,
                        progress,
                        GetMaximumParallelism());
                    IsAlbumDeletionProgressVisible = false;
                    _activeDeletingAlbumManifest = null;
                    _albumDeletionCancellation.Dispose();
                    _albumDeletionCancellation = null;
                    ForgetOpenedAlbumReference(manifest);
                }
                catch
                {
                    IsAlbumDeletionProgressVisible = false;
                    _activeDeletingAlbumManifest = null;
                    _albumDeletionCancellation?.Dispose();
                    _albumDeletionCancellation = null;
                }
            }
        }
        catch
        {
        }
    }

    private void ApplyFeedbackDatabaseToPhotos()
    {
        foreach (var photo in Photos)
        {
            photo.Category = _feedbackDatabase is not null &&
                _feedbackDatabase.PhotoCategories.TryGetValue(photo.PhotoId, out var category)
                    ? category
                    : "";
            photo.IsFrozen = _feedbackDatabase?.FrozenPhotoIds.Contains(photo.PhotoId) == true;
            photo.RotationDegrees = _feedbackDatabase is not null &&
                _feedbackDatabase.PhotoRotations.TryGetValue(photo.PhotoId, out var rotation)
                    ? NormalizeRotationDegrees(rotation)
                    : 0;
        }

        _hasCollectedFeedback = _feedbackDatabase?.HasCollectedFeedback == true;
        _isFeedbackFinalized = _feedbackDatabase?.IsFinalized == true;
        _unfrozenCollectedPhotoCount = _hasCollectedFeedback
            ? Photos.Count(photo => !photo.IsFrozen)
            : 0;

        ApplyDuplicateGroupsToPhotos();
        RebuildCategoryRows();
        if (_selectedViewedPhoto is not null)
        {
            PhotoViewerRotationDegrees = _selectedViewedPhoto.RotationDegrees;
        }
    }

    private void ApplyDuplicateGroupsToPhotos()
    {
        _duplicateGroupsById.Clear();
        foreach (var photo in Photos)
        {
            photo.IsDuplicateGroupMain = false;
            photo.DuplicateGroupId = "";
            photo.DuplicateGroupCount = 0;
            photo.DuplicateStackPhoto = null;
            photo.IsBestInDuplicateGroup = false;
        }

        if (_feedbackDatabase is null)
        {
            return;
        }

        var photosById = Photos.ToDictionary(photo => photo.PhotoId, StringComparer.Ordinal);
        foreach (var group in _feedbackDatabase.DuplicateGroups)
        {
            var members = group.PhotoIds
                .Distinct(StringComparer.Ordinal)
                .Select(photoId => photosById.TryGetValue(photoId, out var photo) ? photo : null)
                .OfType<AlbumPhotoViewModel>()
                .ToList();
            if (members.Count < 2)
            {
                continue;
            }

            var groupId = string.IsNullOrWhiteSpace(group.Id) ? Guid.NewGuid().ToString("N") : group.Id;
            var bestPhoto = members.FirstOrDefault(photo => string.Equals(photo.PhotoId, group.BestPhotoId, StringComparison.Ordinal));
            var mainPhoto = bestPhoto ?? members[0];
            var orderedMembers = new[] { mainPhoto }
                .Concat(members.Where(photo => !ReferenceEquals(photo, mainPhoto)))
                .ToList();
            _duplicateGroupsById[groupId] = orderedMembers;
            var category = GetHighestPriorityCategory(members.Select(photo => photo.Category));
            foreach (var member in members)
            {
                member.DuplicateGroupId = groupId;
                member.DuplicateGroupCount = members.Count;
                member.DuplicateStackPhoto = mainPhoto;
                member.IsBestInDuplicateGroup = ReferenceEquals(member, bestPhoto);
                member.Category = category;
            }

            mainPhoto.IsDuplicateGroupMain = true;
        }
    }

    private void ApplyFeedbackStatus()
    {
        IsFeedbackCommitted = _feedbackStatus?.Status == ReviewerFeedbackStatusKind.Committed;
        IsFeedbackPassed = _feedbackStatus?.Status == ReviewerFeedbackStatusKind.Passed;
        IsFeedbackLeft = _feedbackStatus?.Status == ReviewerFeedbackStatusKind.Left;
    }

    private void RebuildCategoryRows()
    {
        ClearCategoryRows();
        var visiblePhotos = GetVisiblePhotos().ToList();
        AddGroups(UncategorizedPhotoGroups, visiblePhotos.Where(photo => string.IsNullOrWhiteSpace(photo.Category)));
        AddGroups(NicePhotoGroups, visiblePhotos.Where(photo => string.Equals(photo.Category, "nice", StringComparison.Ordinal)));
        AddGroups(OkPhotoGroups, visiblePhotos.Where(photo => string.Equals(photo.Category, "ok", StringComparison.Ordinal)));
        AddGroups(TrashPhotoGroups, visiblePhotos.Where(photo => string.Equals(photo.Category, "trash", StringComparison.Ordinal)));
        AddGroups(UnresolvedDuplicatePhotoGroups, GetUnresolvedDuplicatePhotos());
        NotifyReviewTabHeadersChanged();
        UpdateFeedbackControlState();
        UpdateBulkPhotoSelectionState();
    }

    private void ClearCategoryRows()
    {
        UncategorizedPhotoGroups.Clear();
        NicePhotoGroups.Clear();
        OkPhotoGroups.Clear();
        TrashPhotoGroups.Clear();
        UnresolvedDuplicatePhotoGroups.Clear();
    }

    private void NotifyReviewTabHeadersChanged()
    {
        OnPropertyChanged(nameof(UncategorizedTabHeader));
        OnPropertyChanged(nameof(NiceTabHeader));
        OnPropertyChanged(nameof(OkTabHeader));
        OnPropertyChanged(nameof(TrashTabHeader));
        OnPropertyChanged(nameof(UnresolvedDuplicatesTabHeader));
        OnPropertyChanged(nameof(HasUnresolvedDuplicatePhotos));
    }

    private static void AddGroups(ObservableCollection<object> groups, IEnumerable<AlbumPhotoViewModel> photos)
    {
        var materialized = photos.ToList();
        AddGroup(groups, "Uncommitted", materialized.Where(photo => !photo.IsFrozen));
        AddGroup(groups, "Committed", materialized.Where(photo => photo.IsFrozen));
    }

    private static void AddGroup(
        ObservableCollection<object> groups,
        string header,
        IEnumerable<AlbumPhotoViewModel> photos)
    {
        var materialized = photos.ToList();
        if (materialized.Count == 0)
        {
            return;
        }

        groups.Add(new AlbumPhotoGroupHeaderViewModel(header));
        foreach (var row in materialized.Chunk(PhotosPerRow))
        {
            groups.Add(new AlbumPhotoRowViewModel(row));
        }
    }

    private void ClearFlowReviewers()
    {
        CommittedReviewers.Clear();
        PassedReviewers.Clear();
        LeftReviewers.Clear();
        InProgressReviewers.Clear();
        OnPropertyChanged(nameof(CommittedReviewersHeader));
        OnPropertyChanged(nameof(PassedReviewersHeader));
        OnPropertyChanged(nameof(LeftReviewersHeader));
        OnPropertyChanged(nameof(InProgressReviewersHeader));
    }

    private void StartFeedbackSync()
    {
        StopFeedbackSync();
        _feedbackSyncCancellation = new CancellationTokenSource();
        _ = RunFeedbackSyncLoopAsync(_feedbackSyncCancellation.Token);
    }

    private void StopFeedbackSync()
    {
        _feedbackSyncCancellation?.Cancel();
        _feedbackSyncCancellation?.Dispose();
        _feedbackSyncCancellation = null;
    }

    private async Task RunFeedbackSyncLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await SyncFeedbackAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SyncFeedbackAsync(CancellationToken cancellationToken = default)
    {
        if (_feedbackSession is null || _feedbackDatabase is null)
        {
            return;
        }

        if (!await _feedbackSyncGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            var backend = await CreateFeedbackBackendAsync(cancellationToken);
            var result = await _reviewerFeedbackService.SyncAsync(
                _feedbackSession,
                _feedbackDatabase,
                backend,
                cancellationToken);

            if (result.RemoteWon)
            {
                _feedbackDatabase = result.Database;
                _feedbackStatus = result.Status ?? _feedbackStatus;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ApplyFeedbackDatabaseToPhotos();
                    ApplyFeedbackStatus();
                    UpdateFeedbackControlState();
                    UpdateCurrentPhotoActionVisibility();
                    if (result.LocalDirtyBeforeSync)
                    {
                        ShowFeedbackConflictNotice();
                    }
                });
            }
            else if (result.Status is not null && !Equals(_feedbackStatus?.Status, result.Status.Status))
            {
                _feedbackStatus = result.Status;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ApplyFeedbackStatus();
                    UpdateFeedbackControlState();
                    UpdateCurrentPhotoActionVisibility();
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => Status = $"Feedback sync failed: {ex.Message}");
        }
        finally
        {
            _feedbackSyncGate.Release();
        }
    }

    public void SetActiveReviewTab(string tabId)
    {
        _activeReviewTabId = string.IsNullOrWhiteSpace(tabId) ? UncategorizedReviewTabId : tabId;
        _isFlowTabActive = string.Equals(_activeReviewTabId, FlowReviewTabId, StringComparison.Ordinal);
        UpdateBulkPhotoSelectionState();
        if (_isFlowTabActive && IsAuthorFlowVisible)
        {
            StartFlowMonitor();
        }
        else
        {
            StopFlowMonitor();
        }
    }

    private void StartFlowMonitor()
    {
        if (_flowMonitorCancellation is not null)
        {
            return;
        }

        _flowMonitorCancellation = new CancellationTokenSource();
        _ = RunFlowMonitorAsync(_flowMonitorCancellation.Token);
    }

    private void StopFlowMonitor()
    {
        _flowMonitorCancellation?.Cancel();
        _flowMonitorCancellation?.Dispose();
        _flowMonitorCancellation = null;
    }

    private async Task RunFlowMonitorAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshFlowAsync(cancellationToken);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshFlowAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshFlowAsync(CancellationToken cancellationToken)
    {
        if (_currentManifest is null || !IsAuthorFlowVisible)
        {
            return;
        }

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => FlowStatus = "Refreshing");
            var backend = await CreateFeedbackBackendAsync(_currentManifest, cancellationToken);
            var flow = await _reviewerFeedbackService.LoadFeedbackFlowAsync(backend, cancellationToken);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ClearFlowReviewers();
                foreach (var reviewer in flow.Committed)
                {
                    CommittedReviewers.Add(new ReviewerFeedbackFlowItemViewModel(reviewer));
                }

                foreach (var reviewer in flow.Passed)
                {
                    PassedReviewers.Add(new ReviewerFeedbackFlowItemViewModel(reviewer));
                }

                foreach (var reviewer in flow.Left)
                {
                    LeftReviewers.Add(new ReviewerFeedbackFlowItemViewModel(reviewer));
                }

                var finalizedInProgress = _isFeedbackFinalized
                    ? flow.InProgress.Concat(flow.Committed).Concat(flow.Passed)
                    : flow.InProgress;

                foreach (var reviewer in finalizedInProgress)
                {
                    InProgressReviewers.Add(new ReviewerFeedbackFlowItemViewModel(reviewer));
                }

                OnPropertyChanged(nameof(CommittedReviewersHeader));
                OnPropertyChanged(nameof(PassedReviewersHeader));
                OnPropertyChanged(nameof(LeftReviewersHeader));
                OnPropertyChanged(nameof(InProgressReviewersHeader));
                UpdateFeedbackControlState();
                FlowStatus = flow.Committed.Count == 0 && flow.Passed.Count == 0 && flow.Left.Count == 0 && flow.InProgress.Count == 0
                    ? "No reviewer activity yet."
                    : "";
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => FlowStatus = $"Flow refresh failed: {ex.Message}");
        }
    }

    private void ShowFeedbackConflictNotice()
    {
        FeedbackConflictMessage = "The feedback has been updated concurrently. Remote feedback was loaded.";
        IsFeedbackConflictNoticeVisible = true;
    }

    private void SelectViewedPhoto(AlbumPhotoViewModel? photo)
    {
        if (_selectedViewedPhoto is not null)
        {
            _selectedViewedPhoto.IsSelectedForViewing = false;
        }

        _selectedViewedPhoto = photo;
        _selectedViewedPhotoSourceCategory = photo?.Category ?? "";
        _selectedViewedPhotoSourceReviewTabId = _activeReviewTabId;

        if (_selectedViewedPhoto is not null)
        {
            _selectedViewedPhoto.IsSelectedForViewing = true;
        }

        PhotoViewerDuplicatePhotos.Clear();
        if (_selectedViewedPhoto is not null &&
            !string.IsNullOrWhiteSpace(_selectedViewedPhoto.DuplicateGroupId) &&
            _duplicateGroupsById.TryGetValue(_selectedViewedPhoto.DuplicateGroupId, out var members))
        {
            foreach (var member in members)
            {
                PhotoViewerDuplicatePhotos.Add(member);
            }
        }

        UpdateCurrentPhotoActionVisibility();
    }

    private void UpdateCurrentPhotoActionVisibility()
    {
        var category = _selectedViewedPhoto?.Category ?? "";
        ShouldShowCurrentPhotoUncategorizedAction = !string.IsNullOrWhiteSpace(category);
        ShouldShowCurrentPhotoNiceAction = !string.Equals(category, "nice", StringComparison.Ordinal);
        ShouldShowCurrentPhotoOkAction = !string.Equals(category, "ok", StringComparison.Ordinal);
        ShouldShowCurrentPhotoTrashAction = !string.Equals(category, "trash", StringComparison.Ordinal);
        var canChangeCurrentPhoto = CanModifyFeedback && _selectedViewedPhoto?.IsFrozen != true;
        CanMarkCurrentPhotoUncategorized = canChangeCurrentPhoto && ShouldShowCurrentPhotoUncategorizedAction;
        CanMarkCurrentPhotoNice = canChangeCurrentPhoto && ShouldShowCurrentPhotoNiceAction;
        CanMarkCurrentPhotoOk = canChangeCurrentPhoto && ShouldShowCurrentPhotoOkAction;
        CanMarkCurrentPhotoTrash = canChangeCurrentPhoto && ShouldShowCurrentPhotoTrashAction;
        CanShowCurrentPhotoTrashAction = ShouldShowCurrentPhotoTrashAction;
        CanNavigatePhotoViewerCategory = _selectedViewedPhoto is not null && GetPhotosForReviewTab(_selectedViewedPhotoSourceReviewTabId).Any();
        CanDownloadCurrentPhoto = _selectedViewedPhoto is not null;
        CanRemoveCurrentPhotoFromDuplicates = _selectedViewedPhoto is not null &&
            !string.IsNullOrWhiteSpace(_selectedViewedPhoto.DuplicateGroupId);
        IsCurrentPhotoBestInDuplicateGroup = _selectedViewedPhoto?.IsBestInDuplicateGroup == true;
        UpdatePhotoViewerDuplicateStripVisibility();
    }

    private void UpdatePhotoViewerDuplicateStripVisibility()
    {
        IsPhotoViewerDuplicateStripVisible = IsPhotoViewerActionsVisible && CanRemoveCurrentPhotoFromDuplicates;
    }

    private void UpdateBulkPhotoSelectionState()
    {
        var selectedPhotos = GetSelectedPhotos().ToList();
        SelectedPhotoCount = selectedPhotos.Count;
        IsBulkPhotoActionPanelVisible = selectedPhotos.Count > 0 && !_isFlowTabActive;
        BulkSelectionStatus = selectedPhotos.Count == 0
            ? ""
            : $"{selectedPhotos.Count} selected";
        ShouldShowSelectedPhotosUncategorizedAction =
            selectedPhotos.Count > 0 &&
            selectedPhotos.All(photo => !string.IsNullOrWhiteSpace(photo.Category));
        ShouldShowSelectedPhotosNiceAction =
            selectedPhotos.Count > 0 &&
            selectedPhotos.All(photo => !string.Equals(photo.Category, "nice", StringComparison.Ordinal));
        ShouldShowSelectedPhotosOkAction =
            selectedPhotos.Count > 0 &&
            selectedPhotos.All(photo => !string.Equals(photo.Category, "ok", StringComparison.Ordinal));
        ShouldShowSelectedPhotosTrashAction =
            selectedPhotos.Count > 0 &&
            selectedPhotos.All(photo => !string.Equals(photo.Category, "trash", StringComparison.Ordinal));

        CanMarkSelectedPhotosUncategorized = CanApplyBulkCategory("", selectedPhotos);
        CanMarkSelectedPhotosNice = CanApplyBulkCategory("nice", selectedPhotos);
        CanMarkSelectedPhotosOk = CanApplyBulkCategory("ok", selectedPhotos);
        CanMarkSelectedPhotosTrash = CanApplyBulkCategory("trash", selectedPhotos);
        CanShowSelectedPhotosTrashAction = ShouldShowSelectedPhotosTrashAction;
        CanDownloadSelectedPhotos = selectedPhotos.Count > 0;
        CanRotateSelectedPhotos = selectedPhotos.Count > 0;
        var selectedPhotosWithDuplicateMembers = selectedPhotos
            .SelectMany(GetDuplicateGroupMembers)
            .Distinct()
            .ToList();
        CanMarkSelectedPhotosAsDuplicates = selectedPhotosWithDuplicateMembers.Count >= 2 &&
            CanModifyFeedback &&
            selectedPhotosWithDuplicateMembers.All(photo => !photo.IsFrozen);
    }

    private bool CanApplyBulkCategory(string category, IReadOnlyList<AlbumPhotoViewModel> selectedPhotos)
    {
        if (selectedPhotos.Count == 0 || !CanModifyFeedback)
        {
            return false;
        }

        var actionPhotos = selectedPhotos
            .SelectMany(GetDuplicateGroupMembers)
            .Distinct()
            .ToList();

        return actionPhotos.All(photo =>
            !photo.IsFrozen &&
            (string.IsNullOrWhiteSpace(category)
                ? !string.IsNullOrWhiteSpace(photo.Category)
                : !string.Equals(photo.Category, category, StringComparison.Ordinal)));
    }

    private void UpdateFeedbackControlState()
    {
        var hasTerminalFeedbackState = IsFeedbackCommitted || IsFeedbackPassed || IsFeedbackLeft;
        IsStandardFeedbackActionsVisible = !_isFeedbackFinalized;
        CanLeaveFeedback = _feedbackSession is not null && !IsAuthorFlowVisible && !IsFeedbackLeft;
        IsLeaveFeedbackVisible = _isFeedbackFinalized && CanLeaveFeedback;
        IsLeaveFeedbackMenuItemVisible = !_isFeedbackFinalized && CanLeaveFeedback;
        CanUseAlbumMoreMenu = Photos.Count > 0 || IsLeaveFeedbackMenuItemVisible;
        CanModifyFeedback = _feedbackSession is not null && !hasTerminalFeedbackState && !_hasCollectedFeedback && !_isFeedbackFinalized;
        CanPassFeedback = _feedbackSession is not null && !hasTerminalFeedbackState && !_hasCollectedFeedback && !_isFeedbackFinalized;
        IsCollectFeedbackVisible = IsAuthorFlowVisible && !_hasCollectedFeedback && CommittedReviewers.Count > 0;
        CanCollectFeedback = IsCollectFeedbackVisible;
        CanFinalizeFeedback = IsAuthorFlowVisible && !_isFeedbackFinalized;
        CanStartNextRound = IsAuthorFlowVisible && !_isFeedbackFinalized && _unfrozenCollectedPhotoCount > 0;
        CanDeleteAlbum = IsAuthorFlowVisible && _currentManifest is not null;
        IsRegularFlowVisible = IsAuthorFlowVisible && !_isFeedbackFinalized;
        IsFinalizedFlowVisible = IsAuthorFlowVisible && _isFeedbackFinalized;
        FinalizedFeedbackMessage = _isFeedbackFinalized
            ? IsAuthorFlowVisible
                ? "The album has been finalized."
                : "The album has been finalized. Please leave when you are ready, so the author can safely delete the album."
            : "";
        if (_feedbackSession is null)
        {
            CanCommitFeedback = false;
            CanPassFeedback = false;
            CanLeaveFeedback = false;
            IsLeaveFeedbackVisible = false;
            IsLeaveFeedbackMenuItemVisible = false;
            CommitFeedbackStatus = "";
            UpdateBulkPhotoSelectionState();
            return;
        }

        if (_isFeedbackFinalized)
        {
            CanCommitFeedback = false;
            CommitFeedbackStatus = IsFeedbackLeft ? "you left the album" : "album finalized";
            UpdateBulkPhotoSelectionState();
            return;
        }

        if (IsFeedbackCommitted)
        {
            CanCommitFeedback = false;
            CommitFeedbackStatus = "feedback sent";
            UpdateBulkPhotoSelectionState();
            return;
        }

        if (IsFeedbackPassed)
        {
            CanCommitFeedback = false;
            CommitFeedbackStatus = "feedback passed";
            UpdateBulkPhotoSelectionState();
            return;
        }

        if (_hasCollectedFeedback)
        {
            CanCommitFeedback = false;
            CommitFeedbackStatus = "feedback collected";
            UpdateBulkPhotoSelectionState();
            return;
        }

        var visiblePhotos = GetVisiblePhotos().ToList();
        var uncategorizedCount = visiblePhotos.Count(photo => !photo.IsFrozen && string.IsNullOrWhiteSpace(photo.Category));
        var niceCount = visiblePhotos.Count(photo => string.Equals(photo.Category, "nice", StringComparison.Ordinal));
        var targetNiceCount = _currentManifest?.TargetNicePhotoCount ?? Math.Max(0, TargetNicePhotoCount);

        if (niceCount != targetNiceCount)
        {
            CanCommitFeedback = false;
            CommitFeedbackStatus = $"expecting {targetNiceCount} nice pictures";
            UpdateBulkPhotoSelectionState();
            return;
        }

        if (uncategorizedCount > 0)
        {
            CanCommitFeedback = false;
            CommitFeedbackStatus = $"{uncategorizedCount} uncategorized pictures left";
            UpdateBulkPhotoSelectionState();
            return;
        }        

        CanCommitFeedback = true;
        CommitFeedbackStatus = "ready to send";
        UpdateBulkPhotoSelectionState();
    }

    private async Task MovePhotoViewerInCategoryAsync(int offset)
    {
        if (_selectedViewedPhoto is null)
        {
            return;
        }

        var tabPhotos = GetPhotosForReviewTab(_selectedViewedPhotoSourceReviewTabId).ToList();
        if (tabPhotos.Count == 0)
        {
            ClosePhotoViewer();
            return;
        }

        var currentPhotoForNavigation = GetPhotoViewerNavigationAnchor(_selectedViewedPhoto);
        var currentIndex = tabPhotos.FindIndex(photo => ReferenceEquals(photo, currentPhotoForNavigation));
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        var nextIndex = (currentIndex + offset) % tabPhotos.Count;
        if (nextIndex < 0)
        {
            nextIndex += tabPhotos.Count;
        }

        await OpenPhotoViewerAsync(tabPhotos[nextIndex]);
    }

    private async Task AdvanceFullImageViewerAfterCategoryChangeAsync(string sourceCategory, int changedPhotoIndex)
    {
        var sourcePhotos = GetPhotosForCategory(sourceCategory).ToList();
        if (sourcePhotos.Count == 0)
        {
            ClosePhotoViewer();
            return;
        }

        var nextIndex = changedPhotoIndex >= 0 && changedPhotoIndex < sourcePhotos.Count
            ? changedPhotoIndex
            : 0;

        await OpenPhotoViewerAsync(sourcePhotos[nextIndex]);
    }

    private async Task AdvanceFullImageViewerAfterReviewTabItemResolvedAsync(string sourceReviewTabId, int changedPhotoIndex)
    {
        var sourcePhotos = GetPhotosForReviewTab(sourceReviewTabId).ToList();
        if (sourcePhotos.Count == 0)
        {
            ClosePhotoViewer();
            return;
        }

        var nextIndex = changedPhotoIndex >= 0 && changedPhotoIndex < sourcePhotos.Count
            ? changedPhotoIndex
            : 0;

        await OpenPhotoViewerAsync(sourcePhotos[nextIndex]);
    }

    private async Task OpenNextPhotoAfterDuplicateRemovalAsync(
        string sourceGroupId,
        IReadOnlyList<AlbumPhotoViewModel> sourceMembers,
        int sourceMemberIndex,
        string sourceReviewTabId,
        int sourceTabIndex)
    {
        if (_duplicateGroupsById.TryGetValue(sourceGroupId, out var remainingMembers))
        {
            var orderedCandidates = sourceMembers
                .Skip(Math.Max(0, sourceMemberIndex + 1))
                .Concat(sourceMembers.Take(Math.Max(0, sourceMemberIndex)))
                .Where(candidate => remainingMembers.Contains(candidate))
                .ToList();
            var nextGroupPhoto = orderedCandidates.FirstOrDefault() ?? remainingMembers.FirstOrDefault();
            if (nextGroupPhoto is not null)
            {
                await OpenPhotoViewerAsync(nextGroupPhoto);
                return;
            }
        }

        var sourcePhotos = GetPhotosForReviewTab(sourceReviewTabId).ToList();
        if (sourcePhotos.Count == 0)
        {
            ClosePhotoViewer();
            return;
        }

        var nextIndex = sourceTabIndex >= 0 && sourceTabIndex < sourcePhotos.Count
            ? sourceTabIndex
            : 0;
        await OpenPhotoViewerAsync(sourcePhotos[nextIndex]);
    }

    private IEnumerable<AlbumPhotoViewModel> GetPhotosForCategory(string category)
    {
        var photos = GetVisiblePhotos();
        return string.IsNullOrWhiteSpace(category)
            ? photos.Where(photo => string.IsNullOrWhiteSpace(photo.Category))
            : photos.Where(photo => string.Equals(photo.Category, category, StringComparison.Ordinal));
    }

    private IEnumerable<AlbumPhotoViewModel> GetPhotosForReviewTab(string tabId)
    {
        return tabId switch
        {
            NiceReviewTabId => GetPhotosForCategory("nice"),
            OkReviewTabId => GetPhotosForCategory("ok"),
            TrashReviewTabId => GetPhotosForCategory("trash"),
            UnresolvedDuplicatesReviewTabId => GetUnresolvedDuplicatePhotos(),
            _ => GetPhotosForCategory("")
        };
    }

    private AlbumPhotoViewModel GetPhotoViewerNavigationAnchor(AlbumPhotoViewModel photo)
    {
        return !string.IsNullOrWhiteSpace(photo.DuplicateGroupId) &&
            _duplicateGroupsById.TryGetValue(photo.DuplicateGroupId, out var members)
                ? members.FirstOrDefault(member => member.IsDuplicateGroupMain) ?? photo
                : photo;
    }

    private IEnumerable<AlbumPhotoViewModel> GetSelectedPhotos()
    {
        return Photos.Where(photo => photo.IsSelectedForBulk);
    }

    private IEnumerable<AlbumPhotoViewModel> GetVisiblePhotos()
    {
        return Photos.Where(photo => string.IsNullOrWhiteSpace(photo.DuplicateGroupId) || photo.IsDuplicateGroupMain);
    }

    private IEnumerable<AlbumPhotoViewModel> GetUnresolvedDuplicatePhotos()
    {
        return GetVisiblePhotos().Where(photo =>
            photo.HasDuplicateGroup &&
            _duplicateGroupsById.TryGetValue(photo.DuplicateGroupId, out var members) &&
            !members.Any(member => member.IsBestInDuplicateGroup));
    }

    private IEnumerable<AlbumPhotoViewModel> GetDuplicateGroupMembers(AlbumPhotoViewModel photo)
    {
        return !string.IsNullOrWhiteSpace(photo.DuplicateGroupId) &&
            _duplicateGroupsById.TryGetValue(photo.DuplicateGroupId, out var members)
                ? members
                : [photo];
    }

    private static string GetHighestPriorityCategory(IEnumerable<string> categories)
    {
        var categoryList = categories.ToList();
        if (categoryList.Any(category => string.Equals(category, "nice", StringComparison.Ordinal)))
        {
            return "nice";
        }

        if (categoryList.Any(category => string.Equals(category, "ok", StringComparison.Ordinal)))
        {
            return "ok";
        }

        if (categoryList.Any(category => string.Equals(category, "trash", StringComparison.Ordinal)))
        {
            return "trash";
        }

        return "";
    }

    private void ClearBulkPhotoSelection()
    {
        foreach (var photo in GetSelectedPhotos().ToList())
        {
            photo.IsSelectedForBulk = false;
        }

        UpdateBulkPhotoSelectionState();
    }

    private static bool UsesGoogleDriveBackend(AlbumManifest manifest)
    {
        return manifest.GoogleDrive is not null &&
            (string.Equals(manifest.DatabaseBackendType, "google-drive-folder", StringComparison.OrdinalIgnoreCase) ||
               manifest.Photos.Any(photo => string.Equals(photo.BackendType, "google-drive-file", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool UsesLocalFileSystemBackend(AlbumManifest manifest)
    {
        return manifest.LocalFileSystem is not null &&
            string.Equals(manifest.DatabaseBackendType, "local-file-system", StringComparison.OrdinalIgnoreCase);
    }

    private static bool UsesReviewerFeedbackBackend(AlbumManifest manifest)
    {
        return UsesGoogleDriveBackend(manifest) || UsesLocalFileSystemBackend(manifest);
    }

    private bool IsCurrentReviewerAuthor(AlbumManifest manifest)
    {
        var reviewer = _currentReviewerIdentity ?? CreateReviewerIdentity(manifest);
        if (reviewer?.UserId is not { Length: > 0 } reviewerUserId)
        {
            return false;
        }

        return string.Equals(manifest.Author.BackendType, reviewer.BackendType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(manifest.Author.UserId, reviewerUserId, StringComparison.Ordinal);
    }

    private async Task<IReviewerFeedbackBackend> CreateFeedbackBackendAsync(CancellationToken cancellationToken)
    {
        if (_currentManifest is null)
        {
            throw new InvalidOperationException("No album is open.");
        }

        return await CreateFeedbackBackendAsync(_currentManifest, cancellationToken);
    }

    private async Task<IReviewerFeedbackBackend> CreateFeedbackBackendAsync(
        AlbumManifest manifest,
        CancellationToken cancellationToken)
    {
        if (UsesGoogleDriveBackend(manifest))
        {
            if (manifest.GoogleDrive is null)
            {
                throw new InvalidOperationException("The Google Drive album details are missing.");
            }

            var accessToken = await GetGoogleAccessTokenAsync(cancellationToken);
            return new GoogleDriveReviewerFeedbackBackend(manifest.GoogleDrive.FeedbackFolderId, accessToken);
        }

        if (UsesLocalFileSystemBackend(manifest))
        {
            if (manifest.LocalFileSystem is null)
            {
                throw new InvalidOperationException("The local album details are missing.");
            }

            return new LocalFileSystemReviewerFeedbackBackend(manifest.LocalFileSystem.FeedbackFolderPath);
        }

        throw new InvalidOperationException("The album feedback backend is not supported.");
    }

    private static int NormalizeRotationDegrees(int rotationDegrees)
    {
        var normalized = rotationDegrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private FeedbackReviewerIdentity? CreateReviewerIdentity(AlbumManifest manifest)
    {
        if (UsesGoogleDriveBackend(manifest))
        {
            return _googleTokenSet is null ? null : CreateGoogleReviewerIdentity(_googleTokenSet);
        }

        if (UsesLocalFileSystemBackend(manifest))
        {
            return CreateLocalReviewerIdentity();
        }

        return null;
    }

    private static FeedbackReviewerIdentity CreateGoogleReviewerIdentity(GoogleOAuthTokenSet token)
    {
        return new FeedbackReviewerIdentity
        {
            BackendType = "google",
            UserId = token.UserId ?? throw new InvalidOperationException("Google user id is missing."),
            DisplayName = token.DisplayName,
            Email = token.Email
        };
    }

    private FeedbackReviewerIdentity? CreateLocalReviewerIdentity()
    {
        var name = AnonymousReviewerName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        SaveLocalUserSettings();
        return new FeedbackReviewerIdentity
        {
            BackendType = "local",
            UserId = SanitizeLocalReviewerId(name),
            DisplayName = name,
            Email = null
        };
    }

    private void SaveLocalUserSettings()
    {
        _localUserSettingsStore.Save(new LocalUserSettings
        {
            AnonymousReviewerName = AnonymousReviewerName.Trim(),
            MaximumParallelism = GetMaximumParallelism(),
            CacheThumbnails = CacheThumbnails,
            CacheOriginalImages = CacheOriginalImages,
            PictureDefaultDownloadDirectoryPath = PictureDefaultDownloadDirectoryPath.Trim(),
            UncategorizedDefaultDownloadDirectoryPath = UncategorizedDefaultDownloadDirectoryPath.Trim(),
            NiceDefaultDownloadDirectoryPath = NiceDefaultDownloadDirectoryPath.Trim(),
            OkDefaultDownloadDirectoryPath = OkDefaultDownloadDirectoryPath.Trim(),
            TrashDefaultDownloadDirectoryPath = TrashDefaultDownloadDirectoryPath.Trim()
        });
        ApplyImageCacheSettings();
    }

    private void ApplyImageCacheSettings()
    {
        _imageCache.CacheThumbnails = CacheThumbnails;
        _imageCache.CacheOriginalImages = CacheOriginalImages;
    }

    private void SaveAlbumOpenHistory()
    {
        _albumOpenHistoryStore.Save(new AlbumOpenHistory
        {
            LastOpenAlbumLink = _lastOpenAlbumLink.Trim(),
            RecentAlbums = RecentAlbums.Select(album => new RecentAlbumSettings
            {
                Title = album.Title,
                Link = album.Link,
                Location = album.Location,
                OpenedAt = album.OpenedAt
            }).ToList()
        });
    }

    private static string SanitizeLocalReviewerId(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "anonymous" : sanitized;
    }

    private int GetMaximumParallelism()
    {
        MaximumParallelism = NormalizeMaximumParallelism(MaximumParallelism);
        return MaximumParallelism;
    }

    private static int NormalizeMaximumParallelism(int value)
    {
        return Math.Clamp(value <= 0 ? LocalUserSettings.DefaultMaximumParallelism : value, 1, 64);
    }

    partial void OnSelectedAlbumTypeChanged(AlbumTypeOptionViewModel? value)
    {
        OnPropertyChanged(nameof(IsAlbumSettingsVisible));
        OnPropertyChanged(nameof(IsGoogleDriveAlbumSettingsVisible));
        OnPropertyChanged(nameof(IsLocalAlbumSettingsVisible));
    }

    partial void OnIsAuthorFlowVisibleChanged(bool value)
    {
        if (value && _isFlowTabActive)
        {
            StartFlowMonitor();
        }
        else
        {
            StopFlowMonitor();
        }
    }

    partial void OnIsFeedbackCommittedChanged(bool value)
    {
        UpdateFeedbackControlState();
        UpdateCurrentPhotoActionVisibility();
    }

    partial void OnIsFeedbackPassedChanged(bool value)
    {
        UpdateFeedbackControlState();
        UpdateCurrentPhotoActionVisibility();
    }

    partial void OnShareLinkChanged(string value)
    {
        OnPropertyChanged(nameof(IsCreateResultVisible));
    }

    partial void OnGoogleSignInUrlChanged(string value)
    {
        OnPropertyChanged(nameof(IsGoogleSignInInstructionVisible));
    }

    partial void OnIsGoogleSignInPendingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsGoogleSignInInstructionVisible));
        OnPropertyChanged(nameof(GoogleConnectionStatus));
    }

    partial void OnIsGoogleSignedInChanged(bool value)
    {
        OnPropertyChanged(nameof(GoogleConnectionStatus));
    }

    private void ResetCreateInputs()
    {
        AlbumTitle = "New album";
        TargetNicePhotoCount = 0;
        ImportDate = DateTimeOffset.Now;
        ParentDriveFolderId = "";
        SelectedDriveFolderName = "Please select a folder";
        IsDriveFolderSelected = false;
        SelectedLocalAlbumDestinationName = "Please select a folder";
        LocalAlbumDestinationPath = "";
        IsLocalAlbumDestinationSelected = false;
        CurrentDriveFolderId = "root";
        CurrentDriveFolderName = "My Drive";
        CurrentDriveFolderCanBeUsed = true;
        NewDriveFolderName = "";
        IsCreateDriveFolderDialogVisible = false;
        DriveItems.Clear();
        _driveFolderPath.Clear();
        _driveNextPageToken = null;
        OnPropertyChanged(nameof(HasMoreDriveItems));
        SelectedAlbumPhoto = null;
        AlbumPhotos.Clear();
        RefreshAlbumPhotoRows();
        ImportCandidates.Clear();
        RefreshImportCandidateRows();
        IsFolderDateImportVisible = false;
        FolderDateImportSourceText = "";
        _folderDateImportFolders.Clear();
        OnPropertyChanged(nameof(IsImportCandidatesVisible));
    }

    private void RefreshAlbumPhotoRows()
    {
        RefreshSourceRows(AlbumPhotoRows, AlbumPhotos);
        UpdateAlbumPhotoSourceSelectionState();
    }

    private void RefreshImportCandidateRows()
    {
        RefreshSourceRows(ImportCandidateRows, ImportCandidates);
    }

    private static void RefreshSourceRows(
        ObservableCollection<AlbumPhotoSourceRowViewModel> rows,
        IEnumerable<AlbumPhotoSourceViewModel> photos)
    {
        rows.Clear();
        foreach (var row in photos.Chunk(PhotosPerRow))
        {
            rows.Add(new AlbumPhotoSourceRowViewModel(row));
        }
    }

    private IEnumerable<AlbumPhotoSourceViewModel> GetSelectedAlbumPhotoSources()
    {
        return AlbumPhotos.Where(photo => photo.IsSelected);
    }

    private void ClearAlbumPhotoSourceSelection()
    {
        foreach (var photo in GetSelectedAlbumPhotoSources().ToList())
        {
            photo.IsSelected = false;
        }

        UpdateAlbumPhotoSourceSelectionState();
    }

    private void UpdateAlbumPhotoSourceSelectionState()
    {
        var selectedCount = AlbumPhotos.Count(photo => photo.IsSelected);
        SelectedAlbumPhotoSourceCount = selectedCount;
        IsAlbumPhotoSourceActionPanelVisible = selectedCount > 0;
        AlbumPhotoSourceSelectionStatus = selectedCount == 0
            ? ""
            : $"{selectedCount} selected";
    }

    private async Task<string> GetGoogleAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_googleTokenSet is null)
        {
            throw new InvalidOperationException("Google Drive is not connected.");
        }

        if (_googleTokenSet.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            _googleTokenSet = await PrepareGoogleTokenAsync(_googleTokenSet, cancellationToken);
            _tokenStore.Save(_googleTokenSet);
            IsGoogleSignedIn = true;
            OnPropertyChanged(nameof(GoogleConnectionStatus));
            return _googleTokenSet.AccessToken;
        }

        if (string.IsNullOrWhiteSpace(_googleTokenSet.RefreshToken))
        {
            IsGoogleSignedIn = false;
            throw new InvalidOperationException("Google session expired. Sign in again.");
        }

        var clientId = _settingsProvider.GoogleOAuthClientId;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            IsGoogleSignedIn = false;
            throw new InvalidOperationException(_settingsProvider.MissingGoogleOAuthClientIdMessage);
        }

        try
        {
            var currentToken = _googleTokenSet;
            var refreshedToken = await _oauthClient.RefreshAsync(
                clientId.Trim(),
                _settingsProvider.GoogleOAuthClientSecret,
                currentToken.RefreshToken,
                cancellationToken);
            _googleTokenSet = await PrepareGoogleTokenAsync(
                refreshedToken with
                {
                    UserId = currentToken.UserId,
                    Email = currentToken.Email,
                    DisplayName = currentToken.DisplayName
                },
                cancellationToken);
            _tokenStore.Save(_googleTokenSet);
        }
        catch
        {
            _tokenStore.Clear();
            IsGoogleSignedIn = false;
            throw;
        }

        IsGoogleSignedIn = true;
        OnPropertyChanged(nameof(GoogleConnectionStatus));
        return _googleTokenSet.AccessToken;
    }

    private async Task<GoogleOAuthTokenSet> PrepareGoogleTokenAsync(GoogleOAuthTokenSet tokenSet, CancellationToken cancellationToken)
    {
        EnsureRequiredGoogleScopes(tokenSet);

        if (!string.IsNullOrWhiteSpace(tokenSet.UserId))
        {
            return tokenSet;
        }

        var userInfo = await _googleUserInfoClient.GetUserInfoAsync(tokenSet.AccessToken, cancellationToken);
        return tokenSet with
        {
            UserId = userInfo.UserId,
            Email = userInfo.Email,
            DisplayName = userInfo.DisplayName
        };
    }

    private static void EnsureRequiredGoogleScopes(GoogleOAuthTokenSet tokenSet)
    {
        if (string.IsNullOrWhiteSpace(tokenSet.Scope))
        {
            throw new InvalidOperationException("Google authorization is missing scope information. Sign in again.");
        }

        var scopes = tokenSet.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!scopes.Contains("https://www.googleapis.com/auth/drive", StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Google Drive permission was not granted. Sign in again and allow Drive access.");
        }

        if (!scopes.Contains("openid", StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Google identity permission was not granted. Sign in again and allow profile access.");
        }
    }
}
