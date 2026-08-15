using System.Buffers;

namespace Picshare.Services;

internal sealed class PooledByteBuffer : IDisposable
{
    private byte[]? _array;

    private PooledByteBuffer(byte[] array, int length)
    {
        _array = array;
        Length = length;
    }

    public byte[] Array => _array ?? throw new ObjectDisposedException(nameof(PooledByteBuffer));

    public int Length { get; }

    public Memory<byte> Memory => Array.AsMemory(0, Length);

    public ReadOnlyMemory<byte> ReadOnlyMemory => Array.AsMemory(0, Length);

    public static PooledByteBuffer Rent(int length)
    {
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        return new PooledByteBuffer(ArrayPool<byte>.Shared.Rent(length), length);
    }

    public static async Task<PooledByteBuffer> FromStreamAsync(Stream source, CancellationToken cancellationToken)
    {
        await using var memory = PooledMemoryStreamFactory.GetStream("PooledByteBuffer.FromStreamAsync");
        await source.CopyToAsync(memory, cancellationToken);
        var length = checked((int)memory.Length);
        var buffer = Rent(length);
        memory.Position = 0;
        await memory.ReadExactlyAsync(buffer.Memory, cancellationToken);
        return buffer;
    }

    public static async Task<PooledByteBuffer> FromFileAsync(string path, CancellationToken cancellationToken)
    {
        var length = checked((int)new FileInfo(path).Length);
        var buffer = Rent(length);
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            await stream.ReadExactlyAsync(buffer.Memory, cancellationToken);
            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        var array = Interlocked.Exchange(ref _array, null);
        if (array is not null)
        {
            ArrayPool<byte>.Shared.Return(array);
        }
    }
}
