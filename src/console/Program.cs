using System;
using System.Text;
using Tmds.DBus.Protocol;

class Program
{
    static async Task Main()
    {
        NetworkManagerExample();

        var connection = Connection.Session;

        // await connection.CallMethodAsync(CreateHelloMessage(connection));

        Console.WriteLine("Press any key to stop the application.");
        Console.WriteLine();

        Console.ReadLine();
    }

    private static async void NetworkManagerExample()
    {
        var nm = new NetworkManager(Connection.System);

        foreach (var device in await nm.GetDevicesAsync())
        {
            Console.WriteLine(device);

            await device.WatchStateChangedAsync(
                static (Exception? ex, (DeviceState, DeviceState) change, object? state) =>
                {
                    Console.WriteLine($"{state} {change.Item1} -> {change.Item2}");
                }, device);
        }
    }

    private static void PrintMessage(in Message message)
    {
        StringBuilder sb = new();
        MessageFormatter.FormatMessage(message, sb);
        Console.WriteLine(sb.ToString());
    }

    private static MessageBuffer CreateHelloMessage(Connection connection)
    {
        MessageWriter writer = connection.GetMessageWriter();

        writer.WriteMethodCallHeader(
            destination: "org.freedesktop.DBus",
            path: "/org/freedesktop/DBus",
            @interface: "org.freedesktop.DBus",
            member: "Hello");

        return writer.CreateMessage();
    }
}