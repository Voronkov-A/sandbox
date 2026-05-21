using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Picshare.Services;

public sealed class GoogleDriveRestClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public GoogleDriveRestClient(string accessToken, HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public async Task<DriveFileInfo> CreateFolderAsync(string name, string? parentFolderId, CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["mimeType"] = "application/vnd.google-apps.folder"
        };

        if (!string.IsNullOrWhiteSpace(parentFolderId))
        {
            metadata["parents"] = new[] { parentFolderId };
        }

        using var request = CreateJsonRequest(HttpMethod.Post, "https://www.googleapis.com/drive/v3/files?fields=id,name,webViewLink", metadata);
        return await SendForDriveFileAsync(request, cancellationToken);
    }

    public async Task<DriveFileInfo> UploadFileAsync(
        string name,
        string parentFolderId,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var metadata = new
        {
            name,
            parents = new[] { parentFolderId }
        };

        using var multipart = new MultipartContent("related");
        multipart.Add(new StringContent(JsonSerializer.Serialize(metadata, JsonOptions), Encoding.UTF8, "application/json"));

        var fileContent = new StreamContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        multipart.Add(fileContent);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id,name,webViewLink,webContentLink")
        {
            Content = multipart
        };

        return await SendForDriveFileAsync(request, cancellationToken);
    }

    public async Task ShareWithAnyoneAsync(string fileId, string role, CancellationToken cancellationToken)
    {
        var permission = new
        {
            type = "anyone",
            role,
            allowFileDiscovery = false
        };

        using var request = CreateJsonRequest(
            HttpMethod.Post,
            $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(fileId)}/permissions",
            permission);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<DriveItemPage> ListChildrenAsync(
        string parentFolderId,
        string? pageToken,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = $"'{parentFolderId}' in parents and trashed = false";
        var url =
            "https://www.googleapis.com/drive/v3/files" +
            $"?q={Uri.EscapeDataString(query)}" +
            "&fields=nextPageToken,files(id,name,mimeType,capabilities/canAddChildren)" +
            "&orderBy=name" +
            $"&pageSize={pageSize}" +
            "&supportsAllDrives=true" +
            "&includeItemsFromAllDrives=true";

        if (!string.IsNullOrWhiteSpace(pageToken))
        {
            url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<DriveItemPage>(stream, JsonOptions, cancellationToken) ?? new DriveItemPage();
    }

    public async Task UpdateFileContentAsync(
        string fileId,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"https://www.googleapis.com/upload/drive/v3/files/{Uri.EscapeDataString(fileId)}?uploadType=media")
        {
            Content = new StreamContent(content)
        };

        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public static string CreatePublicDownloadUrl(string fileId)
    {
        return $"https://drive.google.com/uc?export=download&id={Uri.EscapeDataString(fileId)}";
    }

    private static HttpRequestMessage CreateJsonRequest(HttpMethod method, string requestUri, object body)
    {
        return new HttpRequestMessage(method, requestUri)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
        };
    }

    private async Task<DriveFileInfo> SendForDriveFileAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var file = await JsonSerializer.DeserializeAsync<DriveFileInfo>(stream, JsonOptions, cancellationToken);
        return file ?? throw new InvalidOperationException("Google Drive returned an empty file response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Google Drive request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
    }
}

public sealed record DriveFileInfo
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? WebViewLink { get; init; }

    public string? WebContentLink { get; init; }
}

public sealed record DriveItemPage
{
    public string? NextPageToken { get; init; }

    public IReadOnlyList<DriveItemInfo> Files { get; init; } = Array.Empty<DriveItemInfo>();
}

public sealed record DriveItemInfo
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string MimeType { get; init; }

    public DriveItemCapabilities? Capabilities { get; init; }
}

public sealed record DriveItemCapabilities
{
    public bool CanAddChildren { get; init; }
}
