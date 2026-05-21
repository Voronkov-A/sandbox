using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Yap.Common.Channels;

public sealed class ChannelServer
{
    private readonly ChannelWriter<CommandEnvelope> _commandWriter;
    private readonly ConcurrentDictionary<Identity, ServerSideChannel> _channels;

    public ChannelServer(ChannelWriter<CommandEnvelope> commandWriter)
    {
        _commandWriter = commandWriter;
    }

    public async Task RunAsync(IPEndPoint address, CancellationToken cancellationToken)
    {
        using var listener = new TcpListener(address);

        listener.Start();

        while (true)
        {
            var client = await listener.AcceptTcpClientAsync(cancellationToken);
            var channel = new ServerSideChannel(client);

            try
            {
                using var timeoutCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var _ = cancellationToken.Register(timeoutCancellation.Cancel);
                var helloMessage = (HelloMessage)await ReadAsync(client.GetStream(), timeoutCancellation.Token);
                channel.Identity = new Identity(helloMessage.Identity);

                if (!_channels.TryAdd(channel.Identity, channel))
                {
                    throw new InvalidOperationException("Could not add a channel.");
                }
            }
            catch
            {
                await channel.DisposeAsync();
            }
        }

        while (listener.AcceptTcpClientAsync(cancellationToken))
    }

    private sealed class HelloMessage
    {
        public required string Identity { get; init; }
    }

    private sealed class ServerSideChannel : IAsyncDisposable
    {
        private readonly ChannelWriter<CommandEnvelope> _commandWriter;
        private readonly TcpClient _client;
        private Task? _running;

        public ServerSideChannel(ChannelWriter<CommandEnvelope> commandWriter, TcpClient client)
        {
            _commandWriter = commandWriter;
            _client = client;
        }

        public Identity? Identity { get; set; }

        public async ValueTask DisposeAsync()
        {
            try
            {
                _client.Close();
            }
            catch
            {
                //
            }

            if (_running != null)
            {
                try
                {
                    await _running;
                }
                catch
                {
                    //
                }
            }

            _client.Dispose();
        }

        public void Start()
        {
            _running = Task.Run(async () => await RunAsync());
        }

        public async Task RunAsync()
        {
            var stream = _client.GetStream();

            while (true)
            {
                var message = await ReadAsync(stream, CancellationToken.None);
                var envelope = new CommandEnvelope(
                    Identity ?? throw new InvalidOperationException("Identity is null."),
                    message);
                await _commandWriter.WriteAsync(envelope, CancellationToken.None);
            }
        }
    }

    private static async Task WriteAsync(Stream output, object message, CancellationToken cancellationToken)
    {
        var typeBytes = new UTF8Encoding(false).GetBytes(message.GetType().ToString());
        var valueBytes = JsonSerializer.SerializeToUtf8Bytes(message);
        var typePrefix = BitConverter.GetBytes(typeBytes.Length);
        var valuePrefix = BitConverter.GetBytes(valueBytes.Length);

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(typePrefix);
            Array.Reverse(valuePrefix);
        }

        await output.WriteAsync(typePrefix, 0, typePrefix.Length, cancellationToken);
        await output.WriteAsync(typeBytes, 0, typeBytes.Length, cancellationToken);
        await output.WriteAsync(valuePrefix, 0, valuePrefix.Length, cancellationToken);
        await output.WriteAsync(valueBytes, 0, valueBytes.Length, cancellationToken);
    }

    private static async Task<object> ReadAsync(Stream input, CancellationToken cancellationToken)
    {
        var typePrefix = new byte[4];
        await input.ReadExactlyAsync(typePrefix, 0, typePrefix.Length, cancellationToken);

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(typePrefix);
        }

        var typeLength = BitConverter.ToInt32(typePrefix);
        var typeBytes = new byte[typeLength];
        await input.ReadExactlyAsync(typeBytes, 0, typeBytes.Length, cancellationToken);
        var type = Type.GetType(new UTF8Encoding(false).GetString(typeBytes))
            ?? throw new InvalidOperationException("Type is null.");

        var valuePrefix = new byte[4];
        await input.ReadExactlyAsync(valuePrefix, 0, valuePrefix.Length, cancellationToken);

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(valuePrefix);
        }

        var valueLength = BitConverter.ToInt32(valuePrefix);
        var valueBytes = new byte[valueLength];
        await input.ReadExactlyAsync(valueBytes, 0, valueBytes.Length, cancellationToken);
        var value = JsonSerializer.Deserialize(valueBytes, type);

        return value ?? throw new InvalidOperationException("Value is null.");
    }
}
