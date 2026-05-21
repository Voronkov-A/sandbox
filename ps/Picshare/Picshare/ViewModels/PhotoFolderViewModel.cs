using Avalonia.Platform.Storage;

namespace Picshare.ViewModels;

public sealed class PhotoFolderViewModel
{
    public PhotoFolderViewModel(string displayName, string? localPath, IStorageFolder? storageFolder)
    {
        DisplayName = displayName;
        LocalPath = localPath;
        StorageFolder = storageFolder;
    }

    public string DisplayName { get; }

    public string? LocalPath { get; }

    public IStorageFolder? StorageFolder { get; }
}
