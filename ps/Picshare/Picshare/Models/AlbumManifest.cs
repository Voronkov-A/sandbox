namespace Picshare.Models;

public sealed record AlbumManifest
{
    public int Version { get; init; } = 1;

    public required string AlbumId { get; init; }

    public required string Title { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required string PhotoBackendType { get; init; }

    public required string DatabaseBackendType { get; init; }

    public required GoogleDriveAlbumDetails GoogleDrive { get; init; }

    public required IReadOnlyList<PhotoReference> Photos { get; init; }
}

public sealed record GoogleDriveAlbumDetails
{
    public required string AlbumFolderId { get; init; }

    public required string PhotosFolderId { get; init; }

    public required string FeedbackFolderId { get; init; }

    public required string ManifestFileId { get; init; }

    public required string AlbumFolderUrl { get; init; }
}

public sealed record PhotoReference
{
    public required string Id { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required string BackendType { get; init; }

    public required string DriveFileId { get; init; }

    public required string DownloadUrl { get; init; }

    public required string ThumbnailDriveFileId { get; init; }

    public required string ThumbnailDownloadUrl { get; init; }

    public required string ThumbnailContentType { get; init; }
}
