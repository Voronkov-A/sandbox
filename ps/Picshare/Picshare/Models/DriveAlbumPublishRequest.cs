namespace Picshare.Models;

public sealed record DriveAlbumPublishRequest
{
    public required string Title { get; init; }

    public required IReadOnlyList<PhotoUploadSource> Photos { get; init; }

    public string? ParentDriveFolderId { get; init; }

    public required string AccessToken { get; init; }
}

public sealed record DriveAlbumPublishResult
{
    public required AlbumManifest Manifest { get; init; }

    public required string AlbumFolderUrl { get; init; }

    public required string PicshareLink { get; init; }
}
