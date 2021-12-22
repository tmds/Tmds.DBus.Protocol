using System.IO.Pipelines ;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace Tmds.DBus.Protocol;

#pragma warning disable VSTHRD100 // Avoid "async void" methods

public class MessageStream : IMessageStream // TODO: make internal.
{
    private static readonly ReadOnlyMemory<byte> OneByteArray = new[] { (byte)0 };
    private static readonly Exception s_disposedSentinel = new ObjectDisposedException(typeof(MessageStream).FullName);
    private readonly Socket _socket;
    private readonly UnixFdCollection? _fdCollection;
    private bool _supportsFdPassing;

    // Messages going out.
    private readonly ChannelReader<Message> _messageReader;
    private readonly ChannelWriter<Message> _messageWriter;

    // Bytes coming in.
    private readonly PipeWriter _pipeWriter;
    private readonly PipeReader _pipeReader;

    private Exception? _completionException;

    public static async ValueTask<IMessageStream> ConnectAsync(string address, string? userId, bool supportsFdPassing, CancellationToken cancellationToken)
    {
        if (address is null)
        {
            throw new ArgumentNullException(address);
        }

        Exception? firstException = null;
        AddressParser.AddressEntry addr = default;
        while (AddressParser.TryGetNextEntry(address, ref addr))
        {
            Socket? socket = null;
            EndPoint? endpoint = null;
            Guid guid = default;
            if (AddressParser.IsType(addr, "unix"))
            {
                AddressParser.ParseUnixProperties(addr, out string path, out guid);
                socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                endpoint = new UnixDomainSocketEndPoint(path);
            }
            else if (AddressParser.IsType(addr, "tcp"))
            {
                AddressParser.ParseTcpProperties(addr, out string host, out int? port, out guid);
                if (!port.HasValue)
                {
                    throw new ArgumentException("port");
                }
                socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                endpoint = new DnsEndPoint(host, port.Value);
            }
            if (socket is null)
            {
                continue;
            }
            try
            {
                await socket.ConnectAsync(endpoint!, cancellationToken).ConfigureAwait(false);
                MessageStream transport = new MessageStream(socket);
                transport.ReadFromSocketIntoPipe();
                await transport.DoClientAuthAsync(guid, userId, supportsFdPassing).ConfigureAwait(false);
                transport.ReadMessagesIntoSocket();
                return transport;
            }
            catch (Exception exception)
            {
                socket.Dispose();
                firstException ??= exception;
            }
        }

        if (firstException != null)
        {
            throw firstException;
        }

        throw new ArgumentException("No addresses were found", nameof(address));
    }

    private MessageStream(Socket socket)
    {
        _socket = socket;
        Channel<Message> channel = Channel.CreateUnbounded<Message>(); // TODO: review options.
        _messageReader = channel.Reader;
        _messageWriter = channel.Writer;
        var pipe = new Pipe(); // TODO: review options.
        _pipeReader = pipe.Reader;
        _pipeWriter = pipe.Writer;
        if (_supportsFdPassing)
        {
            _fdCollection = new();
        }
    }

    private async void ReadFromSocketIntoPipe()
    {
        var writer = _pipeWriter;
        Exception? exception = null;
        try
        {
            while (true)
            {
                Memory<byte> memory = writer.GetMemory(1024);
                int bytesRead = await _socket.ReceiveAsync(memory, _fdCollection);
                if (bytesRead == 0)
                {
                    throw new IOException("Connection closed by peer");
                }
                writer.Advance(bytesRead);

                await writer.FlushAsync();
            }
        }
        catch (Exception e)
        {
            exception = e;
        }
        writer.Complete(exception);
    }

    private async void ReadMessagesIntoSocket()
    {
        while (true)
        {
            if (!await _messageReader.WaitToReadAsync())
            {
                // No more messages will be coming.
                return;
            }
            var message = await _messageReader.ReadAsync();
            try
            {
                IReadOnlyList<SafeHandle>? handles = _supportsFdPassing ? message.Handles : null;
                var buffer = message.AsReadOnlySequence();
                if (buffer.IsSingleSegment)
                {
                    await _socket.SendAsync(buffer.First, handles);
                }
                else
                {
                    SequencePosition position = buffer.Start;
                    while (buffer.TryGet(ref position, out ReadOnlyMemory<byte> memory))
                    {
                        await _socket.SendAsync(buffer.First, handles);
                        handles = null;
                    }
                }
            }
            catch (Exception exception)
            {
                Complete(exception);
                return;
            }
            finally
            {
                message.ReturnToPool();
            }
        }
    }

