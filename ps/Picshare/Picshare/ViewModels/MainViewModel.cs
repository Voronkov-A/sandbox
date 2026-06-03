using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
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
    private bool _isGoogleAuthorizationRequired;

    [ObservableProperty]
    private string _googleAuthorizationMessage = "";

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

    public ObservableCollection<AlbumPhotoRowViewModel> PhotoRows { get; } = new();

    private readonly GoogleDriveAlbumPublisher _publisher = new();
    private readonly AlbumLoader _albumLoader = new();
    private readonly LocalPhotoScanner _photoScanner = new();
    private readonly GoogleOAuthClient _oauthClient = new();
    private readonly GoogleUserInfoClient _googleUserInfoClient = new();
    private readonly GoogleOAuthTokenStore _tokenStore = new();
    private readonly PicshareSettingsProvider _settingsProvider = new();
    private readonly ImageCacheService _imageCache = new();
    private readonly HttpClient _imageHttpClient = new();
    private readonly List<IStorageFolder> _folderDateImportFolders = new();
    private readonly List<DriveFolderLocation> _driveFolderPath = new();
    private GoogleOAuthTokenSet? _googleTokenSet;
    private CancellationTokenSource? _googleSignInCancellation;
    private CancellationTokenSource? _photoViewerCancellation;
    private AlbumManifest? _pendingGoogleAuthorizationManifest;
    private AlbumPhotoViewModel? _selectedViewedPhoto;
    private string? _driveNextPageToken;

    public bool HasMoreDriveItems => !string.IsNullOrWhiteSpace(_driveNextPageToken);

    public MainViewModel()
    {
        _googleTokenSet = _tokenStore.Load();
        IsGoogleSignedIn = _googleTokenSet is not null;
        ResetCreateInputs();
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
                    ParentDriveFolderId = string.IsNullOrWhiteSpace(ParentDriveFolderId) ? null : ParentDriveFolderId.Trim(),
                    AccessToken = accessToken
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
                _pendingGoogleAuthorizationManifest = manifest;
                CurrentAlbumTitle = manifest.Title;
                Photos.Clear();
                PhotoRows.Clear();
                SelectViewedPhoto(null);
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

    private Task LoadAlbumAsync(AlbumManifest manifest)
    {
        CurrentAlbumTitle = manifest.Title;
        Photos.Clear();
        PhotoRows.Clear();
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

        foreach (var row in Photos.Chunk(PhotosPerRow))
        {
            PhotoRows.Add(new AlbumPhotoRowViewModel(row));
        }

        return Task.CompletedTask;
    }

    public async Task StartPhotoViewportLoadAsync(AlbumPhotoViewModel photo)
    {
        await photo.StartViewportLoadAsync(_imageCache, _imageHttpClient);
    }

    public void StopPhotoViewportLoad(AlbumPhotoViewModel photo)
    {
        photo.StopViewportLoad();
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

    private void SelectViewedPhoto(AlbumPhotoViewModel? photo)
    {
        if (_selectedViewedPhoto is not null)
        {
            _selectedViewedPhoto.IsSelectedForViewing = false;
        }

        _selectedViewedPhoto = photo;

        if (_selectedViewedPhoto is not null)
        {
            _selectedViewedPhoto.IsSelectedForViewing = true;
        }
    }

    private static bool UsesGoogleDriveBackend(AlbumManifest manifest)
    {
        return string.Equals(manifest.DatabaseBackendType, "google-drive-folder", StringComparison.OrdinalIgnoreCase) ||
               manifest.Photos.Any(photo => string.Equals(photo.BackendType, "google-drive-file", StringComparison.OrdinalIgnoreCase));
    }

    partial void OnSelectedAlbumTypeChanged(AlbumTypeOptionViewModel? value)
    {
        OnPropertyChanged(nameof(IsAlbumSettingsVisible));
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
