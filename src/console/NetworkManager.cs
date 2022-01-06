using Tmds.DBus.Protocol;

class DBusObject
{
    protected DBusObject(Connection connection, string path, string peer)
    {
        Connection = connection;
        Path = path;
        Peer = peer;
    }

    public Connection Connection { get; }
    public string Path { get; }
    public string Peer { get; }

    public override string ToString() => Path;

    protected string? GetUniquePeerName() => Peer.StartsWith(":") ? Peer : null;
}

sealed class NetworkManager : DBusObject
{
    private const string DefaultPath = "/org/freedesktop/NetworkManager";
    private const string Service = "org.freedesktop.NetworkManager";
    private const string Interface = "org.freedesktop.NetworkManager";

    public NetworkManager(Connection connection, string path = DefaultPath, string peer = Service) :
        base(connection, path, peer)
    { }

    public Task<Device[]> GetDevicesAsync()
    {
        return Connection.CallMethodAsync<Device[]>(
            message: CreateMessage(),
            reader: static (ref Message message, object? state) =>
            {
                DBusObject nm = (DBusObject)state!;

                List<Device> devices = new();

                var reader = message.GetBodyReader();

                ArrayEnd arrayEnd = reader.ReadArrayStart(DBusType.ObjectPath);
                while (reader.HasNext(arrayEnd))
                {
                    devices.Add(new Device(nm.Connection, reader.ReadObjectPath().ToString(), nm.Peer));
                }

                return devices.ToArray();
            }, this);

        MessageBuffer CreateMessage()
        {
            var writer = Connection.GetMessageWriter();

            writer.WriteMethodCallHeader(
                destination: Peer,
                path: Path,
                @interface: Interface,
                member: "GetDevices");

            return writer.CreateMessage();
        }
    }
}

sealed class Device : DBusObject
{
    private const string Service = "org.freedesktop.NetworkManager";
    private const string Interface = "org.freedesktop.NetworkManager.Device";

    public Device(Connection connection, string path, string peer = Service) :
        base(connection, path, peer)
    { }

    public async ValueTask<IDisposable> WatchStateChangedAsync(Action<Exception?, (DeviceState, DeviceState), object?> handler, object? state = null)
    {
        var rule = new MatchRule
        {
            Type = MessageType.Signal,
            Interface = Interface,
            Path = Path,
            Member = "StateChanged",
            Sender = GetUniquePeerName()
        };

        return await Connection.AddMatchAsync(
            rule,
            reader: static (ref Message message, object? state) =>
            {
                var reader = message.GetBodyReader();

                var oldState = reader.ReadUInt32();
                var newState = reader.ReadUInt32();

                return ((DeviceState)oldState, (DeviceState)newState);
            },
            handler, handlerState: state);
    }
}

enum DeviceState : uint
{
    Unknown = 0,
    Unmanaged = 10,
    Unavailable = 20,
    Disconnected = 30,
    Prepare = 40,
    Config = 50,
    NeedAuth = 60,
    IpConfig = 70,
    IpCheck = 80,
    Secondaries = 90,
    Activated = 100,
    Deactivating = 110,
    Failed = 120
}