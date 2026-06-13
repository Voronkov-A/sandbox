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
    private const int PhotosPerRow = 4;

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
    private bool _isAuthorFlowVisible;

    [ObservableProperty]
    private bool _isRegularFlowVisible;

    [ObservableProperty]
    private bool _isFinalizedFlowVisible;

    [ObservableProperty]
    private string _flowStatus = "";

    [ObservableProperty]
    private AlbumPhotoSourceViewModel? _selectedAlbumPhoto;

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

    public ObservableCollection<AlbumPhotoSourceViewModel> ImportCandidates { get; } = new();

    public ObservableCollection<DriveItemViewModel> DriveItems { get; } = new();

    public ObservableCollection<AlbumPhotoViewModel> Photos { get; } = new();

    public ObservableCollection<AlbumPhotoGroupViewModel> UncategorizedPhotoGroups { get; } = new();

    public ObservableCollection<AlbumPhotoGroupViewModel> NicePhotoGroups { get; } = new();

    public ObservableCollection<AlbumPhotoGroupViewModel> OkPhotoGroups { get; } = new();

    public ObservableCollection<AlbumPhotoGroupViewModel> TrashPhotoGroups { get; } = new();

    public ObservableCollection<ReviewerFeedbackFlowItemViewModel> CommittedReviewers { get; } = new();

    public ObservableCollection<ReviewerFeedbackFlowItemViewModel> PassedReviewers { get; } = new();

    public ObservableCollection<ReviewerFeedbackFlowItemViewModel> LeftReviewers { get; } = new();

    public ObservableCollection<ReviewerFeedbackFlowItemViewModel> InProgressReviewers { get; } = new();

    public ObservableCollection<AlbumDownloadCategoryViewModel> AlbumDownloadCategories { get; } = new();

    public string UncategorizedTabHeader => $"Uncategorized ({Photos.Count(photo => string.IsNullOrWhiteSpace(photo.Category))})";

    public string NiceTabHeader => $"Nice ({Photos.Count(photo => string.Equals(photo.Category, "nice", StringComparison.Ordinal))})";

    public string OkTabHeader => $"Ok ({Photos.Count(photo => string.Equals(photo.Category, "ok", StringComparison.Ordinal))})";

    public string TrashTabHeader => $"Trash ({Photos.Count(photo => string.Equals(photo.Category, "trash", StringComparison.Ordinal))})";

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
    private AlbumManifest? _currentManifest;
    private AlbumManifest? _pendingGoogleAuthorizationManifest;
    private AlbumPhotoViewModel? _selectedViewedPhoto;
    private string _selectedViewedPhotoSourceCategory = "";
    private ReviewerFeedbackSession? _feedbackSession;
    private ReviewerFeedbackDatabase? _feedbackDatabase;
    private ReviewerFeedbackStatus? _feedbackStatus;
    private FeedbackReviewerIdentity? _currentReviewerIdentity;
    private bool _isFlowTabActive;
    private int _unfrozenCollectedPhotoCount;
    private bool _hasCollectedFeedback;
    private bool _isFeedbackFinalized;
    private readonly SemaphoreSlim _feedbackSyncGate = new(1);
    private string? _driveNextPageToken;

    public bool HasMoreDriveItems => !string.IsNullOrWhiteSpace(_driveNextPageToken);

    public MainViewModel()
    {
        _localUserSettingsStore = new LocalUserSettingsStore(_settingsProvider.LocalStorageRootPath);
        _tokenStore = new GoogleOAuthTokenStore(_settingsProvider.LocalStorageRootPath);
        _reviewerFeedbackService = new ReviewerFeedbackService(_settingsProvider.LocalStorageRootPath);
        _imageCache = new ImageCacheService(_settingsProvider.LocalStorageRootPath);
        _albumDeletionService = new AlbumDeletionService(
            _reviewerFeedbackService,
            _imageCache,
            _settingsProvider.LocalStorageRootPath);
        var localSettings = _localUserSettingsStore.Load();
        AnonymousReviewerName = localSettings.AnonymousReviewerName;
        PictureDefaultDownloadDirectoryPath = GetConfiguredOrDefaultDownloadDirectoryPath(localSettings.PictureDefaultDownloadDirectoryPath);
        UncategorizedDefaultDownloadDirectoryPath = GetConfiguredOrDefaultDownloadDirectoryPath(localSettings.UncategorizedDefaultDownloadDirectoryPath);
        NiceDefaultDownloadDirectoryPath = GetConfiguredOrDefaultDownloadDirectoryPath(localSettings.NiceDefaultDownloadDirectoryPath);
        OkDefaultDownloadDirectoryPath = GetConfiguredOrDefaultDownloadDirectoryPath(localSettings.OkDefaultDownloadDirectoryPath);
        TrashDefaultDownloadDirectoryPath = GetConfiguredOrDefaultDownloadDirectoryPath(localSettings.TrashDefaultDownloadDirectoryPath);
        _googleTokenSet = _tokenStore.Load();
        IsGoogleSignedIn = _googleTokenSet is not null;
        ResetCreateInputs();
        _ = ResumePendingAlbumDeletionsAsync();
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
                Status = $"Choose a destination directory for {category.CategoryName}.";
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

        LeaveConfirmationMessage = "Leave this finalized album? The author will see that you are done.";
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
        if (_feedbackSession is null || _currentReviewerIdentity is null || !CanLeaveFeedback)
        {
            IsLeaveConfirmationVisible = false;
            return;
        }

        IsLeaveConfirmationVisible = false;
        LeaveConfirmationMessage = "";

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
            Status = result.RemoteWon
                ? "You had already left this album remotely. The remote state was loaded."
                : "You left the finalized album.";
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
            await ExecuteAlbumDeletionAsync(manifest, reviewer, backend, CancellationToken.None);
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
    }

    [RelayCommand]
    private void TogglePhotoViewerActions()
    {
        IsPhotoViewerActionsVisible = !IsPhotoViewerActionsVisible;
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

    public async Task DownloadCurrentPhotoAsync(string destinationDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectoryPath))
        {
            Status = "Choose a destination folder.";
            return;
        }

        await CopyCurrentOriginalPhotoAsync(destinationDirectoryPath, "Downloaded");
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
            Status = SelectedAlbumType.Id == LocalAlbumTypeId
                ? "Creating local album..."
                : "Uploading selected local photos to Google Drive...";
            Photos.Clear();
            ShareLink = "";
            DriveFolderLink = "";

            if (SelectedAlbumType.Id == LocalAlbumTypeId)
            {
                var result = await _localPublisher.PublishAsync(
                    new LocalAlbumPublishRequest
                    {
                        Title = string.IsNullOrWhiteSpace(AlbumTitle) ? "Picshare album" : AlbumTitle.Trim(),
                        Photos = AlbumPhotos.Select(photo => photo.Source).ToList(),
                        TargetNicePhotoCount = Math.Max(0, TargetNicePhotoCount),
                        ParentFolderPath = LocalAlbumDestinationPath,
                        Author = CreateLocalReviewerIdentity()!
                    },
                    CancellationToken.None);

                ShareLink = result.PicshareLink;
                DriveFolderLink = result.AlbumFolderPath;
                OpenAlbumLink = result.PicshareLink;
                Status = $"Local album created with {result.Manifest.Photos.Count} photo(s).";
                await LoadAlbumAsync(result.Manifest);
            }
            else
            {
                var accessToken = await GetGoogleAccessTokenAsync(CancellationToken.None);
                var result = await _publisher.PublishAsync(
                    new DriveAlbumPublishRequest
                    {
                        Title = string.IsNullOrWhiteSpace(AlbumTitle) ? "Picshare album" : AlbumTitle.Trim(),
                        Photos = AlbumPhotos.Select(photo => photo.Source).ToList(),
                        TargetNicePhotoCount = Math.Max(0, TargetNicePhotoCount),
                        ParentDriveFolderId = string.IsNullOrWhiteSpace(ParentDriveFolderId) ? null : ParentDriveFolderId.Trim(),
                        AccessToken = accessToken,
                        Author = CreateGoogleReviewerIdentity(_googleTokenSet!)
                    },
                    CancellationToken.None);

                ShareLink = result.PicshareLink;
                DriveFolderLink = result.AlbumFolderUrl;
                OpenAlbumLink = result.PicshareLink;
                Status = $"Album created with {result.Manifest.Photos.Count} photo(s).";
                await LoadAlbumAsync(result.Manifest);
            }
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
        OnPropertyChanged(nameof(IsImportCandidatesVisible));
        Status = "Choose a date and scan the selected folders.";
    }

    public void CloseFolderDateImport()
    {
        IsFolderDateImportVisible = false;
        FolderDateImportSourceText = "";
        _folderDateImportFolders.Clear();
        ImportCandidates.Clear();
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

            ImportCandidates.Clear();
            foreach (var photo in photos)
            {
                if (AlbumPhotos.Any(existing => string.Equals(existing.Source.SortKey, photo.SortKey, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var photoViewModel = new AlbumPhotoSourceViewModel(photo);
                ImportCandidates.Add(photoViewModel);
                StartThumbnailLoad(photoViewModel);
            }

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
        foreach (var file in files)
        {
            var localPath = file.TryGetLocalPath();
            var key = !string.IsNullOrWhiteSpace(localPath)
                ? Path.GetFullPath(localPath)
                : file.Path.AbsoluteUri;

            if (AlbumPhotos.Any(photo => string.Equals(photo.Source.SortKey, key, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var photoViewModel = new AlbumPhotoSourceViewModel(new PhotoUploadSource(
                file.Name,
                key,
                async () => await file.OpenReadAsync()));
            AlbumPhotos.Add(photoViewModel);
            StartThumbnailLoad(photoViewModel);
            added++;
        }

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
        OnPropertyChanged(nameof(IsImportCandidatesVisible));
    }

    public void AddImportCandidates(IEnumerable<AlbumPhotoSourceViewModel> candidates)
    {
        var added = 0;
        foreach (var candidate in candidates.ToList())
        {
            if (AlbumPhotos.Any(photo => string.Equals(photo.Source.SortKey, candidate.Source.SortKey, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            AlbumPhotos.Add(candidate);
            StartThumbnailLoad(candidate);
            added++;
        }

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
        Status = $"Removed {removed} photo(s) from the album.";
    }

    private static void StartThumbnailLoad(AlbumPhotoSourceViewModel photo)
    {
        _ = photo.LoadThumbnailAsync();
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
        if (IsBusy)
        {
            return;
        }

        var localManifestPath = AlbumLinkParser.TryGetLocalManifestPath(OpenAlbumLink);
        var manifestFileId = localManifestPath is null
            ? AlbumLinkParser.TryGetManifestFileId(OpenAlbumLink)
            : null;
        if (string.IsNullOrWhiteSpace(manifestFileId) && string.IsNullOrWhiteSpace(localManifestPath))
        {
            Status = "Paste a Picshare link, Google Drive file link, manifest file id, or local album.json path.";
            return;
        }

        try
        {
            IsBusy = true;
            Status = "Opening album...";
            var manifest = localManifestPath is not null
                ? await _albumLoader.LoadFromLocalFileAsync(localManifestPath, CancellationToken.None)
                : await _albumLoader.LoadFromPublicDriveFileAsync(manifestFileId!, CancellationToken.None);
            DriveFolderLink = manifest.GoogleDrive?.AlbumFolderUrl ?? manifest.LocalFileSystem?.RootPath ?? "";
            ShareLink = manifest.GoogleDrive is not null
                ? AlbumLinkParser.CreatePicshareLink(manifest.GoogleDrive.ManifestFileId, manifest.GoogleDrive.AlbumFolderId)
                : AlbumLinkParser.CreateLocalPicshareLink(manifest.LocalFileSystem!.ManifestFilePath);

            if (await RequiresGoogleAuthorizationPromptAsync(manifest))
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
                Status = $"Opened {manifest.Title} with {manifest.Photos.Count} photo(s).";
            }
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
    }

    public void StopPhotoViewportLoad(AlbumPhotoViewModel photo)
    {
        photo.StopViewportLoad();
    }

    private async Task SetCurrentPhotoCategoryAsync(string category)
    {
        if (_selectedViewedPhoto is null || _selectedViewedPhoto.IsFrozen || IsFeedbackCommitted || IsFeedbackPassed)
        {
            return;
        }

        var changedPhoto = _selectedViewedPhoto;
        var sourceCategory = _selectedViewedPhotoSourceCategory;
        var sourcePhotosBeforeChange = GetPhotosForCategory(sourceCategory).ToList();
        var changedPhotoIndex = sourcePhotosBeforeChange.FindIndex(photo => ReferenceEquals(photo, changedPhoto));
        _selectedViewedPhoto.Category = category;

        if (_feedbackSession is not null && _feedbackDatabase is not null)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                await _reviewerFeedbackService.RemoveLocalDecisionAsync(
                    _feedbackSession,
                    _feedbackDatabase,
                    _selectedViewedPhoto.PhotoId,
                    CancellationToken.None);
            }
            else
            {
                await _reviewerFeedbackService.SaveLocalDecisionAsync(
                    _feedbackSession,
                    _feedbackDatabase,
                    _selectedViewedPhoto.PhotoId,
                    category,
                    CancellationToken.None);
            }
            _ = SyncFeedbackAsync();
        }

        RebuildCategoryRows();
        UpdateCurrentPhotoActionVisibility();
        Status = string.IsNullOrWhiteSpace(category)
            ? $"{changedPhoto.FileName} moved to uncategorized."
            : $"{changedPhoto.FileName} marked as {category}.";

        await AdvanceFullImageViewerAfterCategoryChangeAsync(sourceCategory, changedPhotoIndex);
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

    partial void OnPhotoViewerImageChanging(Bitmap? value)
    {
        if (PhotoViewerImage is not null && !ReferenceEquals(PhotoViewerImage, value))
        {
            PhotoViewerImage.Dispose();
        }
    }

    private async Task CopyCurrentOriginalPhotoAsync(string destinationDirectoryPath, string successVerb)
    {
        if (_selectedViewedPhoto is not { } photo)
        {
            Status = "No photo is selected.";
            return;
        }

        try
        {
            IsBusy = true;
            Directory.CreateDirectory(destinationDirectoryPath);

            var cachePath = await _imageCache.GetOrDownloadAsync(
                photo.AlbumId,
                GetFullPhotoCacheFileName(photo),
                photo.DownloadUrl,
                _imageHttpClient,
                CancellationToken.None);
            var destinationPath = GetAvailableDestinationPath(
                destinationDirectoryPath,
                GetSafeDownloadFileName(photo));

            await using var source = File.OpenRead(cachePath);
            await using var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            await source.CopyToAsync(destination, CancellationToken.None);

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
            defaultModeId));
    }

    private async Task DownloadAlbumCategoryFilesAsync(
        AlbumDownloadCategoryViewModel category,
        IReadOnlyList<AlbumPhotoViewModel> photos,
        CancellationToken cancellationToken)
    {
        var destinationDirectory = Path.GetFullPath(category.DestinationDirectoryPath.Trim());
        Directory.CreateDirectory(destinationDirectory);

        foreach (var photo in photos)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AlbumDownloadProgressMessage = $"Downloading {category.CategoryName}: {photo.FileName}";

            var destinationPath = GetAvailableDestinationPath(destinationDirectory, GetSafeDownloadFileName(photo));
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
                        cancellationToken);
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

            AlbumDownloadProgressValue++;
        }
    }

    private async Task DownloadAlbumCategoryArchiveAsync(
        AlbumDownloadCategoryViewModel category,
        IReadOnlyList<AlbumPhotoViewModel> photos,
        CancellationToken cancellationToken)
    {
        var destinationDirectory = Path.GetFullPath(category.DestinationDirectoryPath.Trim());
        Directory.CreateDirectory(destinationDirectory);
        var archiveName = GetSafeArchiveFileName($"{CurrentAlbumTitle}-{category.CategoryName}.zip");
        var archivePath = GetAvailableDestinationPath(destinationDirectory, archiveName);
        var tempArchivePath = Path.Combine(destinationDirectory, $".{Guid.NewGuid():N}.zip.tmp");
        var entryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(tempArchivePath, System.IO.Compression.ZipArchiveMode.Create))
            {
                foreach (var photo in photos)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AlbumDownloadProgressMessage = $"Archiving {category.CategoryName}: {photo.FileName}";

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

            File.Move(tempArchivePath, archivePath);
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
        var targetPath = Path.Combine(destinationDirectoryPath, fileName);
        if (!File.Exists(targetPath))
        {
            return targetPath;
        }

        var extension = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        for (var index = 1; ; index++)
        {
            targetPath = Path.Combine(destinationDirectoryPath, $"{baseName} ({index}){extension}");
            if (!File.Exists(targetPath))
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
            Status = $"Opened {manifest.Title} with {manifest.Photos.Count} photo(s).";
        }
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
                await ExecuteAlbumDeletionAsync(manifest, reviewerIdentity, backend, CancellationToken.None);
            }
            else
            {
                ClearOpenedAlbumState();
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
        CancellationToken cancellationToken)
    {
        await _albumDeletionService.RequestDeletionAsync(
            manifest,
            reviewer,
            backend,
            GetGoogleAccessTokenAsync,
            cancellationToken);

        ClearOpenedAlbumState();
        Status = "Album deleted.";
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
        Photos.Clear();
        ClearCategoryRows();
        SelectViewedPhoto(null);
        ClosePhotoViewer();
        ClearFlowReviewers();
        UpdateFeedbackControlState();
        UpdateCurrentPhotoActionVisibility();
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
                    await _albumDeletionService.RequestDeletionAsync(
                        manifest,
                        pendingDeletion.RequestedBy,
                        backend,
                        GetGoogleAccessTokenAsync,
                        CancellationToken.None);
                }
                catch
                {
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

        RebuildCategoryRows();
        if (_selectedViewedPhoto is not null)
        {
            PhotoViewerRotationDegrees = _selectedViewedPhoto.RotationDegrees;
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
        AddGroups(UncategorizedPhotoGroups, Photos.Where(photo => string.IsNullOrWhiteSpace(photo.Category)));
        AddGroups(NicePhotoGroups, Photos.Where(photo => string.Equals(photo.Category, "nice", StringComparison.Ordinal)));
        AddGroups(OkPhotoGroups, Photos.Where(photo => string.Equals(photo.Category, "ok", StringComparison.Ordinal)));
        AddGroups(TrashPhotoGroups, Photos.Where(photo => string.Equals(photo.Category, "trash", StringComparison.Ordinal)));
        OnPropertyChanged(nameof(UncategorizedTabHeader));
        OnPropertyChanged(nameof(NiceTabHeader));
        OnPropertyChanged(nameof(OkTabHeader));
        OnPropertyChanged(nameof(TrashTabHeader));
        UpdateFeedbackControlState();
    }

    private void ClearCategoryRows()
    {
        UncategorizedPhotoGroups.Clear();
        NicePhotoGroups.Clear();
        OkPhotoGroups.Clear();
        TrashPhotoGroups.Clear();
    }

    private static void AddGroups(ObservableCollection<AlbumPhotoGroupViewModel> groups, IEnumerable<AlbumPhotoViewModel> photos)
    {
        var materialized = photos.ToList();
        AddGroup(groups, "Uncommitted", materialized.Where(photo => !photo.IsFrozen));
        AddGroup(groups, "Committed", materialized.Where(photo => photo.IsFrozen));
    }

    private static void AddGroup(
        ObservableCollection<AlbumPhotoGroupViewModel> groups,
        string header,
        IEnumerable<AlbumPhotoViewModel> photos)
    {
        var materialized = photos.ToList();
        if (materialized.Count == 0)
        {
            return;
        }

        groups.Add(new AlbumPhotoGroupViewModel(header, materialized, PhotosPerRow));
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

    public void SetFlowTabActive(bool isActive)
    {
        _isFlowTabActive = isActive;
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

        if (_selectedViewedPhoto is not null)
        {
            _selectedViewedPhoto.IsSelectedForViewing = true;
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
        CanNavigatePhotoViewerCategory = _selectedViewedPhoto is not null && GetPhotosForCategory(category).Any();
        CanDownloadCurrentPhoto = _selectedViewedPhoto is not null;
    }

    private void UpdateFeedbackControlState()
    {
        var hasTerminalFeedbackState = IsFeedbackCommitted || IsFeedbackPassed || IsFeedbackLeft;
        CanUseAlbumMoreMenu = Photos.Count > 0;
        IsStandardFeedbackActionsVisible = !_isFeedbackFinalized;
        IsLeaveFeedbackVisible = _isFeedbackFinalized && _feedbackSession is not null && !IsFeedbackLeft;
        CanModifyFeedback = _feedbackSession is not null && !hasTerminalFeedbackState && !_hasCollectedFeedback && !_isFeedbackFinalized;
        CanPassFeedback = _feedbackSession is not null && !hasTerminalFeedbackState && !_hasCollectedFeedback && !_isFeedbackFinalized;
        CanLeaveFeedback = IsLeaveFeedbackVisible;
        IsCollectFeedbackVisible = IsAuthorFlowVisible && !_hasCollectedFeedback && CommittedReviewers.Count > 0;
        CanCollectFeedback = IsCollectFeedbackVisible;
        CanFinalizeFeedback = IsAuthorFlowVisible && !_isFeedbackFinalized;
        CanStartNextRound = IsAuthorFlowVisible && !_isFeedbackFinalized && _unfrozenCollectedPhotoCount > 0;
        CanDeleteAlbum = IsAuthorFlowVisible && _currentManifest is not null;
        IsRegularFlowVisible = IsAuthorFlowVisible && !_isFeedbackFinalized;
        IsFinalizedFlowVisible = IsAuthorFlowVisible && _isFeedbackFinalized;
        FinalizedFeedbackMessage = _isFeedbackFinalized
            ? "The album has been finalized. Please leave when you are ready, so the author can safely delete the album."
            : "";
        if (_feedbackSession is null)
        {
            CanCommitFeedback = false;
            CanPassFeedback = false;
            CanLeaveFeedback = false;
            CommitFeedbackStatus = "";
            return;
        }

        if (_isFeedbackFinalized)
        {
            CanCommitFeedback = false;
            CommitFeedbackStatus = IsFeedbackLeft ? "you left the album" : "album finalized";
            return;
        }

        if (IsFeedbackCommitted)
        {
            CanCommitFeedback = false;
            CommitFeedbackStatus = "feedback sent";
            return;
        }

        if (IsFeedbackPassed)
        {
            CanCommitFeedback = false;
            CommitFeedbackStatus = "feedback passed";
            return;
        }

        if (_hasCollectedFeedback)
        {
            CanCommitFeedback = false;
            CommitFeedbackStatus = "feedback collected";
            return;
        }

        var uncategorizedCount = Photos.Count(photo => !photo.IsFrozen && string.IsNullOrWhiteSpace(photo.Category));
        var niceCount = Photos.Count(photo => string.Equals(photo.Category, "nice", StringComparison.Ordinal));
        var targetNiceCount = _currentManifest?.TargetNicePhotoCount ?? Math.Max(0, TargetNicePhotoCount);

        if (niceCount != targetNiceCount)
        {
            CanCommitFeedback = false;
            CommitFeedbackStatus = $"expecting {targetNiceCount} nice pictures";
            return;
        }

        if (uncategorizedCount > 0)
        {
            CanCommitFeedback = false;
            CommitFeedbackStatus = $"{uncategorizedCount} uncategorized pictures left";
            return;
        }        

        CanCommitFeedback = true;
        CommitFeedbackStatus = "ready to send";
    }

    private async Task MovePhotoViewerInCategoryAsync(int offset)
    {
        if (_selectedViewedPhoto is null)
        {
            return;
        }

        var categoryPhotos = GetPhotosForCategory(_selectedViewedPhotoSourceCategory).ToList();
        if (categoryPhotos.Count == 0)
        {
            ClosePhotoViewer();
            return;
        }

        var currentIndex = categoryPhotos.FindIndex(photo => ReferenceEquals(photo, _selectedViewedPhoto));
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        var nextIndex = (currentIndex + offset) % categoryPhotos.Count;
        if (nextIndex < 0)
        {
            nextIndex += categoryPhotos.Count;
        }

        await OpenPhotoViewerAsync(categoryPhotos[nextIndex]);
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

    private IEnumerable<AlbumPhotoViewModel> GetPhotosForCategory(string category)
    {
        return string.IsNullOrWhiteSpace(category)
            ? Photos.Where(photo => string.IsNullOrWhiteSpace(photo.Category))
            : Photos.Where(photo => string.Equals(photo.Category, category, StringComparison.Ordinal));
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
            PictureDefaultDownloadDirectoryPath = PictureDefaultDownloadDirectoryPath.Trim(),
            UncategorizedDefaultDownloadDirectoryPath = UncategorizedDefaultDownloadDirectoryPath.Trim(),
            NiceDefaultDownloadDirectoryPath = NiceDefaultDownloadDirectoryPath.Trim(),
            OkDefaultDownloadDirectoryPath = OkDefaultDownloadDirectoryPath.Trim(),
            TrashDefaultDownloadDirectoryPath = TrashDefaultDownloadDirectoryPath.Trim()
        });
    }

    private static string SanitizeLocalReviewerId(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "anonymous" : sanitized;
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
        ImportCandidates.Clear();
        IsFolderDateImportVisible = false;
        FolderDateImportSourceText = "";
        _folderDateImportFolders.Clear();
        OnPropertyChanged(nameof(IsImportCandidatesVisible));
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
