using System.Net.Http.Headers;

namespace Picshare.Services;

/// <summary>Downloads Drive content through the authenticated API, including links in older albums.</summary>
public sealed class GoogleDriveMediaHandler(Func<CancellationToken, Task<string>> getAccessTokenAsync) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!TryGetMediaFileId(request.RequestUri, out var fileId))
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        request.RequestUri = new Uri(GoogleDriveRestClient.CreateMediaDownloadUrl(fileId));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await getAccessTokenAsync(cancellationToken).ConfigureAwait(false));
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.Content.Headers.ContentType?.MediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true)
        {
            response.Dispose();
            throw new InvalidOperationException("Google Drive returned an HTML page instead of file content. Check your Google sign-in and album access.");
        }
        return response;
    }

    internal static bool TryGetMediaFileId(Uri? uri, out string fileId)
    {
        fileId = "";
        if (uri is null || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2)).Where(part => part.Length == 2)
            .GroupBy(part => Uri.UnescapeDataString(part[0]), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => Uri.UnescapeDataString(group.First()[1]), StringComparer.OrdinalIgnoreCase);
        if (uri.Host.Equals("drive.google.com", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath == "/uc")
            return query.TryGetValue("id", out fileId!) && !string.IsNullOrWhiteSpace(fileId);
        const string prefix = "/drive/v3/files/";
        if (uri.Host.Equals("www.googleapis.com", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)
            && query.TryGetValue("alt", out var alt) && alt == "media")
        {
            fileId = Uri.UnescapeDataString(uri.AbsolutePath[prefix.Length..]);
            return !string.IsNullOrWhiteSpace(fileId) && !fileId.Contains('/');
        }
        return false;
    }
}
