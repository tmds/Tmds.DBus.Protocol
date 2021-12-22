namespace Tmds.DBus.Protocol;

public ref struct MessageWriter
{
    private readonly Message _message;
    private Span<byte> _span;
    private int _offset;
    private int _buffered;

    private IBufferWriter<byte> Writer
    {
        get
        {
            Flush();
            return _message.Writer;
        }
    }

    internal MessageWriter(Message message)
    {
        _message = message;
        _offset = 0;
        _buffered = 0;
        _span = _message.GetSpan(sizeHint: 0);
    }

    public void WriteMethodCallHeader(
        string? destination = null,
        string? path = null,
        string? @interface = null,
        string? member = null,
        string? signature = null,
        MessageFlags flags = MessageFlags.None)
    {
        ArrayStart start = WriteHeaderStart(MessageType.MethodCall, flags);

        // Path.
        if (path is not null)
        {
            WriteStructureStart();
            WriteByte((byte)MessageHeader.Path);
            WriteVariantObjectPath(path);
        }

        // Interface.
        if (@interface is not null)
        {
            WriteStructureStart();
            WriteByte((byte)MessageHeader.Interface);
            WriteVariantString(@interface);
        }

        // Member.
        if (member is not null)
        {
            WriteStructureStart();
            WriteByte((byte)MessageHeader.Member);
            WriteVariantString(member);
        }

        // Destination.
        if (destination is not null)
        {
            WriteStructureStart();
            WriteByte((byte)MessageHeader.Destination);
            WriteVariantString(destination);
        }

        // Signature.
        if (signature is not null)
        {
            WriteStructureStart();
            WriteByte((byte)MessageHeader.Signature);
            WriteVariantSignature(signature);
        }

        WriteHeaderEnd(ref start);
    }

    public void WriteMethodReturnHeader(
        uint replySerial,
        string? destination = null,
        string? signature = null)
    {
        ArrayStart start = WriteHeaderStart(MessageType.MethodReturn, MessageFlags.None);

        // Destination.
        if (destination is not null)
        {
            WriteStructureStart();
            WriteByte((byte)MessageHeader.Destination);
            WriteVariantString(destination);
        }

        // Signature.
        if (signature is not null)
        {
            WriteStructureStart();
            WriteByte((byte)MessageHeader.Signature);
            WriteVariantSignature(signature);
        }

        WriteHeaderEnd(ref start);
    }

    public void WriteErrorHeader(
        uint replySerial,
        string? destination = null,
        string? signature = null,
        string? errorName = null)
    {
        ArrayStart start = WriteHeaderStart(MessageType.Error, MessageFlags.None);

        // Destination.
        if (destination is not null)
        {
            WriteStructureStart();
            WriteByte((byte)MessageHeader.Destination);
            WriteVariantString(destination);
        }

        // Error.
        if (errorName is not null)
        {
            WriteStructureStart();
            WriteByte((byte)MessageHeader.ErrorName);
            WriteVariantString(errorName);
        }

        // Signature.
        if (signature is not null)
        {
            WriteStructureStart();
            WriteByte((byte)MessageHeader.Signature);
            WriteVariantSignature(signature);
        }

        WriteHeaderEnd(ref start);
    }

    public void WriteSignalHeader(
        string? destination = null,
        string? path = null,
        string? @interface = null,
        string? member = null,
        string? signature = null)
    {
        ArrayStart start = WriteHeaderStart(MessageType.Signal, MessageFlags.None);

        // Path.
        if (path is not null)
        {
            WriteStructureStart();
            WriteByte((byte)MessageHeader.Path);
            WriteVariantObjectPath(path);
        }

        // Interface.
        if (@interface is not null)
        {
            WriteStructureStart();
            WriteByte((byte)MessageHeader.Interface);
            WriteVariantString(@interface);
        }

        // Member.
        if (member is not null)
        {
            WriteStructureStart();
            WriteByte((byte)MessageHeader.Member);
            WriteVariantString(member);
        }

        // Destination.
        if (destination is not null)
        {
            WriteStructureStart();
            WriteByte((byte)MessageHeader.Destination);
            WriteVariantString(destination);
        }

        // Signature.
        if (signature is not null)
        {
            WriteStructureStart();
            WriteByte((byte)MessageHeader.Signature);
            WriteVariantSignature(signature);
        }

        WriteHeaderEnd(ref start);
    }

    private void WriteHeaderEnd(ref ArrayStart start)
    {
        WriteArrayEnd(ref start);
        WritePadding(8);
    }

    private ArrayStart WriteHeaderStart(MessageType type, MessageFlags flags)
    {
        WriteByte(BitConverter.IsLittleEndian ? (byte)'l' : (byte)'B'); // endianness
        WriteByte((byte)type);
        WriteByte((byte)flags);
        WriteByte((byte)1); // version
        WriteUInt32((uint)0); // length placeholder
        Debug.Assert(_offset == Message.LengthOffset + 4);
        WriteUInt32((uint)0); // serial placeholder
        Debug.Assert(_offset == Message.SerialOffset + 4);

        // headers
        ArrayStart start = WriteArrayStart(alignment: 8); // structs have 8-byte alignment.

        // UnixFds
        WriteStructureStart();
        WriteByte((byte)MessageHeader.UnixFds);
        WriteVariantUInt32(0); // unix fd length placeholder
        Debug.Assert(_offset == Message.UnixFdLengthOffset + 4);
        return start;
    }

    // public void Write<T>(T value)
    // {
    //     GeneratedWriters.Instance.GetWriter<T>().Write(ref this, value);
    // }

    public void WriteBool(bool value) => WriteUInt32(value ? 1u : 0u);

    public void WriteByte(byte value) => WritePrimitiveCore<Int16>(value, alignment: 1);

    public void WriteInt16(Int16 value) => WritePrimitiveCore<Int16>(value, alignment: 2);

    public void WriteUInt16(UInt16 value) => WritePrimitiveCore<UInt16>(value, alignment: 2);

    public void WriteInt32(Int32 value) => WritePrimitiveCore<Int32>(value, alignment: 4);

    public void WriteUInt32(UInt32 value) => WritePrimitiveCore<UInt32>(value, alignment: 4);

    public void WriteInt64(Int64 value) => WritePrimitiveCore<Int64>(value, alignment: 8);

    public void WriteUInt64(UInt64 value) => WritePrimitiveCore<UInt64>(value, alignment: 8);

    public void WriteDouble(double value) => WritePrimitiveCore<double>(value, alignment: 8);

    public void WriteString(StringSpan value) => WriteStringCore(value.Span);

    public void WriteString(string value) => WriteStringCore(value);

    public void WriteSignature(StringSpan value)
    {
        ReadOnlySpan<byte> span = value.Span;
        int length = span.Length;
        WriteByte((byte)length);
        var dst = GetSpan(length);
        span.CopyTo(dst);
        Advance(length);
        WriteByte((byte)0);
    }

    public void WriteSignature(string s)
    {
        Span<byte> lengthSpan = GetSpan(1);
        Advance(1);
        int bytesWritten = (int)Encoding.UTF8.GetBytes(s.AsSpan(), Writer);
        lengthSpan[0] = (byte)bytesWritten;
        _offset += bytesWritten;
        WriteByte(0);
    }

    public void WriteObjectPath(StringSpan value) => WriteStringCore(value.Span);

    public void WriteObjectPath(string value) => WriteStringCore(value);

    private void WriteStringCore(ReadOnlySpan<byte> span)
    {
        int length = span.Length;
        WriteUInt32((uint)length);
        var dst = GetSpan(length);
        span.CopyTo(dst);
        Advance(length);
        WriteByte((byte)0);
    }

    private void WriteStringCore(string s)
    {
        WritePadding(4);
        Span<byte> lengthSpan = GetSpan(4);
        Advance(4);
        int bytesWritten = (int)Encoding.UTF8.GetBytes(s.AsSpan(), Writer);
        Unsafe.WriteUnaligned<uint>(ref MemoryMarshal.GetReference(lengthSpan), (uint)bytesWritten);
        _offset += bytesWritten;
        WriteByte(0);
    }

    private void WritePrimitiveCore<T>(T value, int alignment)
    {
        WritePadding(alignment);
        var span = GetSpan(alignment);
        Unsafe.WriteUnaligned<T>(ref MemoryMarshal.GetReference(span), value);
        Advance(alignment);
    }

    public void WriteHandle(SafeHandle value)
    {
        throw new NotImplementedException();
    }

    // public void WriteVariant<T>(T value)
    // {
    //     var writer = GeneratedWriters.Instance.GetWriter<T>();
    //     Write(writer.Signature);
    //     writer.Write(ref this, value);
    // }

    public void WriteVariantBool(bool value)
    {
        WriteSignature(ProtocolConstants.BooleanSignature);
        WriteBool(value);
    }

    public void WriteVariantByte(byte value)
    {
        WriteSignature(ProtocolConstants.ByteSignature);
        WriteByte(value);
    }

    public void WriteVariantInt16(Int16 value)
    {
        WriteSignature(ProtocolConstants.Int16Signature);
        WriteInt16(value);
    }

    public void WriteVariantUInt16(UInt16 value)
    {
        WriteSignature(ProtocolConstants.UInt16Signature);
        WriteUInt16(value);
    }

    public void WriteVariantInt32(Int32 value)
    {
        WriteSignature(ProtocolConstants.Int32Signature);
        WriteInt32(value);
    }

    public void WriteVariantUInt32(UInt32 value)
    {
        WriteSignature(ProtocolConstants.UInt32Signature);
        WriteUInt32(value);
    }

    public void WriteVariantInt64(Int64 value)
    {
        WriteSignature(ProtocolConstants.Int64Signature);
        WriteInt64(value);
    }

    public void WriteVariantUInt64(UInt64 value)
    {
        WriteSignature(ProtocolConstants.UInt64Signature);
        WriteUInt64(value);
    }

    public void WriteVariantDouble(double value)
    {
        WriteSignature(ProtocolConstants.DoubleSignature);
        WriteDouble(value);
    }

    public void WriteVariantString(StringSpan value)
    {
        WriteSignature(ProtocolConstants.StringSignature);
        WriteString(value);
    }

    public void WriteVariantSignature(StringSpan value)
    {
        WriteSignature(ProtocolConstants.SignatureSignature);
        WriteSignature(value);
    }

    public void WriteVariantObjectPath(StringSpan value)
    {
        WriteSignature(ProtocolConstants.ObjectPathSignature);
        WriteObjectPath(value);
    }

    public void WriteVariantString(string value)
    {
        WriteSignature(ProtocolConstants.StringSignature);
        WriteString(value);
    }

    public void WriteVariantSignature(string value)
    {
        WriteSignature(ProtocolConstants.SignatureSignature);
        WriteSignature(value);
    }

    public void WriteVariantObjectPath(string value)
    {
        WriteSignature(ProtocolConstants.ObjectPathSignature);
        WriteObjectPath(value);
    }

    public ArrayStart WriteArrayStart(int alignment)
    {
        // Array length.
        WritePadding(4);
        Span<byte> lengthSpan = GetSpan(4);
        Advance(4);

        WritePadding(alignment);

        return new ArrayStart(lengthSpan, _offset);
    }

    public void WriteArrayEnd(ref ArrayStart start) // TODO?: remove 'ref'
    {
        start.WriteLength(_offset);
    }

    // public void WriteArray<T>(ICollection<T> elements)
    // {
    //     var writer = GeneratedWriters.Instance.GetWriter<T>();
    //     ArrayStart start = WriteArrayStart(writer.Alignment);

    //     foreach (var element in elements)
    //     {
    //         writer.Write(ref this, element);
    //     }

    //     WriteArrayEnd(ref start);
    // }

    public void WriteStructureStart()
    {
        WritePadding(8);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Advance(int count)
    {
        _buffered += count;
        _offset += count;
        _span = _span.Slice(count);
    }

    private void WritePadding(int alignment)
    {
        int pad = _offset % alignment;
        if (pad != 0)
        {
            pad = alignment - pad;
            GetSpan(pad).Slice(0, pad).Fill(0);
            Advance(pad);
        }
    }

    private Span<byte> GetSpan(int sizeHint)
    {
        Ensure(sizeHint);
        return _span;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Ensure(int count = 1)
    {
        if (_span.Length < count)
        {
            EnsureMore(count);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void EnsureMore(int count = 0)
    {
        if (_buffered > 0)
        {
            Flush();
        }

        _span = _message.GetSpan(count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Flush()
    {
        var buffered = _buffered;
        if (buffered > 0)
        {
            _buffered = 0;
            _message.Advance(buffered);
            _span = default;
        }
    }
}

public ref struct ArrayStart
{
    private Span<byte> _span;
    private int _offset;

    internal ArrayStart(Span<byte> lengthSpan, int offset)
    {
        _span = lengthSpan;
        _offset = offset;
    }

    internal void WriteLength(int offset)
    {
        if (_span.IsEmpty)
        {
            return;
        }
        uint length = (uint)(offset - _offset);
        Unsafe.WriteUnaligned<uint>(ref MemoryMarshal.GetReference(_span), length);
        _span = default;
    }
}

// class GeneratedWriters
// {
//     public static readonly GeneratedWriters Instance = new GeneratedWriters();

//     public IValueWriter<T> GetWriter<T>()
//     {
//         object writer = new IntWriter();
//         return (IValueWriter<T>)writer;
//     }
// }

// sealed class IntWriter : IValueWriter<int>
// {
//     private static ReadOnlySpan<byte> s_signature => new byte[] { (byte)'i' };
//     public int Alignment => 4;
//     public Utf8Span Signature => new Utf8Span(s_signature);

//     public void Write(ref MessageWriter writer, int value)
//     {
//         throw new NotImplementedException();
//     }
// }

// interface IValueWriter<T>
// {
//     void Write(ref MessageWriter writer, T value);
//     int Alignment { get; }
//     Utf8Span Signature { get; }
// }
