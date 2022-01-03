using System;
using System.Text;
using Tmds.DBus.Protocol;

class Program
{
    static async Task Main()
    {
        var connection = Connection.Session;

        await connection.AddMatchAsync(new MatchRule
        {
            Type = MessageType.Signal,
            Member = "NameOwnerChanged"
        }, OnNameOwnerChanged);

        await connection.CallMethodAsync(
            message: CreateHelloMessage(connection),
            (Exception? exception, ref Message message, object? state) =>
            {
                PrintMessage(message);
            });

        Console.WriteLine("Press any key to stop the application.");
        Console.WriteLine();

        Console.ReadLine();
    }

    private static void OnNameOwnerChanged(Exception? exception, ref Message message, object? state)
    {
        Console.WriteLine("NameOwnerChanged:");
        PrintMessage(message);
    }

    private static void ReceiveMessages(Exception? exception, ref Message message, object? state)
    {
        if (exception is not null)
        {
            if (exception is not ObjectDisposedException)
            {
                Console.WriteLine($"Exception: {exception}");
            }
        }
        else
        {
            PrintMessage(message);
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