    public async void ReceiveMessages<T>(IMessageStream.MessageReceivedHandler<T> handler, T state)
    {
        var reader = _pipeReader;
        try
        {
            while (true)
            {
                ReadResult result = await reader.ReadAsync();
                ReadOnlySequence<byte> buffer = result.Buffer;

                ReadMessages(ref buffer, _fdCollection, handler, state);

                reader.AdvanceTo(buffer.Start, buffer.End);
            }
        }
        catch (Exception exception)
        {
            exception = Complete(exception);
            OnException(exception, handler, state);
        }
        finally
        {
            _fdCollection?.Dispose();
        }

        static void ReadMessages<TState>(ref ReadOnlySequence<byte> buffer, UnixFdCollection? fdCollection, IMessageStream.MessageReceivedHandler<TState> handler, TState state)
        {
            while (MessageReader.TryReadMessage(ref buffer, out MessageReader reader, fdCollection))
            {
                handler(exception: null, ref reader, state);
            }
        }

        static void OnException(Exception exception, IMessageStream.MessageReceivedHandler<T> handler, T state)
        {
            MessageReader reader = default;
            handler(exception, ref reader, state);
        }
    }

    private struct AuthenticationResult
    {
        public bool IsAuthenticated;
        public bool SupportsFdPassing;
        public Guid Guid;
    }

    private async ValueTask DoClientAuthAsync(Guid guid, string? userId, bool supportsFdPassing)
    {
        // send 1 byte
        await _socket.SendAsync(OneByteArray, SocketFlags.None).ConfigureAwait(false);
        // auth
        var authenticationResult = await SendAuthCommandsAsync(userId, supportsFdPassing).ConfigureAwait(false);
        _supportsFdPassing = authenticationResult.SupportsFdPassing;
        if (guid != Guid.Empty)
        {
            if (guid != authenticationResult.Guid)
            {
                throw new ConnectException("Authentication failure: Unexpected GUID");
            }
        }
    }

    private async ValueTask<AuthenticationResult> SendAuthCommandsAsync(string? userId, bool supportsFdPassing)
    {
        AuthenticationResult result;
        if (userId != null)
        {
            const string AuthExternal = "AUTH EXTERNAL ";
            string command = string.Create<string>(
                length: AuthExternal.Length + userId.Length * 2 + 2, userId,
                static (Span<char> span, string userId) =>
                {
                    AuthExternal.AsSpan().CopyTo(span);
                    span = span.Slice(AuthExternal.Length);

                    const string hexchars = "0123456789abcdef";
                    for (int i = 0; i < userId.Length; i++)
                    {
                        byte b = (byte)userId[i];
                        span[i * 2] = hexchars[(int)(b >> 4)];
                        span[i * 2 + 1] = hexchars[(int)(b & 0xF)];
                    }
                    span = span.Slice(userId.Length * 2);

                    span[0] = '\r';
                    span[1] = '\n';
                });

            result = await SendAuthCommandAsync(command, supportsFdPassing).ConfigureAwait(false);

            if (result.IsAuthenticated)
            {
                return result;
            }
        }

        result = await SendAuthCommandAsync("AUTH ANONYMOUS\r\n", supportsFdPassing).ConfigureAwait(false);
        if (result.IsAuthenticated)
        {
            return result;
        }

        throw new ConnectException("Authentication failure");
    }

