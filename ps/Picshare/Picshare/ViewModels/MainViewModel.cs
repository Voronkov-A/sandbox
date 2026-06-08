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
    private bool _isPhotoViewerVisible;

    [ObservableProperty]
    private string _photoViewerTitle = "";

    [ObservableProperty]
    private string _photoViewerStatus = "";

    [ObservableProperty]
    private Bitmap? _photoViewerImage;

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
    private bool _isCollectFeedbackConfirmationVisible;

    [ObservableProperty]
    private string _collectFeedbackConfirmationMessage = "";

    [ObservableProperty]
    private bool _canCollectFeedback;

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
    private bool _isAuthorFlowVisible;

    [ObservableProperty]
    private string _flowStatus = "";

    [ObservableProperty]
    private AlbumPhotoSourceViewModel? _selectedAlbumPhoto;

    public ObservableCollection<AlbumTypeOptionViewModel> AlbumTypes { get; } = new()
    {
        new AlbumTypeOptionViewModel(
            LocalToGoogleDriveAlbumTypeId,
            "Upload local photos to Google Drive",
            "Create a trusted shared Drive folder from local photos captured on the selected date.")
    };

    public bool IsAlbumSettingsVisible => SelectedAlbumType?.Id == LocalToGoogleDriveAlbumTypeId;

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

    public ObservableCollection<ReviewerFeedbackFlowItemViewModel> InProgressReviewers { get; } = new();

    public string UncategorizedTabHeader => $"Uncategorized ({Photos.Count(photo => string.IsNullOrWhiteSpace(photo.Category))})";

    public string NiceTabHeader => $"Nice ({Photos.Count(photo => string.Equals(photo.Category, "nice", StringComparison.Ordinal))})";

    public string OkTabHeader => $"Ok ({Photos.Count(photo => string.Equals(photo.Category, "ok", StringComparison.Ordinal))})";

    public string TrashTabHeader => $"Trash ({Photos.Count(photo => string.Equals(photo.Category, "trash", StringComparison.Ordinal))})";

    public string FlowTabHeader => "Flow";

    public string CommittedReviewersHeader => $"Committed reviewers ({CommittedReviewers.Count})";

    public string PassedReviewersHeader => $"Passed ({PassedReviewers.Count})";

    public string InProgressReviewersHeader => $"In progress ({InProgressReviewers.Count})";

    private readonly GoogleDriveAlbumPublisher _publisher = new();
    private readonly AlbumLoader _albumLoader = new();
    private readonly LocalPhotoScanner _photoScanner = new();
    private readonly GoogleOAuthClient _oauthClient = new();
    private readonly GoogleUserInfoClient _googleUserInfoClient = new();
    private readonly GoogleOAuthTokenStore _tokenStore = new();
    private readonly ReviewerFeedbackService _reviewerFeedbackService = new();
    private readonly PicshareSettingsProvider _settingsProvider = new();
    private readonly ImageCacheService _imageCache = new();
    private readonly HttpClient _imageHttpClient = new();
    private readonly List<IStorageFolder> _folderDateImportFolders = new();
    private readonly List<DriveFolderLocation> _driveFolderPath = new();
    private GoogleOAuthTokenSet? _googleTokenSet;
    private CancellationTokenSource? _googleSignInCancellation;
    private CancellationTokenSource? _photoViewerCancellation;
    private CancellationTokenSource? _feedbackSyncCancellation;
    private CancellationTokenSource? _flowMonitorCancellation;
    private AlbumManifest? _currentManifest;
    private AlbumManifest? _pendingGoogleAuthorizationManifest;
    private AlbumPhotoViewModel? _selectedViewedPhoto;
    private string _selectedViewedPhotoSourceCategory = "";
    private ReviewerFeedbackSession? _feedbackSession;
    private ReviewerFeedbackDatabase? _feedbackDatabase;
    private ReviewerFeedbackStatus? _feedbackStatus;
    private bool _isFlowTabActive;
    private int _unfrozenCollectedPhotoCount;
    private readonly SemaphoreSlim _feedbackSyncGate = new(1);
    private string? _driveNextPageToken;

    public bool HasMoreDriveItems => !string.IsNullOrWhiteSpace(_driveNextPageToken);

    public MainViewModel()
    {
        _googleTokenSet = _tokenStore.Load();
        IsGoogleSignedIn = _googleTokenSet is not null;
        ResetCreateInputs();
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
        if (_feedbackSession is null || _googleTokenSet is null || !CanCommitFeedback)
        {
            IsCommitConfirmationVisible = false;
            return;
        }

        IsCommitConfirmationVisible = false;
        CommitConfirmationMessage = "";

        try
        {
            var accessToken = await GetGoogleAccessTokenAsync(CancellationToken.None);
            var result = await _reviewerFeedbackService.CommitAsync(
                _feedbackSession,
                CreateGoogleReviewerIdentity(_googleTokenSet),
                accessToken,
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
        if (_feedbackSession is null || _googleTokenSet is null || !CanPassFeedback)
        {
            IsPassConfirmationVisible = false;
            return;
        }

        IsPassConfirmationVisible = false;
        PassConfirmationMessage = "";

        try
        {
            var accessToken = await GetGoogleAccessTokenAsync(CancellationToken.None);
            var result = await _reviewerFeedbackService.PassAsync(
                _feedbackSession,
                CreateGoogleReviewerIdentity(_googleTokenSet),
                accessToken,
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
        if (_currentManifest is null || _googleTokenSet is null || !CanCollectFeedback)
        {
            IsCollectFeedbackConfirmationVisible = false;
            return;
        }

        IsCollectFeedbackConfirmationVisible = false;
        CollectFeedbackConfirmationMessage = "";

        try
        {
            IsBusy = true;
            var accessToken = await GetGoogleAccessTokenAsync(CancellationToken.None);
            var result = await _reviewerFeedbackService.CollectFeedbackAsync(
                _currentManifest,
                accessToken,
                CancellationToken.None);

            await SyncFeedbackAsync();
            await RefreshFlowAsync(CancellationToken.None);
            _unfrozenCollectedPhotoCount = result.UnfrozenPhotoCount;
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
        if (_currentManifest is null || _googleTokenSet is null || !CanStartNextRound)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var accessToken = await GetGoogleAccessTokenAsync(CancellationToken.None);
            var reviewerCount = await _reviewerFeedbackService.StartNextRoundAsync(
                _currentManifest,
                accessToken,
                CancellationToken.None);

            await SyncFeedbackAsync();
            await RefreshFlowAsync(CancellationToken.None);
            _unfrozenCollectedPhotoCount = 0;
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
        Status = "Finalization is not implemented yet.";
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
            PhotoViewerStatus = "Loading";
            PhotoViewerImage = null;

            var viewerImage = await _imageCache.LoadOriginalBitmapAsync(
                photo.AlbumId,
                $"{photo.PhotoId}-full{photo.FileExtension}",
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

        if (SelectedAlbumType?.Id != LocalToGoogleDriveAlbumTypeId)
        {
            Status = "Choose the album type before creating the album.";
            return;
        }

        if (!IsGoogleSignedIn)
        {
            Status = "Sign in with Google before creating the album.";
            return;
        }

        if (!IsDriveFolderSelected)
        {
            Status = "Select a Google Drive destination folder before creating the album.";
            return;
        }

        try
        {
            IsBusy = true;
            Status = "Uploading selected local photos to Google Drive...";
            Photos.Clear();
            ShareLink = "";
            DriveFolderLink = "";

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

        var manifestFileId = AlbumLinkParser.TryGetManifestFileId(OpenAlbumLink);
        if (string.IsNullOrWhiteSpace(manifestFileId))
        {
            Status = "Paste a Picshare link, Google Drive file link, or manifest file id.";
            return;
        }

        try
        {
            IsBusy = true;
            Status = "Opening album...";
            var manifest = await _albumLoader.LoadFromPublicDriveFileAsync(manifestFileId, CancellationToken.None);
            DriveFolderLink = manifest.GoogleDrive.AlbumFolderUrl;
            ShareLink = AlbumLinkParser.CreatePicshareLink(manifest.GoogleDrive.ManifestFileId, manifest.GoogleDrive.AlbumFolderId);

            if (await RequiresGoogleAuthorizationPromptAsync(manifest))
            {
                StopFeedbackSync();
                StopFlowMonitor();
                _currentManifest = null;
                _feedbackSession = null;
                _feedbackDatabase = null;
                _feedbackStatus = null;
                IsFeedbackCommitted = false;
                IsFeedbackPassed = false;
                IsCommitConfirmationVisible = false;
                CommitConfirmationMessage = "";
                IsPassConfirmationVisible = false;
                PassConfirmationMessage = "";
                IsCollectFeedbackConfirmationVisible = false;
                CollectFeedbackConfirmationMessage = "";
                CanCollectFeedback = false;
                CanStartNextRound = false;
                CanFinalizeFeedback = false;
                _unfrozenCollectedPhotoCount = 0;
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

            await LoadAlbumAsync(manifest);
            Status = $"Opened {manifest.Title} with {manifest.Photos.Count} photo(s).";
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
        IsFeedbackCommitted = false;
        IsFeedbackPassed = false;
        IsCommitConfirmationVisible = false;
        CommitConfirmationMessage = "";
        IsPassConfirmationVisible = false;
        PassConfirmationMessage = "";
        IsCollectFeedbackConfirmationVisible = false;
        CollectFeedbackConfirmationMessage = "";
        CanCollectFeedback = false;
        CanStartNextRound = false;
        CanFinalizeFeedback = false;
        _unfrozenCollectedPhotoCount = 0;
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

        if (UsesGoogleDriveBackend(manifest))
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
        var previousCategory = _selectedViewedPhoto.Category;
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

        await AdvanceFullImageViewerAfterCategoryChangeAsync(sourceCategory, changedPhoto);
    }

    partial void OnPhotoViewerImageChanging(Bitmap? value)
    {
        if (PhotoViewerImage is not null && !ReferenceEquals(PhotoViewerImage, value))
        {
            PhotoViewerImage.Dispose();
        }
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
        Status = $"Opened {manifest.Title} with {manifest.Photos.Count} photo(s).";
    }

    private async Task LoadReviewerFeedbackAsync(AlbumManifest manifest)
    {
        if (_googleTokenSet is null)
        {
            return;
        }

        var accessToken = await GetGoogleAccessTokenAsync(CancellationToken.None);
        var result = await _reviewerFeedbackService.LoadAsync(
            manifest,
            _googleTokenSet,
            accessToken,
            CancellationToken.None);

        _feedbackSession = result.Session;
        _feedbackDatabase = result.Database;
        _feedbackStatus = result.Status;
        IsFeedbackCommitted = _feedbackStatus.Status == ReviewerFeedbackStatusKind.Committed;
        IsFeedbackPassed = _feedbackStatus.Status == ReviewerFeedbackStatusKind.Passed;
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

    private void ApplyFeedbackDatabaseToPhotos()
    {
        foreach (var photo in Photos)
        {
            photo.Category = _feedbackDatabase is not null &&
                _feedbackDatabase.PhotoCategories.TryGetValue(photo.PhotoId, out var category)
                    ? category
                    : "";
            photo.IsFrozen = _feedbackDatabase?.FrozenPhotoIds.Contains(photo.PhotoId) == true;
        }

        _unfrozenCollectedPhotoCount = _feedbackDatabase?.HasCollectedFeedback == true
            ? Photos.Count(photo => !photo.IsFrozen)
            : 0;

        RebuildCategoryRows();
    }

    private void ApplyFeedbackStatus()
    {
        IsFeedbackCommitted = _feedbackStatus?.Status == ReviewerFeedbackStatusKind.Committed;
        IsFeedbackPassed = _feedbackStatus?.Status == ReviewerFeedbackStatusKind.Passed;
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
        InProgressReviewers.Clear();
        OnPropertyChanged(nameof(CommittedReviewersHeader));
        OnPropertyChanged(nameof(PassedReviewersHeader));
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
        if (_feedbackSession is null || _feedbackDatabase is null || _googleTokenSet is null)
        {
            return;
        }

        if (!await _feedbackSyncGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            var accessToken = await GetGoogleAccessTokenAsync(cancellationToken);
            var result = await _reviewerFeedbackService.SyncAsync(
                _feedbackSession,
                _feedbackDatabase,
                accessToken,
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
        if (_currentManifest is null || _googleTokenSet is null || !IsAuthorFlowVisible)
        {
            return;
        }

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => FlowStatus = "Refreshing");
            var accessToken = await GetGoogleAccessTokenAsync(cancellationToken);
            var flow = await _reviewerFeedbackService.LoadFeedbackFlowAsync(
                _currentManifest.GoogleDrive.FeedbackFolderId,
                accessToken,
                cancellationToken);

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

                foreach (var reviewer in flow.InProgress)
                {
                    InProgressReviewers.Add(new ReviewerFeedbackFlowItemViewModel(reviewer));
                }

                OnPropertyChanged(nameof(CommittedReviewersHeader));
                OnPropertyChanged(nameof(PassedReviewersHeader));
                OnPropertyChanged(nameof(InProgressReviewersHeader));
                FlowStatus = flow.Committed.Count == 0 && flow.Passed.Count == 0 && flow.InProgress.Count == 0
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
    }

    private void UpdateFeedbackControlState()
    {
        var hasTerminalFeedbackState = IsFeedbackCommitted || IsFeedbackPassed;
        CanModifyFeedback = _feedbackSession is not null && !hasTerminalFeedbackState;
        CanPassFeedback = _feedbackSession is not null && !hasTerminalFeedbackState;
        CanCollectFeedback = IsAuthorFlowVisible;
        CanFinalizeFeedback = IsAuthorFlowVisible;
        CanStartNextRound = IsAuthorFlowVisible && _unfrozenCollectedPhotoCount > 0;
        if (_feedbackSession is null)
        {
            CanCommitFeedback = false;
            CanPassFeedback = false;
            CommitFeedbackStatus = "";
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

        var uncategorizedCount = Photos.Count(photo => !photo.IsFrozen && string.IsNullOrWhiteSpace(photo.Category));
        var niceCount = Photos.Count(photo => string.Equals(photo.Category, "nice", StringComparison.Ordinal));
        var targetNiceCount = _currentManifest?.TargetNicePhotoCount ?? Math.Max(0, TargetNicePhotoCount);

        if (uncategorizedCount > 0)
        {
            CanCommitFeedback = false;
            CommitFeedbackStatus = $"{uncategorizedCount} uncategorized pictures left";
            return;
        }

        if (niceCount != targetNiceCount)
        {
            CanCommitFeedback = false;
            CommitFeedbackStatus = $"expecting {targetNiceCount} nice pictures";
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

    private async Task AdvanceFullImageViewerAfterCategoryChangeAsync(string sourceCategory, AlbumPhotoViewModel changedPhoto)
    {
        var sourcePhotos = GetPhotosForCategory(sourceCategory).ToList();
        if (sourcePhotos.Count == 0)
        {
            ClosePhotoViewer();
            return;
        }

        var changedIndex = sourcePhotos.FindIndex(photo => ReferenceEquals(photo, changedPhoto));
        var nextIndex = changedIndex >= 0 && changedIndex < sourcePhotos.Count
            ? changedIndex % sourcePhotos.Count
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
        return string.Equals(manifest.DatabaseBackendType, "google-drive-folder", StringComparison.OrdinalIgnoreCase) ||
               manifest.Photos.Any(photo => string.Equals(photo.BackendType, "google-drive-file", StringComparison.OrdinalIgnoreCase));
    }

    private bool IsCurrentReviewerAuthor(AlbumManifest manifest)
    {
        if (_googleTokenSet?.UserId is not { Length: > 0 } reviewerUserId)
        {
            return false;
        }

        return string.Equals(manifest.Author.BackendType, "google", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(manifest.Author.UserId, reviewerUserId, StringComparison.Ordinal);
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

    partial void OnSelectedAlbumTypeChanged(AlbumTypeOptionViewModel? value)
    {
        OnPropertyChanged(nameof(IsAlbumSettingsVisible));
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
