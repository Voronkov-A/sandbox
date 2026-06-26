namespace Picshare.ViewModels;

public sealed class RecentPhotoViewModel
{
    public RecentPhotoViewModel(
        string key,
        string photoId,
        string duplicateGroupId,
        string displayName)
    {
        Key = key;
        PhotoId = photoId;
        DuplicateGroupId = duplicateGroupId;
        DisplayName = displayName;
    }

    public string Key { get; }

    public string PhotoId { get; }

    public string DuplicateGroupId { get; }

    public string DisplayName { get; }
}
