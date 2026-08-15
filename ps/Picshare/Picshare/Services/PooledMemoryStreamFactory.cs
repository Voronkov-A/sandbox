using Microsoft.IO;

namespace Picshare.Services;

internal static class PooledMemoryStreamFactory
{
    private static readonly RecyclableMemoryStreamManager Manager = new();

    public static MemoryStream GetStream(string tag)
    {
        return Manager.GetStream(tag);
    }
}
