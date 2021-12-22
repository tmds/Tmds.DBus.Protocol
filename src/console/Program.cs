using System.Text;
using System.Buffers;
using Tmds.DBus.Protocol;

class Program
{
    private static uint _serial = 1;
    static async Task Main()
    {
        using var stream = await MessageStream.ConnectAsync(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"), "1000", true, default(CancellationToken));

        stream.ReceiveMessages(ReceiveMessages, (object)null!);

        using var rented = MessagePool.Shared.Rent();
        var message = rented.Message;

        WriteHello(message);

        await stream.SendMessageAsync(message);


        Console.WriteLine("Press any key to stop the application.");
        Console.WriteLine();

        Console.ReadLine();
    }

    private static void ReceiveMessages(Exception? exception, ref MessageReader reader, object state)
    {
        if (exception != null)
        {
            if (exception is not ObjectDisposedException)
            {
                Console.WriteLine($"Exception: {exception}");
            }
        }
        else
        {
            PrintMessage(reader);
        }
    }

    private static void PrintMessage(MessageReader reader)
    {
        StringBuilder sb = new();
        MessageFormatter.FormatMessage(ref reader, sb);
        Console.WriteLine(sb.ToString());
    }

    private static void WriteHello(Message message)
    {
        // Write message.
        MessageWriter writer = message.GetWriter();
        writer.WriteMethodCallHeader(
            destination: "org.freedesktop.DBus",
            path: "/org/freedesktop/DBus",
            @interface: "org.freedesktop.DBus",
            member: "Hello");

        writer.Flush();

        message.SetSerial(_serial++);
    }
}