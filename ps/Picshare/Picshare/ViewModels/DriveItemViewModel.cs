using Picshare.Services;

namespace Picshare.ViewModels;

public sealed class DriveItemViewModel
{
    private const string FolderMimeType = "application/vnd.google-apps.folder";

    public DriveItemViewModel(DriveItemInfo item)
    {
        Id = item.Id;
        Name = item.Name;
        MimeType = item.MimeType;
        CanAddChildren = item.Capabilities?.CanAddChildren ?? false;
    }

    public DriveItemViewModel(string id, string name, string mimeType, bool canAddChildren)
    {
        Id = id;
        Name = name;
        MimeType = mimeType;
        CanAddChildren = canAddChildren;
    }

    public string Id { get; }

    public string Name { get; }

    public string MimeType { get; }

    public bool IsFolder => MimeType == FolderMimeType;

    public bool CanAddChildren { get; }

    public bool CanOpen => IsFolder;

    public string Icon => IsFolder ? "Folder" : "File";

    public string Description => IsFolder
        ? CanAddChildren ? "Folder" : "Folder, read-only"
        : "File";
}

public sealed record DriveFolderLocation(string Id, string Name, bool CanAddChildren);
