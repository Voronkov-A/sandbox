namespace Picshare.Services;

/// <summary>Provider-independent photo input and duplicate groups. Omitted photos are singletons.</summary>
public sealed record AiPhoto(string Id, byte[] JpegBytes);

public interface IAiDuplicateDetector
{
    Task<IReadOnlyList<IReadOnlyList<string>>> FindDuplicatesAsync(
        IReadOnlyList<AiPhoto> photos, string additionalPrompt, CancellationToken cancellationToken);
}

public sealed record AiDuplicateCandidate(IReadOnlyList<string> PhotoIds, string? BestPhotoId = null);

public sealed record AiDuplicateProgress(int Iteration, int Batch, int BatchCount);

public sealed record AiDuplicateOptions(int BatchSize = 50, int MaxImageDimension = 512)
{
    public void Validate()
    {
        if (BatchSize is < 2 or > 100) throw new ArgumentOutOfRangeException(nameof(BatchSize), "AI batch size must be between 2 and 100.");
        if (MaxImageDimension is < 64 or > 4096) throw new ArgumentOutOfRangeException(nameof(MaxImageDimension), "AI image size must be between 64 and 4096 pixels.");
    }
}
