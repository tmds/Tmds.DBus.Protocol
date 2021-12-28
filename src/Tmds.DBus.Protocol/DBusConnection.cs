using System.Net;
using System.Net.Sockets;

namespace Tmds.DBus.Protocol;

class DBusConnection : IDisposable
{
    private static readonly Exception s_disposedSentinel = new ObjectDisposedException(typeof(Connection).FullName);

    enum ConnectionState
    {
        Created,
        Connecting,
        Connected,
        Disconnected
    }

    private readonly object _gate = new object();
    private readonly Connection _parentConnection;
    private readonly Dictionary<uint, (MethodReturnHandler, object)>? _methodReturnHandlers;
    private IMessageStream? _messageStream;
    private ConnectionState _state;
    private Exception? _disconnectReason;
    private uint _nextSerial;
    private string? _localName;

    public Exception DisconnectReason
    {
        get => _disconnectReason ?? new ObjectDisposedException(GetType().FullName);
        set => Interlocked.CompareExchange(ref _disconnectReason, value, null);
    }

    public DBusConnection(Connection parent)
    {
        _parentConnection = parent;
        _methodReturnHandlers = new();
    }

    public async ValueTask ConnectAsync(string address, string? userId, bool supportsFdPassing, CancellationToken cancellationToken)
    {
        _state = ConnectionState.Connecting;
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

                MessageStream stream;
                lock (_gate)
                {
                    if (_state != ConnectionState.Connecting)
                    {
                        throw new DisconnectedException(DisconnectReason);
                    }
                    _messageStream = stream = new MessageStream(socket);
                }

                await stream.DoClientAuthAsync(guid, userId, supportsFdPassing).ConfigureAwait(false);

                stream.ReceiveMessages(
                    (Exception? exception, ref MessageReader reader, DBusConnection connection) =>
                        connection.HandleMessages(exception, ref reader), this);

                lock (_gate)
                {
                    if (_state != ConnectionState.Connecting)
                    {
                        throw new DisconnectedException(DisconnectReason);
                    }
                    _state = ConnectionState.Connected;
                }

                _localName = await GetLocalNameAsync();

                return;
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

    private async Task<string?> GetLocalNameAsync()
    {
        TaskCompletionSource<string?> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        await CallMethodAsync(
            message: CreateHelloMessage(),
            (Exception? exception, ref MessageReader reader, object state) =>
            {
                var tcsState = (TaskCompletionSource<string?>)state;

                if (exception is not null)
                {
                    tcsState.SetException(exception);
                }
                else if (reader.Type == MessageType.MethodReturn)
                {
                    tcsState.SetResult(reader.ReadStringAsString());
                }
                else
                {
                    tcsState.SetResult(null);
                }
            }, tcs);

        return await tcs.Task;
    }

    private static Message CreateHelloMessage()
    {
        var rented = MessagePool.Shared.Rent();
        var message = rented.Message;

        MessageWriter writer = message.GetWriter();

        writer.WriteMethodCallHeader(
            destination: "org.freedesktop.DBus",
            path: "/org/freedesktop/DBus",
            @interface: "org.freedesktop.DBus",
            member: "Hello");

        writer.Flush();

        return message;
    }

    private void HandleMessages(Exception? exception, ref MessageReader reader)
    {
        if (exception != null)
        {
            _parentConnection.Disconnect(exception, this);
        }
        else
        {
            if (reader.ReplySerial.HasValue)
            {
                (MethodReturnHandler handler, object state) value;
                lock (_gate)
                {
                    if (!_methodReturnHandlers!.Remove(reader.ReplySerial.Value, out value))
                    {
                        return;
                    }
                }
                value.handler(null, ref reader, value.state);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_state == ConnectionState.Disconnected)
            {
                return;
            }
            _state = ConnectionState.Disconnected;
        }

        Exception disconnectReason = DisconnectReason;

        _messageStream?.Close(disconnectReason);

        if (_methodReturnHandlers != null)
        {
            MessageReader reader = default;
            foreach (var handler in _methodReturnHandlers)
            {
                handler.Value.Item1.Invoke(new DisconnectedException(disconnectReason), ref reader, handler.Value.Item2);
            }
        }
    }

    public async ValueTask CallMethodAsync(Message message, MethodReturnHandler returnHandler, object state)
    {
        bool messageSent = false;
        try
        {
            uint nextSerial;
            lock (_gate)
            {
                if (_state != ConnectionState.Connected)
                {
                    throw new DisconnectedException(DisconnectReason!);
                }
                nextSerial = ++_nextSerial;
                _methodReturnHandlers!.Add(nextSerial, (returnHandler, state));
            }

            message.SetSerial(nextSerial);

            messageSent = await _messageStream!.TrySendMessageAsync(message);
        }
        finally
        {
            if (!messageSent)
            {
                message.ReturnToPool();
            }
        }
    }
}