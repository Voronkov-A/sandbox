using CommunityToolkit.Mvvm.Input;

namespace Picshare.ViewModels;

public sealed class RecentPhotoViewModel
{
    public RecentPhotoViewModel(
        string key,
        string photoId,
        string duplicateGroupId,
        string displayName,
        Func<RecentPhotoViewModel, Task> openAsync)
    {
        Key = key;
        PhotoId = photoId;
        DuplicateGroupId = duplicateGroupId;
        DisplayName = displayName;
        OpenCommand = new AsyncRelayCommand(() => openAsync(this));
    }

    public string Key { get; }

    public string PhotoId { get; }

    public string DuplicateGroupId { get; }

    public string DisplayName { get; }

    public IAsyncRelayCommand OpenCommand { get; }
}
