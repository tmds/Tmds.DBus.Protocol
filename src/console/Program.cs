using System.Text;
using Tmds.DBus.Protocol;

class Program
{
    static async Task Main()
    {
        var connection = new Connection();

        await connection.CallMethodAsync(
            message: CreateHelloMessage(),
            (Exception? exception, ref MessageReader reader, object state) => {
                PrintMessage(reader);
             }, null!);

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
}