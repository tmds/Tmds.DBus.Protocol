using System.Text;
using Tmds.DBus.Protocol;

class Program
{
    static void Main()
    {
        using var rented = MessagePool.Shared.Rent();
        var message = rented.Message;

        WriteMessage(message);

        if (MessageReader.TryReadMessage(message.AsReadOnlySequence(), out MessageReader reader))
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

    private static void WriteMessage(Message message)
    {
        uint i = 314;
        uint j = 159;

        // Write message.
        MessageWriter writer = message.GetWriter();
        writer.WriteMethodCallHeader(
            destination: default,
            path: s_example_calculator_Path,
            @interface: default,
            member: s_add_Method,
            signature: s_add_MethodSignature);

        writer.WriteUInt32(i);
        writer.WriteUInt32(j);

        ArrayStart start = writer.WriteArrayStart(4);
        writer.WriteUInt32(i);
        writer.WriteUInt32(j);
        writer.WriteUInt32(i);
        writer.WriteArrayEnd(ref start);

        writer.Flush();
    }

    private static readonly byte[] s_example_calculator_Path_Bytes = Encoding.UTF8.GetBytes("/example/calculator");
    private static StringSpan s_example_calculator_Path => new StringSpan(s_example_calculator_Path_Bytes);
    private static readonly byte[] s_add_Method_Bytes = Encoding.UTF8.GetBytes("Add");
    private static StringSpan s_add_Method => new StringSpan(s_add_Method_Bytes);
    private static readonly byte[] s_add_MethodSignature_Bytes = Encoding.UTF8.GetBytes("uuau");
    private static StringSpan s_add_MethodSignature => new StringSpan(s_add_MethodSignature_Bytes);
}