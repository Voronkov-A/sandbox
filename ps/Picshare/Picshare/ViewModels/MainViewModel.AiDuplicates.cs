using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Picshare.Services;

namespace Picshare.ViewModels;

public partial class MainViewModel
{
    private static readonly HttpClient AiHttpClient = AiHttpClientFactory.Create();
    private CancellationTokenSource? _aiDuplicateCancellation;
    private Guid? _aiDuplicateLongRunningOperationId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAlbumModalDialogsVisible))]
    private bool _isAiDuplicateDialogVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAlbumModalDialogsVisible))]
    [NotifyPropertyChangedFor(nameof(IsAlbumProgressDialogsVisible))]
    [NotifyPropertyChangedFor(nameof(CanFindAiDuplicates))]
    [NotifyPropertyChangedFor(nameof(CanStartAiDuplicateDetection))]
    [NotifyCanExecuteChangedFor(nameof(OpenAiDuplicateDialogCommand))]
    [NotifyCanExecuteChangedFor(nameof(FindAiDuplicatesCommand))]
    private bool _isAiDuplicateProgressVisible;

    [ObservableProperty] private string _aiDuplicatePrompt = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAiDuplicateDialogMessage))]
    private string _aiDuplicateDialogMessage = "";
    [ObservableProperty] private string _aiDuplicateProgressMessage = "";
    [ObservableProperty] private int _aiDuplicateProgressValue;
    [ObservableProperty] private int _aiDuplicateProgressMaximum = 1;

    public ObservableCollection<AiDuplicateCategoryViewModel> AiDuplicateCategories { get; } = new();
    public bool HasAiDuplicateDialogMessage => !string.IsNullOrEmpty(AiDuplicateDialogMessage);
    public bool CanFindAiDuplicates => CanModifyFeedback && !IsBusy && !IsAiDuplicateProgressVisible &&
        Photos?.Where(photo => !photo.IsFrozen).Take(2).Count() == 2;
    public bool CanStartAiDuplicateDetection => CanFindAiDuplicates && AiDuplicateCategories?.Any(category => category.IsSelected) == true;

    [RelayCommand(CanExecute = nameof(CanFindAiDuplicates))]
    private void OpenAiDuplicateDialog()
    {
        AiDuplicateCategories.Clear();
        foreach (var category in new[]
        {
            new AiDuplicateCategoryViewModel("", "Todo", true),
            new AiDuplicateCategoryViewModel("nice", "Nice", false),
            new AiDuplicateCategoryViewModel("ok", "Okay", false),
            new AiDuplicateCategoryViewModel("trash", "Trash", false)
        })
        {
            category.PropertyChanged += (_, _) => NotifyAiDuplicateAvailability();
            AiDuplicateCategories.Add(category);
        }
        AiDuplicatePrompt = "";
        AiDuplicateDialogMessage = "";
        IsAiDuplicateDialogVisible = true;
        NotifyAiDuplicateAvailability();
    }

    [RelayCommand]
    private void CancelAiDuplicateDialog() => IsAiDuplicateDialogVisible = false;

    [RelayCommand]
    private void CancelAiDuplicateDetection() => _aiDuplicateCancellation?.Cancel();

    private void NotifyAiDuplicateAvailability()
    {
        OnPropertyChanged(nameof(CanFindAiDuplicates));
        OnPropertyChanged(nameof(CanStartAiDuplicateDetection));
        OpenAiDuplicateDialogCommand.NotifyCanExecuteChanged();
        FindAiDuplicatesCommand.NotifyCanExecuteChanged();
    }

    partial void OnCanModifyFeedbackChanged(bool value) => NotifyAiDuplicateAvailability();
    partial void OnIsBusyChanged(bool value) => NotifyAiDuplicateAvailability();

    [RelayCommand(CanExecute = nameof(CanStartAiDuplicateDetection))]
    private async Task FindAiDuplicatesAsync()
    {
        if (!CanStartAiDuplicateDetection || _feedbackSession is not { } session ||
            _feedbackDatabase is not { } database || _currentManifest is not { } manifest) return;
        var categories = AiDuplicateCategories.Where(category => category.IsSelected).Select(category => category.Category).ToHashSet(StringComparer.Ordinal);
        var candidates = GetVisiblePhotos().Where(photo => categories.Contains(photo.Category))
            .Select(photo => GetDuplicateGroupMembers(photo).ToList())
            .Where(members => members.All(photo => !photo.IsFrozen))
            .Select(members => new AiDuplicateCandidate(members.Select(photo => photo.PhotoId).ToList(),
                members.FirstOrDefault(photo => photo.IsBestInDuplicateGroup)?.PhotoId)).ToList();
        if (candidates.Count < 2)
        {
            AiDuplicateDialogMessage = "Select categories containing at least two editable photos or duplicate groups.";
            return;
        }

        var snapshot = JsonSerializer.Serialize(database);
        var ids = candidates.SelectMany(candidate => candidate.PhotoIds).ToHashSet(StringComparer.Ordinal);
        // Snapshot UI-owned properties before moving image work to a worker thread.
        var sources = Photos.Where(photo => ids.Contains(photo.PhotoId)).ToDictionary(photo => photo.PhotoId,
            photo => (photo.AlbumId, CacheFileName: GetFullPhotoCacheFileName(photo), photo.DownloadUrl, photo.RotationDegrees), StringComparer.Ordinal);
        var prompt = AiDuplicatePrompt;
        using var cancellation = new CancellationTokenSource();
        _aiDuplicateCancellation = cancellation;
        IsAiDuplicateDialogVisible = false;
        IsAiDuplicateProgressVisible = true;
        AiDuplicateProgressValue = 0;
        AiDuplicateProgressMaximum = 1;
        AiDuplicateProgressMessage = "Preparing photos...";
        try
        {
            var settings = _settingsProvider.Ai;
            if (string.IsNullOrWhiteSpace(settings.ApiKey))
                throw new InvalidOperationException("Configure ai.apiKey in picshare.settings.json before finding duplicates with AI.");
            var diagnostics = new AiDuplicateDiagnostics(DiagnosticLogging.Current);
            IAiDuplicateDetector detector = new OpenAiDuplicateDetector(AiHttpClient, settings.ApiKey, settings.Model ?? "gpt-6-luna", diagnostics);
            var options = new AiDuplicateOptions(settings.BatchSize ?? 50, settings.MaxImageDimension ?? 512);
            options.Validate();
            await using var operation = await BeginLongRunningOperationAsync(LongRunningOperationKind.AiDuplicateDetection,
                "Find duplicates with AI", AiDuplicateProgressMessage, 0, 1, cancellation.Token);
            var workflow = new AiDuplicateDetectionWorkflow(detector, options, diagnostics: diagnostics);
            var result = await Task.Run(() => workflow.FindDuplicatesAsync(candidates, async (id, maxDimension, token) =>
            {
                var source = sources[id];
                using var bytes = new MemoryStream();
                await _imageCache.CopyOriginalToAsync(source.AlbumId, source.CacheFileName, source.DownloadUrl,
                    _imageHttpClient, bytes, token).ConfigureAwait(false);
                bytes.Position = 0;
                return AiPhotoImageEncoder.Encode(bytes, maxDimension, source.RotationDegrees, token);
            }, prompt, progress => Dispatcher.UIThread.Post(() =>
            {
                if (!ReferenceEquals(_aiDuplicateCancellation, cancellation) || cancellation.IsCancellationRequested) return;
                AiDuplicateProgressMaximum = progress.BatchCount;
                AiDuplicateProgressValue = progress.Batch;
                AiDuplicateProgressMessage = $"Iteration {progress.Iteration} of {AiDuplicateDetectionWorkflow.MaximumIterations}, batch {Math.Min(progress.Batch + 1, progress.BatchCount)} of {progress.BatchCount}";
                UpdateLongRunningOperation(_aiDuplicateLongRunningOperationId, LongRunningOperationKind.AiDuplicateDetection,
                    "Find duplicates with AI", AiDuplicateProgressMessage, progress.Batch, progress.BatchCount);
            }, DispatcherPriority.Background), cancellation.Token), cancellation.Token);

            cancellation.Token.ThrowIfCancellationRequested();
            await _feedbackSyncGate.WaitAsync(cancellation.Token);
            try
            {
                if (!ReferenceEquals(_currentManifest, manifest) || !ReferenceEquals(_feedbackSession, session) ||
                    !ReferenceEquals(_feedbackDatabase, database) || !CanModifyFeedback || JsonSerializer.Serialize(database) != snapshot)
                {
                    if (ReferenceEquals(_aiDuplicateCancellation, cancellation))
                        Status = "Album feedback changed during AI analysis. Run Find duplicates with AI again.";
                    return;
                }
                if (result.Count == 0)
                {
                    Status = "AI found no new duplicate groups.";
                    return;
                }

                var updated = AiDuplicateFeedbackMerger.Merge(database, result, ids);
                cancellation.Token.ThrowIfCancellationRequested();
                AiDuplicateProgressMessage = "Saving duplicate groups...";
                var undo = BeginFeedbackUndo("Find duplicates with AI", result.SelectMany(group => group));
                // Commit the complete result once. Cancellation applies before this persistence boundary.
                await _reviewerFeedbackService.RestoreLocalDatabaseAsync(session, updated, CancellationToken.None);
                // A deep link can switch albums even while the modal blocks ordinary UI input.
                if (!ReferenceEquals(_currentManifest, manifest) || !ReferenceEquals(_feedbackSession, session) ||
                    !ReferenceEquals(_feedbackDatabase, database)) return;
                _feedbackDatabase = updated;
                ClearBulkPhotoSelection();
                ApplyFeedbackDatabaseToPhotos();
                UpdateCurrentPhotoActionVisibility();
                CompleteFeedbackUndo(undo);
                Status = $"AI marked {result.Count} new duplicate group(s).";
            }
            finally { _feedbackSyncGate.Release(); }
            _ = SyncFeedbackAsync();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (ReferenceEquals(_aiDuplicateCancellation, cancellation)) Status = "AI duplicate detection cancelled.";
        }
        catch (Exception ex)
        {
            DiagnosticLogging.LogException(ex);
            if (ReferenceEquals(_aiDuplicateCancellation, cancellation)) Status = $"Could not find duplicates with AI: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_aiDuplicateCancellation, cancellation))
            {
                _aiDuplicateCancellation = null;
                IsAiDuplicateProgressVisible = false;
                ClearLongRunningOperation(LongRunningOperationKind.AiDuplicateDetection);
            }
        }
    }

    private void ClearAiDuplicateState()
    {
        _aiDuplicateCancellation?.Cancel();
        _aiDuplicateCancellation = null;
        IsAiDuplicateDialogVisible = false;
        IsAiDuplicateProgressVisible = false;
        ClearLongRunningOperation(LongRunningOperationKind.AiDuplicateDetection);
    }
}
