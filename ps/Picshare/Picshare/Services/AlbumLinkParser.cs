namespace Picshare.Services;

public static class AlbumLinkParser
{
    public static string? TryGetManifestFileId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            var query = ParseQuery(uri.Query);
            var manifest = GetQueryValue(query, "manifest") ?? GetQueryValue(query, "manifestFileId");
            if (!string.IsNullOrWhiteSpace(manifest))
            {
                return manifest;
            }

            var id = GetQueryValue(query, "id");
            if (!string.IsNullOrWhiteSpace(id))
            {
                return id;
            }

            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var fileSegmentIndex = Array.IndexOf(segments, "d");
            if (fileSegmentIndex >= 0 && fileSegmentIndex + 1 < segments.Length)
            {
                return segments[fileSegmentIndex + 1];
            }
        }

        return value.Contains(' ', StringComparison.Ordinal) ? null : value;
    }

    public static string CreatePicshareLink(string manifestFileId, string albumFolderId)
    {
        return $"picshare://album?manifest={Uri.EscapeDataString(manifestFileId)}&folder={Uri.EscapeDataString(albumFolderId)}";
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        if (query.StartsWith("?", StringComparison.Ordinal))
        {
            query = query[1..];
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = part.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex < 0)
            {
                values[Uri.UnescapeDataString(part)] = "";
                continue;
            }

            var key = Uri.UnescapeDataString(part[..separatorIndex]);
            var value = Uri.UnescapeDataString(part[(separatorIndex + 1)..]);
            values[key] = value;
        }

        return values;
    }

    private static string? GetQueryValue(Dictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }
}
