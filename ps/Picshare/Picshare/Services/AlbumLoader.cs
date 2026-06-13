using System.Text.Json;
using Picshare.Models;

namespace Picshare.Services;

public sealed class AlbumLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public AlbumLoader(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<AlbumManifest> LoadFromPublicDriveFileAsync(string manifestFileId, CancellationToken cancellationToken)
    {
        var url = GoogleDriveRestClient.CreatePublicDownloadUrl(manifestFileId);
        await using var stream = await _httpClient.GetStreamAsync(url, cancellationToken);
        var manifest = await JsonSerializer.DeserializeAsync<AlbumManifest>(stream, JsonOptions, cancellationToken);
        return manifest ?? throw new InvalidOperationException("The album manifest is empty or invalid.");
    }

    public async Task<AlbumManifest> LoadFromLocalFileAsync(string manifestFilePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(manifestFilePath);
        var manifest = await JsonSerializer.DeserializeAsync<AlbumManifest>(stream, JsonOptions, cancellationToken);
        return manifest ?? throw new InvalidOperationException("The album manifest is empty or invalid.");
    }
}