    private async ValueTask<AuthenticationResult> SendAuthCommandAsync(string command, bool supportsFdPassing)
    {
        byte[] lineBuffer = ArrayPool<byte>.Shared.Rent(512);
        try
        {
            AuthenticationResult result = default(AuthenticationResult);
            await WriteAsync(command, lineBuffer).ConfigureAwait(false);
            int lineLength = await ReadLineAsync(lineBuffer).ConfigureAwait(false);

            if (StartsWithAscii(lineBuffer, lineLength, "OK"))
            {
                result.IsAuthenticated = true;
                result.Guid = ParseGuid(lineBuffer, lineLength);

                if (supportsFdPassing)
                {
                    await WriteAsync("NEGOTIATE_UNIX_FD\r\n", lineBuffer).ConfigureAwait(false);

                    lineLength = await ReadLineAsync(lineBuffer).ConfigureAwait(false);

                    result.SupportsFdPassing = StartsWithAscii(lineBuffer, lineLength, "AGREE_UNIX_FD");
                }

                await WriteAsync("BEGIN\r\n", lineBuffer).ConfigureAwait(false);
                return result;
            }
            else if (StartsWithAscii(lineBuffer, lineLength, "REJECTED"))
            {
                return result;
            }
            else
            {
                await WriteAsync("ERROR\r\n", lineBuffer).ConfigureAwait(false);
                return result;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(lineBuffer);
        }

        static bool StartsWithAscii(byte[] line, int length, string expected)
        {
            if (length < expected.Length)
            {
                return false;
            }
            for (int i = 0; i < expected.Length; i++)
            {
                if (line[i] != expected[i])
                {
                    return false;
                }
            }
            return true;
        }

        static Guid ParseGuid(byte[] line, int length)
        {
            Span<byte> span = new Span<byte>(line, 0, length);
            int spaceIndex = span.IndexOf((byte)' ');
            if (spaceIndex == -1)
            {
                return Guid.Empty;
            }
            span = span.Slice(spaceIndex + 1);
            spaceIndex = span.IndexOf((byte)' ');
            if (spaceIndex != -1)
            {
                span = span.Slice(0, spaceIndex);
            }
            Span<char> charBuffer = stackalloc char[span.Length]; // TODO: check length
            for (int i = 0; i < span.Length; i++)
            {
                // TODO: validate char
                charBuffer[i] = (char)span[i];
            }
            return Guid.ParseExact(charBuffer, "N");
        }
    }

    private async ValueTask WriteAsync(string message, Memory<byte> lineBuffer)
    {
        int length = Encoding.ASCII.GetBytes(message.AsSpan(), lineBuffer.Span);
        lineBuffer = lineBuffer.Slice(0, length);
        await _socket.SendAsync(lineBuffer, SocketFlags.None);
    }

    private async ValueTask<int> ReadLineAsync(Memory<byte> lineBuffer)
    {
        var reader = _pipeReader;
        while (true)
        {
            ReadResult result = await reader.ReadAsync();
            ReadOnlySequence<byte> buffer = result.Buffer;

            // TODO: check length.

            SequencePosition? position = buffer.PositionOf((byte)'\n');

            if (!position.HasValue)
            {
                reader.AdvanceTo(buffer.Start, buffer.End);
                continue;
            }

            int length = CopyBuffer(buffer.Slice(0, position.Value), lineBuffer);
            reader.AdvanceTo(buffer.GetPosition(1, position.Value));
            return length;
        }

        int CopyBuffer(ReadOnlySequence<byte> src, Memory<byte> dst)
        {
            Span<byte> span = dst.Span;
            src.CopyTo(span);
            span = span.Slice(0, (int)src.Length);
            if (!span.EndsWith((ReadOnlySpan<byte>) new byte [] { (byte)'\r' }))
            {
                throw new ProtocolException("Authentication messages from server must end with '\\r\\n'.");
            }
            if (span.Length == 1)
            {
                throw new ProtocolException("Received empty authentication message from server.");
            }
            return span.Length - 1;
        }
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _completionException) == null)
        {
            Complete(s_disposedSentinel);
        }
    }

    public async ValueTask SendMessageAsync(Message message)
    {
        while (await _messageWriter.WaitToWriteAsync().ConfigureAwait(false))
        {
            if (_messageWriter.TryWrite(message))
                return;
        }

        message.ReturnToPool();

        Exception completionException = Volatile.Read(ref _completionException)!;
        if (completionException == s_disposedSentinel)
        {
            throw new ObjectDisposedException(typeof(MessageStream).FullName);
        }
        else
        {
            throw new DisconnectedException(completionException);
        }
    }

    private Exception Complete(Exception exception)
    {
        Exception? previous = Interlocked.CompareExchange(ref _completionException, exception, null);
        if (previous == null)
        {
            _socket?.Dispose();
            _messageWriter.Complete();
        }
        return previous ?? exception;
    }
}
