namespace Picshare.Models;

public sealed record PhotoUploadSource(
    string FileName,
    string SortKey,
    Func<Task<Stream>> OpenReadAsync,
    string? LocalPath = null);
