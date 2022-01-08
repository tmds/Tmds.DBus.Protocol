using Tmds.DBus.Protocol;

class Program
{
    static async Task Main()
    {
        string peerName = await StartAddServiceAsync();

        var connection = Connection.Session;

        var addProxy = new AddProxy(connection, peerName);

        int sum = await addProxy.AddAsync(10, 20);

        Console.WriteLine(sum);
    }

    private async static Task<string> StartAddServiceAsync()
    {
        var connection = new Connection(Address.Session!);

        await connection.ConnectAsync();

        connection.AddMethodHandler(new AddImplementation());

        return connection.UniqueName ?? "";
    }

    // private static void PrintMessage(in Message message)
    // {
    //     StringBuilder sb = new();
    //     MessageFormatter.FormatMessage(message, sb);
    //     Console.WriteLine(sb.ToString());
    // }
}

class AddProxy
{
    private const string Interface = "org.example.Adder";
    private const string Path = "/org/example/Adder";

    private readonly Connection _connection;
    private readonly string _peer;

    public AddProxy(Connection connection, string peer)
    {
        _connection = connection;
        _peer = peer;
    }

    public Task<int> AddAsync(int i, int j)
    {
        return _connection.CallMethodAsync(CreateAddMessage(),
            (in Message message, object? state) =>
            {
                return message.GetBodyReader().ReadInt32();
            });

        MessageBuffer CreateAddMessage()
        {
            var writer = _connection.GetMessageWriter();

            writer.WriteMethodCallHeader(
                destination: _peer,
                path: Path,
                @interface: Interface,
                signature: "ii",
                member: "Add");

            writer.WriteInt32(i);
            writer.WriteInt32(j);

            return writer.CreateMessage();
        }
    }
}

class AddImplementation : IMethodHandler
{
    public string Path => "/org/example/Adder";

    public bool TryHandleMethod(Connection connection, in Message message)
    {
        string method = message.Member.ToString();
        string sig = message.Signature.ToString();
        switch ((method, sig))
        {
            case ("Add", "ii"):
                Add(connection, message);
                return true;
        }

        return false;
    }

    private void Add(Connection connection, in Message message)
    {
        var reader = message.GetBodyReader();

        int i = reader.ReadInt32();
        int j = reader.ReadInt32();

        connection.TrySendMessage(CreateResponseMessage(message));

        MessageBuffer CreateResponseMessage(in Message message)
        {
            var writer = connection.GetMessageWriter();

            writer.WriteMethodReturnHeader(
                replySerial: message.Serial,
                destination: message.Sender,
                signature: "i"
            );

            writer.WriteInt32(i + j);

            return writer.CreateMessage();
        }
    }
}