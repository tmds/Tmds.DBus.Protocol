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
    private readonly Dictionary<uint, (MessageReceivedHandler, object?)> _pendingCalls;
    private readonly CancellationTokenSource _connectCts;
    private readonly Dictionary<string, MatchMaker> _matchMakers;
    private readonly List<Observer> _matchedObservers;
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

    public bool RemoteIsBus => _localName is not null;

    public DBusConnection(Connection parent)
    {
        _parentConnection = parent;
        _connectCts = new();
        _pendingCalls = new();
        _matchMakers = new();
        _matchedObservers = new();
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

        if (firstException is not null)
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
            (Exception? exception, ref MessageReader reader, object? state) =>
            {
                var tcsState = (TaskCompletionSource<string?>)state!;

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

        static Message CreateHelloMessage()
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
    }

    private void HandleMessages(Exception? exception, ref MessageReader reader)
    {
        if (exception is not null)
        {
            _parentConnection.Disconnect(exception, this);
        }
        else
        {
            (MessageReceivedHandler handler, object? state) pendingCall = default;

            lock (_gate)
            {
                if (_state == ConnectionState.Disconnected)
                {
                    return;
                }

                if (reader.ReplySerial.HasValue)
                {
                    _pendingCalls.Remove(reader.ReplySerial.Value, out pendingCall);
                }

                foreach (var matchMaker in _matchMakers.Values)
                {
                    if (matchMaker.Matches(reader))
                    {
                        _matchedObservers.AddRange(matchMaker.Observers);
                    }
                }
            }

            foreach (var observer in _matchedObservers)
            {
                observer.Emit(ref reader);
            }
            _matchedObservers.Clear();

            if (pendingCall.handler is not null)
            {
                pendingCall.handler(null, ref reader, pendingCall.state!);
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

        if (_pendingCalls is not null)
        {
            MessageReader reader = default;
            foreach (var handler in _pendingCalls.Values)
            {
                handler.Item1.Invoke(new DisconnectedException(disconnectReason), ref reader, handler.Item2);
            }
            _pendingCalls.Clear();
        }

        if (_matchMakers is not null)
        {
            foreach (var matchMaker in _matchMakers.Values)
            {
                foreach (var observer in matchMaker.Observers)
                {
                    observer.Disconnect(new DisconnectedException(disconnectReason));
                }
            }
            _matchMakers.Clear();
        }
    }

    public async ValueTask CallMethodAsync(Message message, MessageReceivedHandler returnHandler, object? state)
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
                // TODO: don't add pending call when NoReplyExpected.
                _pendingCalls.Add(nextSerial, (returnHandler, state));
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

    public async Task<T> CallMethodAsync<T>(Message message, MethodReturnHandler<T> returnHandler, bool runContinuationsAsync = true)
    {
        TaskCompletionSource<T> tcs = new(runContinuationsAsync ? TaskCreationOptions.RunContinuationsAsynchronously : TaskCreationOptions.None);

        await CallMethodAsync(
            message,
            (Exception? exception, ref MessageReader reader, object? state) =>
            {
                var tcsState = (TaskCompletionSource<T>)state!;

                if (exception is not null)
                {
                    tcsState.SetException(exception);
                }
                else if (reader.Type == MessageType.MethodReturn)
                {
                    tcsState.SetResult(returnHandler(ref reader));
                }
                else if (reader.Type == MessageType.Error)
                {
                    string errorName = reader.ErrorName.ToString();
                    string message = errorName;
                    if (!reader.Signature.IsEmpty && (DBusType)reader.Signature.Span[0] == DBusType.String)
                    {
                        message = reader.ReadStringAsString();
                    }
                    tcsState.SetException(new DBusException(errorName, message));
                }
                else
                {
                    tcsState.SetException(new ProtocolException($"Unexpected reply type: {reader.Type}."));
                }
            }, tcs);

        return await tcs.Task;
    }

    public async Task CallMethodAsync(Message message, bool runContinuationsAsync = true)
    {
        TaskCompletionSource tcs = new(runContinuationsAsync ? TaskCreationOptions.RunContinuationsAsynchronously : TaskCreationOptions.None);

        await CallMethodAsync(message,
            (Exception? exception, ref MessageReader reader, object? state) => CompleteCallTaskCompletionSource(exception, ref reader, state), tcs);

        await tcs.Task;
    }

    private static void CompleteCallTaskCompletionSource(Exception? exception, ref MessageReader reader, object? state)
    {
        var tcsState = (TaskCompletionSource)state!;

        if (exception is not null)
        {
            tcsState.SetException(exception);
        }
        else if (reader.Type == MessageType.MethodReturn)
        {
            tcsState.SetResult();
        }
        else if (reader.Type == MessageType.Error)
        {
            string errorName = reader.ErrorName.ToString();
            string message = errorName;
            if (!reader.Signature.IsEmpty && (DBusType)reader.Signature.Span[0] == DBusType.String)
            {
                message = reader.ReadStringAsString();
            }
            tcsState.SetException(new DBusException(errorName, message));
        }
        else
        {
            tcsState.SetException(new ProtocolException($"Unexpected reply type: {reader.Type}."));
        }
    }

    public async ValueTask<IDisposable> AddMatchAsync(MatchRule rule, MessageReceivedHandler handler, object? state, bool subscribe)
    {
        MatchRuleData data = rule.Data;
        MatchMaker? matchMaker;
        string ruleString;
        uint nextSerial = 0;
        bool sendMessage;
        Observer observer;

        lock (_gate)
        {
            if (_state != ConnectionState.Connected)
            {
                throw new DisconnectedException(DisconnectReason!);
            }

            if (!RemoteIsBus)
            {
                subscribe = false;
            }

            ruleString = data.GetRuleString();

            if (!_matchMakers.TryGetValue(ruleString, out matchMaker))
            {
                matchMaker = new MatchMaker(this, ruleString, in data);
                _matchMakers.Add(ruleString, matchMaker);
            }

            observer = new Observer(matchMaker, handler, state, subscribe);
            matchMaker.Observers.Add(observer);

            sendMessage = subscribe && matchMaker.AddMatchTcs is null;

            if (sendMessage)
            {
                matchMaker.AddMatchTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                nextSerial = ++_nextSerial;

                MessageReceivedHandler receiveHandler = static (Exception? exception, ref MessageReader reader, object? state)
                                                            => HandleReply(exception, ref reader, (MatchMaker)state!);

                _pendingCalls.Add(nextSerial, (receiveHandler, matchMaker));
            }
        }

        if (subscribe)
        {
            if (sendMessage)
            {
                var message = CreateAddMatchMessage(matchMaker.RuleString);
                message.SetSerial(nextSerial);
                if (!await _messageStream!.TrySendMessageAsync(message))
                {
                    message.ReturnToPool();
                }
            }

            try
            {
                await matchMaker.AddMatchTcs!.Task;
            }
            catch
            {
                observer.Dispose();

                throw;
            }
        }

        return observer;

        static void HandleReply(Exception? exception, ref MessageReader reader, MatchMaker mm)
        {
            if (reader.Type == MessageType.MethodReturn)
            {
                mm.HasSubscribed = true;
            }
            CompleteCallTaskCompletionSource(exception, ref reader, mm.AddMatchTcs!);
        }

        static Message CreateAddMatchMessage(string ruleString)
        {
            var rented = MessagePool.Shared.Rent();
            var message = rented.Message;

            MessageWriter writer = message.GetWriter();

            writer.WriteMethodCallHeader(
                destination: "org.freedesktop.DBus",
                path: "/org/freedesktop/DBus",
                @interface: "org.freedesktop.DBus",
                member: "AddMatch",
                signature: "s");

            writer.WriteString(ruleString);

            writer.Flush();

            return message;
        }
    }

    sealed class Observer : IDisposable
    {
        private readonly object _gate = new object();
        private readonly MatchMaker _matchMaker;
        private readonly MessageReceivedHandler _messageHandler;
        private readonly object? _state;
        private bool _disposed;

        public bool Subscribes { get; }

        public Observer(MatchMaker matchMaker, MessageReceivedHandler messageHandler, object? state, bool subscribes)
        {
            _matchMaker = matchMaker;
            _messageHandler = messageHandler;
            _state = state;
            Subscribes = subscribes;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;

                MessageReader reader = default;
                // TODO: signal Dispose without allocating an Exception?
                _messageHandler(new ObjectDisposedException(GetType().FullName), ref reader, _state);
            }

            _matchMaker.Connection.RemoveObserver(_matchMaker, this);
        }

        public void Emit(ref MessageReader reader)
        {
            if (Subscribes && !_matchMaker.HasSubscribed)
            {
                return;
            }

            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _messageHandler(null, ref reader, _state);
            }
        }

        internal void Disconnect(DisconnectedException disconnectedException)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;

                MessageReader reader = default;
                _messageHandler(disconnectedException, ref reader, _state);
            }
        }
    }

    private async void RemoveObserver(MatchMaker matchMaker, Observer observer)
    {
        string ruleString = matchMaker.RuleString;
        uint nextSerial = 0;
        bool sendMessage = false;

        lock (_gate)
        {
            if (_state == ConnectionState.Disconnected)
            {
                return;
            }

            if (_matchMakers.TryGetValue(ruleString, out _))
            {
                matchMaker.Observers.Remove(observer);
                sendMessage = matchMaker.AddMatchTcs is not null && matchMaker.HasSubscribers;
                if (sendMessage)
                {
                    _matchMakers.Remove(ruleString);
                    nextSerial = ++_nextSerial;
                }
            }
        }

        if (sendMessage)
        {
            var message = CreateRemoveMatchMessage(ruleString);
            message.SetSerial(nextSerial);

            if (!await _messageStream!.TrySendMessageAsync(message))
            {
                message.ReturnToPool();
            }
        }

        static Message CreateRemoveMatchMessage(string ruleString)
        {
            var rented = MessagePool.Shared.Rent();
            var message = rented.Message;

            MessageWriter writer = message.GetWriter();

            writer.WriteMethodCallHeader(
                destination: "org.freedesktop.DBus",
                path: "/org/freedesktop/DBus",
                @interface: "org.freedesktop.DBus",
                member: "RemoveMatch",
                signature: "s",
                flags: MessageFlags.NoReplyExpected);

            writer.WriteString(ruleString);

            writer.Flush();

            return message;
        }
    }

    sealed class MatchMaker
    {
        private readonly MessageType? _type;
        private readonly byte[]? _sender;
        private readonly byte[]? _interface;
        private readonly byte[]? _member;
        private readonly byte[]? _path;
        private readonly byte[]? _pathNamespace;
        private readonly byte[]? _destination;
        private readonly byte[]? _arg0;
        private readonly byte[]? _arg0Path;
        private readonly byte[]? _arg0Namespace;
        private readonly string _rule;

        public List<Observer> Observers { get; } = new();

        public TaskCompletionSource? AddMatchTcs { get; set; }

        public bool HasSubscribed { get; set; }

        public DBusConnection Connection { get; }

        public MatchMaker(DBusConnection connection, string rule, in MatchRuleData data)
        {
            Connection = connection;
            _rule = rule;

            _type = data.Type;

            if (data.Sender is not null)
            {
                _sender = Encoding.UTF8.GetBytes(data.Sender);
            }
            if (data.Interface is not null)
            {
                _interface = Encoding.UTF8.GetBytes(data.Interface);
            }
            if (data.Member is not null)
            {
                _member = Encoding.UTF8.GetBytes(data.Member);
            }
            if (data.Path is not null)
            {
                _path = Encoding.UTF8.GetBytes(data.Path);
            }
            if (data.PathNamespace is not null)
            {
                _pathNamespace = Encoding.UTF8.GetBytes(data.PathNamespace);
            }
            if (data.Destination is not null)
            {
                _destination = Encoding.UTF8.GetBytes(data.Destination);
            }
            if (data.Arg0 is not null)
            {
                _arg0 = Encoding.UTF8.GetBytes(data.Arg0);
            }
            if (data.Arg0Path is not null)
            {
                _arg0Path = Encoding.UTF8.GetBytes(data.Arg0Path);
            }
            if (data.Arg0Namespace is not null)
            {
                _arg0Namespace = Encoding.UTF8.GetBytes(data.Arg0Namespace);
            }
        }

        public string RuleString => _rule;

        public bool HasSubscribers
        {
            get
            {
                if (Observers.Count == 0)
                {
                    return false;
                }
                foreach (var observer in Observers)
                {
                    if (observer.Subscribes)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public override string ToString() => _rule;

        internal bool Matches(MessageReader reader) // TODO: 'in' arg
        {
            if (_type.HasValue && _type != reader.Type)
            {
                return false;
            }

            if (_sender is not null && !reader.Sender.Span.SequenceEqual(_sender))
            {
                return false;
            }

            if (_interface is not null && !reader.Interface.Span.SequenceEqual(_interface))
            {
                return false;
            }

            if (_member is not null && !reader.Member.Span.SequenceEqual(_member))
            {
                return false;
            }

            if (_path is not null && !reader.Path.Span.SequenceEqual(_path))
            {
                return false;
            }

            if (_destination is not null && !reader.Destination.Span.SequenceEqual(_destination))
            {
                return false;
            }

            if (_pathNamespace is not null && !IsEqualOrChildOfPath(reader.Path, _pathNamespace))
            {
                return false;
            }

            if (_arg0Namespace is not null ||
                _arg0 is not null ||
                _arg0Path is not null)
            {
                reader.Rewind();

                if (reader.Signature.IsEmpty)
                {
                    return false;
                }

                DBusType arg0Type = (DBusType)reader.Signature.Span[0];

                if (arg0Type != DBusType.String ||
                    arg0Type != DBusType.ObjectPath)
                {
                    return false;
                }

                ReadOnlySpan<byte> arg0 = reader.ReadString();

                if (_arg0Path is not null && !IsEqualParentOrChildOfPath(arg0, _arg0Path))
                {
                    return false;
                }

                if (arg0Type != DBusType.String)
                {
                    return false;
                }

                if (_arg0 is not null && !arg0.SequenceEqual(_arg0))
                {
                    return false;
                }

                if (_arg0Namespace is not null && !IsEqualOrChildOfName(_arg0, _arg0Namespace))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsEqualOrChildOfName(ReadOnlySpan<byte> lhs, ReadOnlySpan<byte> rhs)
        {
            return lhs.StartsWith(rhs) && (lhs.Length == rhs.Length || lhs[rhs.Length] == '.');
        }

        private static bool IsEqualOrChildOfPath(ReadOnlySpan<byte> lhs, ReadOnlySpan<byte> rhs)
        {
            return lhs.StartsWith(rhs) && (lhs.Length == rhs.Length || lhs[rhs.Length] == '/');
        }

        private static bool IsEqualParentOrChildOfPath(ReadOnlySpan<byte> lhs, ReadOnlySpan<byte> rhs)
        {
            if (rhs.Length < lhs.Length)
            {
                return rhs[^1] == '/' && lhs.StartsWith(rhs);
            }
            else if (lhs.Length < rhs.Length)
            {
                return lhs[^1] == '/' && rhs.StartsWith(lhs);
            }
            else
            {
                return lhs.SequenceEqual(rhs);
            }
        }
    }
}