namespace Tmds.DBus.Protocol;

public delegate void MethodReturnHandler(Exception? exception, ref MessageReader reader, object state);

public class Connection : IDisposable
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
    private DBusConnection? _connection;
    private Task<DBusConnection>? _connectingTask;
    private ConnectionState _state;
    private bool _disposed;

    public async ValueTask ConnectAsync()
    {
        await ConnectCoreAsync();
    }

    private ValueTask<DBusConnection> ConnectCoreAsync()
    {
        lock (_gate)
        {
            ThrowHelper.ThrowIfDisposed(_disposed, this);

            switch (_state)
            {
                case ConnectionState.Connected:
                    return ValueTask.FromResult(_connection!);
                case ConnectionState.Connecting:
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
            string address = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")!; // TODO
            connection = _connection = new DBusConnection(address, this);

            await connection.ConnectAsync();

            lock (_gate)
            {
                ThrowHelper.ThrowIfDisposed(_disposed, this);

                if (_connection == connection)
                {
                    _connectingTask = null;
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
        lock (_gate)
        {
            if (trigger != null && trigger != _connection)
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
            _connection = null;
            _connectingTask = null;

            if (connection != null)
            {
                connection.DisconnectReason = disconnectReason;
            }
        }

        connection?.Dispose();
    }

    public async ValueTask CallMethodAsync(Message message, MethodReturnHandler returnHandler, object state)
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
        await connection.CallMethodAsync(message, returnHandler, state);
    }
}