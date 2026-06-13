namespace Picshare.Models;

public sealed record LocalAlbumPublishRequest
{
    public required string Title { get; init; }

    public required IReadOnlyList<PhotoUploadSource> Photos { get; init; }

    public required int TargetNicePhotoCount { get; init; }

    public required string ParentFolderPath { get; init; }

    public required FeedbackReviewerIdentity Author { get; init; }
}

public sealed record LocalAlbumPublishResult
{
    public required AlbumManifest Manifest { get; init; }

    public required string AlbumFolderPath { get; init; }

    public required string PicshareLink { get; init; }
}
