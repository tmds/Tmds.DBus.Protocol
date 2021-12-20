namespace Tmds.DBus.Protocol;

public class MessageFormatter
{
    public static void FormatMessage(ref MessageReader messageReader, StringBuilder sb)
    {
        MessageReader msg = messageReader.CloneAndRewind();

        // Header.
        Append(sb, msg.Type);
        sb.Append("  ");
        sb.Append("serial=");
        sb.Append(msg.Serial);
        sb.Append("  ");
        if (msg.ReplySerial.HasValue)
        {
            sb.Append("repl=");
            sb.Append(msg.ReplySerial.Value);
            sb.Append("  ");
        }
        Append(sb, "err", msg.Error);
        sb.Append("  ");
        Append(sb, "path", msg.Path);
        sb.Append("  ");
        Append(sb, "memb", msg.Member);
        sb.Append("  ");
        Append(sb, "body", msg.Signature);
        sb.Append("  ");
        Append(sb, "src", msg.Sender);
        sb.Append("  ");
        Append(sb, "dst", msg.Destination);
        sb.Append("  ");
        Append(sb, "ifac", msg.Interface);
        sb.Append("  ");
        if (msg.UnixFds != 0)
        {
            sb.Append("fds=");
            sb.Append(msg.UnixFds);
            sb.Append("  ");
        }

        sb.AppendLine();

        // Body.
        int indent = 2;
        ReadData(sb, ref msg, new SignatureReader(msg.Signature), indent);

        // Remove final newline.
        sb.Remove(sb.Length - Environment.NewLine.Length, Environment.NewLine.Length);
    }

    private static void ReadData(StringBuilder sb, ref MessageReader msg, SignatureReader sigReader, int indent)
    {
        while (sigReader.TryRead(out DBusType type, out SignatureReader innerReader))
        {
            if (type == DBusType.Invalid)
            {
                // TODO something is wrong, complain loudly.
                break;
            }
            sb.Append(' ', indent);
            switch (type)
            {
                // case DBusType.Byte:
                //     sb.AppendLine($"byte   {msg.ReadByte()}");
                //     break;
                // case DBusType.Bool:
                //     sb.AppendLine($"bool   {msg.ReadBool()}");
                //     break;
                // case DBusType.Int16:
                //     sb.AppendLine($"int16  {msg.ReadInt16()}");
                //     break;
                // case DBusType.UInt16:
                //     sb.AppendLine($"uint16 {msg.ReadUInt16()}");
                //     break;
                // case DBusType.Int32:
                //     sb.AppendLine($"int32  {msg.ReadInt32()}");
                //     break;
                case DBusType.UInt32:
                    sb.AppendLine($"uint32 {msg.ReadUInt32()}");
                    break;
                // case DBusType.Int64:
                //     sb.Append($"int64  {msg.ReadInt64()}");
                //     break;
                // case DBusType.UInt64:
                //     sb.Append($"uint64 {msg.ReadUInt64()}");
                //     break;
                // case DBusType.Double:
                //     sb.Append($"double {msg.ReadDouble()}");
                //     break;
                case DBusType.UnixFd:
                    sb.AppendLine($"fd     {msg.ReadHandle(own: false)}");
                    break;
                case DBusType.String:
                    sb.Append("string ");
                    Append(sb, msg.ReadString()); // TODO: handle long strings without allocating.
                    sb.AppendLine();
                    break;
                case DBusType.ObjectPath:
                    sb.Append("path   ");
                    Append(sb, msg.ReadObjectPath()); // TODO: handle long strings without allocating.
                    sb.AppendLine();
                    break;
                case DBusType.Signature:
                    sb.Append("sig    ");
                    Append(sb, msg.ReadSignature());
                    sb.AppendLine();
                    break;
                case DBusType.Array:
                    sb.AppendLine("array  [");
                    int alignment = ProtocolConstants.GetFirstTypeAlignment(innerReader.Signature);
                    ArrayEnd itEnd = msg.ReadArrayStart(alignment);
                    while (msg.HasNext(itEnd))
                    {
                        ReadData(sb, ref msg, innerReader, indent + 2);
                    }
                    sb.Append(' ', indent);
                    sb.AppendLine("]");
                    break;
                case DBusType.Struct:
                    sb.AppendLine("struct (");
                    ReadData(sb, ref msg, innerReader, indent + 2);
                    sb.Append(' ', indent);
                    sb.AppendLine(")");
                    break;
                case DBusType.Variant:
                    sb.AppendLine("var   ("); // TODO: merge with next line
                    ReadData(sb, ref msg, new SignatureReader(msg.ReadSignature()), indent + 2);
                    sb.Append(' ', indent);
                    sb.AppendLine(")");
                    break;
                case DBusType.DictEntry:
                    sb.AppendLine("dicte (");
                    ReadData(sb, ref msg, innerReader, indent + 2);
                    sb.Append(' ', indent);
                    sb.AppendLine(")");
                    break;
            }
        }
        // TODO: complain if there is still data left.
    }

    private static void Append(StringBuilder sb, MessageType type)
    {
        switch (type)
        {
            case MessageType.MethodCall:
                sb.Append("call"); break;
            case MessageType.MethodReturn:
                sb.Append("ret "); break;
            case MessageType.Error:
                sb.Append("err "); break;
            case MessageType.Signal:
                sb.Append("sig "); break;
            default:
                sb.Append($"?{type}"); break;
        }
    }

    private static void Append(StringBuilder sb, string field, StringSpan value)
    {
        if (value.IsEmpty)
        {
            return;
        }

        sb.Append(field);
        sb.Append('=');
        Append(sb, value);
    }

    private static void Append(StringBuilder sb, StringSpan value)
    {
        char[]? valueArray = null;

        int length = Encoding.UTF8.GetCharCount(value.Span);

        Span<char> charBuffer = length <= StackAllocCharThreshold ?
            stackalloc char[length] :
            (valueArray = ArrayPool<char>.Shared.Rent(length));

        int charsWritten = Encoding.UTF8.GetChars(value.Span, charBuffer);

        sb.Append(charBuffer.Slice(0, charsWritten));

        if (valueArray != null)
        {
            ArrayPool<char>.Shared.Return(valueArray);
        }
    }

    private const int StackAllocByteThreshold = 512;
    private const int StackAllocCharThreshold = StackAllocByteThreshold / 2;
}