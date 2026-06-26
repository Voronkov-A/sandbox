namespace Picshare.Models;

public sealed record DriveAlbumPublishRequest
{
    public required string Title { get; init; }

    public required IReadOnlyList<PhotoUploadSource> Photos { get; init; }

    public required int TargetNicePhotoCount { get; init; }

    public string? ParentDriveFolderId { get; init; }

    public GoogleDriveAlbumShareSettings ShareSettings { get; init; } = GoogleDriveAlbumShareSettings.Public;

    public required string AccessToken { get; init; }

    public required FeedbackReviewerIdentity Author { get; init; }
}

public sealed record GoogleDriveAlbumShareSettings
{
    public static GoogleDriveAlbumShareSettings Public { get; } = new()
    {
        IsPublic = true,
        UserEmailAddresses = Array.Empty<string>()
    };

    public bool IsPublic { get; init; } = true;

    public IReadOnlyList<string> UserEmailAddresses { get; init; } = Array.Empty<string>();
}

public sealed record DriveAlbumPublishResult
{
    public required AlbumManifest Manifest { get; init; }

    public required string AlbumFolderUrl { get; init; }

    public required string PicshareLink { get; init; }
}
