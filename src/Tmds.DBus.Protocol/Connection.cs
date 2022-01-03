namespace Tmds.DBus.Protocol;

public delegate void MessageReceivedHandler(Exception? exception, ref MessageReader reader, object? state);
public delegate T MethodReturnHandler<T>(ref MessageReader reader);

public class Connection : IDisposable
{
    private static readonly Exception s_disposedSentinel = new ObjectDisposedException(typeof(Connection).FullName);
    private static Connection? s_systemConnection;
    private static Connection? s_sessionConnection;

    public static Connection System => s_systemConnection ?? CreateConnection(ref s_systemConnection, Address.System);
    public static Connection Session => s_sessionConnection ?? CreateConnection(ref s_sessionConnection, Address.Session);

    enum ConnectionState
    {
        Created,
        Connecting,
        Connected,
        Disconnected
    }

    private readonly object _gate = new object();
    private readonly ClientConnectionOptions _connectionOptions;
    private DBusConnection? _connection;
    private CancellationTokenSource? _connectCts;
    private Task<DBusConnection>? _connectingTask;
    private ClientSetupResult? _setupResult;
    private ConnectionState _state;
    private bool _disposed;

    public Connection(string address) :
        this(new ClientConnectionOptions(address))
    { }

    public Connection(ConnectionOptions connectionOptions)
    {
        if (connectionOptions == null)
            throw new ArgumentNullException(nameof(connectionOptions));

        _connectionOptions = (ClientConnectionOptions)connectionOptions;
    }

    public async ValueTask ConnectAsync()
    {
        await ConnectCoreAsync(autoConnect: false);
    }

    private ValueTask<DBusConnection> ConnectCoreAsync(bool autoConnect = true)
    {
        lock (_gate)
        {
            ThrowHelper.ThrowIfDisposed(_disposed, this);

            ConnectionState state = _state;

            if (state == ConnectionState.Connected)
            {
                return ValueTask.FromResult(_connection!);
            }

            if (!_connectionOptions.AutoConnect)
            {
                if (autoConnect || _state != ConnectionState.Created)
                {
                    throw new InvalidOperationException("Can only connect once using an explicit call.");
                }
            }

            if (state == ConnectionState.Connecting)
            {
                return new ValueTask<DBusConnection>(_connectingTask!);
            }

            _state = ConnectionState.Connecting;
            _connectingTask = DoConnectAsync();

            return new ValueTask<DBusConnection>(_connectingTask);
        }
    }

    private async Task<DBusConnection> DoConnectAsync()
    {
        Debug.Assert(Monitor.IsEntered(_gate));

        DBusConnection? connection = null;
        try
        {
            _connectCts = new();
            _setupResult = await _connectionOptions.SetupAsync(_connectCts.Token);
            connection = _connection = new DBusConnection(this);

            await connection.ConnectAsync(_setupResult.ConnectionAddress, _setupResult.UserId, _setupResult.SupportsFdPassing, _connectCts.Token);

            lock (_gate)
            {
                ThrowHelper.ThrowIfDisposed(_disposed, this);

                if (_connection == connection)
                {
                    _connectingTask = null;
                    _connectCts = null;
                    _state = ConnectionState.Connected;
                }
                else
                {
                    throw new DisconnectedException(connection.DisconnectReason);
                }
            }

            return connection;
        }
        catch (Exception exception)
        {
            Disconnect(exception, connection);

            ThrowHelper.ThrowIfDisposed(_disposed, this);

            throw;
        }
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
        }

        Disconnect(s_disposedSentinel);
    }

    internal void Disconnect(Exception disconnectReason, DBusConnection? trigger = null)
    {
        DBusConnection? connection;
        ClientSetupResult? setupResult;
        CancellationTokenSource? connectCts;
        lock (_gate)
        {
            if (trigger is not null && trigger != _connection)
            {
                // Already disconnected from this stream.
                return;
            }

            ConnectionState state = _state;
            if (state == ConnectionState.Disconnected)
            {
                return;
            }

            _state = ConnectionState.Disconnected;

            connection = _connection;
            setupResult = _setupResult;
            connectCts = _connectCts;

            _connection = null;
            _connectingTask = null;
            _setupResult = null;
            _connectCts = null;

            if (connection is not null)
            {
                connection.DisconnectReason = disconnectReason;
            }
        }

        connectCts?.Cancel();
        connection?.Dispose();
        if (setupResult != null)
        {
            _connectionOptions.Teardown(setupResult.TeardownToken);
        }
    }

    public async ValueTask CallMethodAsync(Message message, MessageReceivedHandler handler, object? state = null)
    {
        DBusConnection connection;
        try
        {
            connection = await ConnectCoreAsync();
        }
        catch
        {
            message.ReturnToPool();
            throw;
        }
        await connection.CallMethodAsync(message, handler, state);
    }

    public async ValueTask<IDisposable> AddMatchAsync(MatchRule rule, MessageReceivedHandler handler, object? state = null, bool subscribe = true)
    {
        DBusConnection connection = await ConnectCoreAsync();
        return await connection.AddMatchAsync(rule, handler, state, subscribe);
    }

    private static Connection CreateConnection(ref Connection? field, string? address)
    {
        address = address ?? "unix:";
        var connection = Volatile.Read(ref field);
        if (connection is not null)
        {
            return connection;
        }
        var newConnection = new Connection(new ClientConnectionOptions(address) { AutoConnect = true });
        connection = Interlocked.CompareExchange(ref field, newConnection, null);;
        if (connection != null)
        {
            newConnection.Dispose();
            return connection;
        }
        return newConnection;
    }
}