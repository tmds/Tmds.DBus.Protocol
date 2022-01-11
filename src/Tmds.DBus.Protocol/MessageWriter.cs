using System.Reflection;

namespace Tmds.DBus.Protocol;

public ref struct MessageWriter
{
    private delegate void ValueWriter(ref MessageWriter writer, object value);

    private const int LengthOffset = 4;
    private const int SerialOffset = 8;
    private const int HeaderFieldsLengthOffset = 12;
    private const int UnixFdLengthOffset = 20;

    private readonly MessageBuffer _message;
    private readonly uint _serial;
    private Span<byte> _firstSpan;
    private Span<byte> _span;
    private int _offset;
    private int _buffered;

    public MessageBuffer CreateMessage()
    {
        Flush();

        CompleteMessage();

        return _message;
    }

    private void CompleteMessage()
    {
        Span<byte> span = _firstSpan;

        // Length
        uint headerFieldsLength = Unsafe.ReadUnaligned<uint>(ref MemoryMarshal.GetReference(span.Slice(HeaderFieldsLengthOffset)));
        uint pad = headerFieldsLength % 8;
        if (pad != 0)
        {
            headerFieldsLength += (8 - pad);
        }
        uint length = (uint)_message.Length             // Total length
                      - headerFieldsLength               // Header fields
                      - 4                                // Header fields length
                      - (uint)HeaderFieldsLengthOffset;  // Preceeding header fields
        Unsafe.WriteUnaligned<uint>(ref MemoryMarshal.GetReference(span.Slice(LengthOffset)), length);

        // UnixFdLength
        Unsafe.WriteUnaligned<uint>(ref MemoryMarshal.GetReference(span.Slice(UnixFdLengthOffset)), (uint)_message.HandleCount);

        _message.Serial = _serial;
    }

    private IBufferWriter<byte> Writer
    {
        get
        {
            Flush();
            return _message.Writer;
        }
    }

    internal MessageWriter(MessageBuffer message, uint serial)
    {
        _message = message;
        _offset = 0;
        _buffered = 0;
        _serial = serial;
        _firstSpan = _span = _message.GetSpan(sizeHint: 0);
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
        Utf8Span destination = default,
        string? signature = null)
    {
        ArrayStart start = WriteHeaderStart(MessageType.MethodReturn, MessageFlags.None);

       // ReplySerial
        WriteStructureStart();
        WriteByte((byte)MessageHeader.ReplySerial);
        WriteVariantUInt32(replySerial);

        // Destination.
        if (!destination.IsEmpty)
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

    public void WriteError(
        uint replySerial,
        Utf8Span destination = default,
        string? errorName = null,
        string? errorMsg = null)
    {
        ArrayStart start = WriteHeaderStart(MessageType.Error, MessageFlags.None);

       // ReplySerial
        WriteStructureStart();
        WriteByte((byte)MessageHeader.ReplySerial);
        WriteVariantUInt32(replySerial);

        // Destination.
        if (!destination.IsEmpty)
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
        if (errorMsg is not null)
        {
            WriteStructureStart();
            WriteByte((byte)MessageHeader.Signature);
            WriteVariantSignature(ProtocolConstants.StringSignature);
        }

        WriteHeaderEnd(ref start);

        if (errorMsg is not null)
        {
            WriteString(errorMsg);
        }
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
        WritePadding(DBusType.Struct);
    }

    private ArrayStart WriteHeaderStart(MessageType type, MessageFlags flags)
    {
        WriteByte(BitConverter.IsLittleEndian ? (byte)'l' : (byte)'B'); // endianness
        WriteByte((byte)type);
        WriteByte((byte)flags);
        WriteByte((byte)1); // version
        WriteUInt32((uint)0); // length placeholder
        Debug.Assert(_offset == LengthOffset + 4);
        WriteUInt32(_serial);
        Debug.Assert(_offset == SerialOffset + 4);

        // headers
        ArrayStart start = WriteArrayStart(DBusType.Struct);

        // UnixFds
        WriteStructureStart();
        WriteByte((byte)MessageHeader.UnixFds);
        WriteVariantUInt32(0); // unix fd length placeholder
        Debug.Assert(_offset == UnixFdLengthOffset + 4);
        return start;
    }

    // public void Write<T>(T value)
    // {
    //     GeneratedWriters.Instance.GetWriter<T>().Write(ref this, value);
    // }

    public void WriteBool(bool value) => WriteUInt32(value ? 1u : 0u);

    public void WriteByte(byte value) => WritePrimitiveCore<Int16>(value, DBusType.Byte);

    public void WriteInt16(Int16 value) => WritePrimitiveCore<Int16>(value, DBusType.Int16);

    public void WriteUInt16(UInt16 value) => WritePrimitiveCore<UInt16>(value, DBusType.UInt16);

    public void WriteInt32(Int32 value) => WritePrimitiveCore<Int32>(value, DBusType.Int32);

    public void WriteUInt32(UInt32 value) => WritePrimitiveCore<UInt32>(value, DBusType.UInt32);

    public void WriteInt64(Int64 value) => WritePrimitiveCore<Int64>(value, DBusType.Int64);

    public void WriteUInt64(UInt64 value) => WritePrimitiveCore<UInt64>(value, DBusType.UInt64);

    public void WriteDouble(double value) => WritePrimitiveCore<double>(value, DBusType.Double);

    public void WriteString(Utf8Span value) => WriteStringCore(value);

    public void WriteString(string value) => WriteStringCore(value);

    public void WriteSignature(Utf8Span value)
    {
        ReadOnlySpan<byte> span = value;
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

    public void WriteObjectPath(Utf8Span value) => WriteStringCore(value);

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
        WritePadding(DBusType.UInt32);
        Span<byte> lengthSpan = GetSpan(4);
        Advance(4);
        int bytesWritten = (int)Encoding.UTF8.GetBytes(s.AsSpan(), Writer);
        Unsafe.WriteUnaligned<uint>(ref MemoryMarshal.GetReference(lengthSpan), (uint)bytesWritten);
        _offset += bytesWritten;
        WriteByte(0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WritePrimitiveCore<T>(T value, DBusType type)
    {
        WritePadding(type);
        int length = ProtocolConstants.GetFixedTypeLength(type);
        var span = GetSpan(length);
        Unsafe.WriteUnaligned<T>(ref MemoryMarshal.GetReference(span), value);
        Advance(length);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDictionary<TKey, TValue>(IDictionary<TKey, TValue> value)
    {
        ArrayStart arrayStart = WriteArrayStart(DBusType.Struct);
        foreach (var item in value)
        {
            WriteStructureStart();
            Write<TKey>(item.Key);
            Write<TValue>(item.Value);
        }
        WriteArrayEnd(ref arrayStart);
    }

    private static void WriteDictionaryCore<TKey, TValue>(ref MessageWriter writer, object o)
    {
        writer.WriteDictionary<TKey, TValue>((IDictionary<TKey, TValue>)o);
    }

    private void WriteDictionaryTyped(Type keyType, Type valueType, object o)
    {
        var method = typeof(MessageWriter).GetMethod(nameof(WriteDictionaryCore), BindingFlags.Static | BindingFlags.NonPublic)
            .MakeGenericMethod(new[] { keyType, valueType });
        var dlg = method!.CreateDelegate<ValueWriter>();
        dlg.Invoke(ref this, o);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteArray<T>(T[] value)
    {
        ArrayStart arrayStart = WriteArrayStart(GetTypeAlignment<T>());
        foreach (T item in value)
        {
            Write<T>(item);
        }
        WriteArrayEnd(ref arrayStart);
    }

    private static void WriteArrayCore<TElement>(ref MessageWriter writer, object o)
    {
        writer.WriteArray<TElement>((TElement[])o);
    }

    private void WriteArrayTyped(Type elementType, object o)
    {
        var method = typeof(MessageWriter).GetMethod(nameof(WriteArrayCore), BindingFlags.Static | BindingFlags.NonPublic)
            .MakeGenericMethod(new[] { elementType });
        var dlg = method!.CreateDelegate<ValueWriter>();
        dlg.Invoke(ref this, o);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteStruct<T1>(T1 item1)
    {
        WriteStructureStart();
        Write<T1>(item1);
    }

    private static void WriteValueTuple1Core<T1>(ref MessageWriter writer, object o)
    {
        var value = (ValueTuple<T1>)o;
        writer.WriteStruct(value.Item1);
    }

    private void WriteValueTuple1Typed(Type t1Type, object o)
    {
        var method = typeof(MessageWriter).GetMethod(nameof(WriteValueTuple1Core), BindingFlags.Static | BindingFlags.NonPublic)
            .MakeGenericMethod(new[] { t1Type });
        var dlg = method!.CreateDelegate<ValueWriter>();
        dlg.Invoke(ref this, o);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteStruct<T1, T2>(T1 item1, T2 item2)
    {
        WriteStructureStart();
        Write<T1>(item1);
        Write<T2>(item2);
    }

    private static void WriteValueTuple2Core<T1, T2>(ref MessageWriter writer, object o)
    {
        var value = (ValueTuple<T1, T2>)o;
        writer.WriteStruct(value.Item1, value.Item2);
    }

    private void WriteValueTuple2Typed(Type t1Type, Type t2Type, object o)
    {
        var method = typeof(MessageWriter).GetMethod(nameof(WriteValueTuple2Core), BindingFlags.Static | BindingFlags.NonPublic)
            .MakeGenericMethod(new[] { t1Type, t2Type });
        var dlg = method!.CreateDelegate<ValueWriter>();
        dlg.Invoke(ref this, o);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Write<T>(T value)
    {
        Type type = typeof(T);

        if (type == typeof(object))
        {
            type = value!.GetType();

            // Variant: write signature.
            WriteSignatureTyped(type);
        }

        if (type == typeof(byte))
        {
            WriteByte((byte)(object)value);
            return;
        }
        else if (type == typeof(bool))
        {
            WriteBool((bool)(object)value);
            return;
        }
        else if (type == typeof(Int16))
        {
            WriteInt16((Int16)(object)value);
            return;
        }
        else if (type == typeof(UInt16))
        {
            WriteUInt16((UInt16)(object)value);
            return;
        }
        else if (type == typeof(Int32))
        {
            WriteInt32((Int32)(object)value);
            return;
        }
        else if (type == typeof(UInt32))
        {
            WriteUInt32((UInt32)(object)value);
            return;
        }
        else if (type == typeof(Int64))
        {
            WriteInt64((Int64)(object)value);
            return;
        }
        else if (type == typeof(UInt64))
        {
            WriteUInt64((UInt64)(object)value);
            return;
        }
        else if (type == typeof(Double))
        {
            WriteDouble((double)(object)value);
            return;
        }
        else if (type == typeof(String))
        {
            WriteString((string)(object)value);
            return;
        }
        else if (type == typeof(ObjectPath))
        {
            WriteString(((ObjectPath)(object)value).ToString());
            return;
        }
        else if (type == typeof(Signature))
        {
            WriteString(((Signature)(object)value).ToString());
            return;
        }
        if (type.IsAssignableTo(typeof(SafeHandle)))
        {
            WriteHandle((SafeHandle)(object)value);
            return;
        }
        else
        {
            if (type.IsArray)
            {
                var rank = type.GetArrayRank();
                if (rank == 1)
                {
                    WriteArrayTyped(type.GetElementType()!, (object)value!);
                    return;
                }
            }
            else if (type.IsGenericType && type.FullName!.StartsWith("System.ValueTuple"))
            {
                switch (type.GenericTypeArguments.Length)
                {
                    case 1:
                        WriteValueTuple1Typed(type.GenericTypeArguments[0], (object)value);
                        return;
                    case 2:
                        WriteValueTuple2Typed(type.GenericTypeArguments[0], type.GenericTypeArguments[1], (object)value);
                        return;
                }
            }
            else
            {
                Type? extractedType = ExtractGenericInterface(type, typeof(IDictionary<,>));
                if (extractedType != null)
                {
                    WriteDictionaryTyped(extractedType.GenericTypeArguments[0], extractedType.GenericTypeArguments[1], (object)value!);
                    return;
                }
            }
        }
        ThrowNotSupportedType(type);
    }

    private static void ThrowNotSupportedType(Type type)
    {
        throw new NotSupportedException($"Cannot write type {type.FullName}");
    }

    private static Type? ExtractGenericInterface(Type queryType, Type interfaceType)
    {
        if (IsGenericInstantiation(queryType, interfaceType))
        {
            return queryType;
        }

        return GetGenericInstantiation(queryType, interfaceType);
    }

    private static bool IsGenericInstantiation(Type candidate, Type interfaceType)
    {
        return
            candidate.IsGenericType &&
            candidate.GetGenericTypeDefinition() == interfaceType;
    }

    private static Type? GetGenericInstantiation(Type queryType, Type interfaceType)
    {
        Type? bestMatch = null;
        var interfaces = queryType.GetInterfaces();
        foreach (var @interface in interfaces)
        {
            if (IsGenericInstantiation(@interface, interfaceType))
            {
                if (bestMatch == null)
                {
                    bestMatch = @interface;
                }
                else if (StringComparer.Ordinal.Compare(@interface.FullName, bestMatch.FullName) < 0)
                {
                    bestMatch = @interface;
                }
            }
        }

        if (bestMatch != null)
        {
            return bestMatch;
        }

        var baseType = queryType?.BaseType;
        if (baseType == null)
        {
            return null;
        }
        else
        {
            return GetGenericInstantiation(baseType, interfaceType);
        }
    }

    private void WriteSignatureTyped(Type type)
    {
        Span<byte> signature = stackalloc byte[256];
        int bytesWritten = WriteSignatureTyped(type, signature);
        WriteSignature(MemoryMarshal.CreateReadOnlySpan(ref MemoryMarshal.GetReference(signature), bytesWritten));
    }

    private static int WriteSignatureTyped(Type type, Span<byte> signature)
    {
        Type? extractedType;
        if (type == typeof(object))
        {
            signature[0] = (byte)DBusType.Variant;
            return 1;
        }
        else if (type == typeof(byte))
        {
            signature[0] = (byte)DBusType.Byte;
            return 1;
        }
        else if (type == typeof(bool))
        {
            signature[0] = (byte)DBusType.Bool;
            return 1;
        }
        else if (type == typeof(Int16))
        {
            signature[0] = (byte)DBusType.Int16;
            return 1;
        }
        else if (type == typeof(UInt16))
        {
            signature[0] = (byte)DBusType.UInt16;
            return 1;
        }
        else if (type == typeof(Int32))
        {
            signature[0] = (byte)DBusType.Int32;
            return 1;
        }
        else if (type == typeof(UInt32))
        {
            signature[0] = (byte)DBusType.UInt32;
            return 1;
        }
        else if (type == typeof(Int64))
        {
            signature[0] = (byte)DBusType.Int64;
            return 1;
        }
        else if (type == typeof(UInt64))
        {
            signature[0] = (byte)DBusType.UInt64;
            return 1;
        }
        else if (type == typeof(Double))
        {
            signature[0] = (byte)DBusType.Double;
            return 1;
        }
        else if (type == typeof(String))
        {
            signature[0] = (byte)DBusType.String;
            return 1;
        }
        else if (type == typeof(ObjectPath))
        {
            signature[0] = (byte)DBusType.ObjectPath;
            return 1;
        }
        else if (type == typeof(Signature))
        {
            signature[0] = (byte)DBusType.Signature;
            return 1;
        }
        else if (type.IsArray)
        {
            int bytesWritten = 0;
            signature[bytesWritten++] = (byte)DBusType.Array;
            bytesWritten += WriteSignatureTyped(type.GetElementType()!, signature.Slice(bytesWritten));
            return bytesWritten;
        }
        else if (type.FullName!.StartsWith("System.ValueTuple"))
        {
            int bytesWritten = 0;
            signature[bytesWritten++] = (byte)'(';
            foreach (var itemType in type.GenericTypeArguments)
            {
                bytesWritten += WriteSignatureTyped(itemType, signature.Slice(bytesWritten));
            }
            signature[bytesWritten++] = (byte)')';
            return bytesWritten;
        }
        else if ((extractedType = ExtractGenericInterface(type, typeof(IDictionary<,>))) != null)
        {
            int bytesWritten = 0;
            signature[bytesWritten++] = (byte)'a';
            signature[bytesWritten++] = (byte)'{';
            bytesWritten += WriteSignatureTyped(extractedType.GenericTypeArguments[0], signature.Slice(bytesWritten));
            bytesWritten += WriteSignatureTyped(extractedType.GenericTypeArguments[1], signature.Slice(bytesWritten));
            signature[bytesWritten++] = (byte)'}';
            return bytesWritten;
        }
        else if (type.IsAssignableTo(typeof(SafeHandle)))
        {
            signature[0] = (byte)DBusType.UnixFd;
            return 1;
        }
        return 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static DBusType GetTypeAlignment<T>()
    {
        if (typeof(T) == typeof(object))
        {
            return DBusType.Variant;
        }
        else if (typeof(T) == typeof(byte))
        {
            return DBusType.Byte;
        }
        else if (typeof(T) == typeof(bool))
        {
            return DBusType.Bool;
        }
        else if (typeof(T) == typeof(Int16))
        {
            return DBusType.Int16;
        }
        else if (typeof(T) == typeof(UInt16))
        {
            return DBusType.UInt16;
        }
        else if (typeof(T) == typeof(Int32))
        {
            return DBusType.Int32;
        }
        else if (typeof(T) == typeof(UInt32))
        {
            return DBusType.UInt32;
        }
        else if (typeof(T) == typeof(Int64))
        {
            return DBusType.Int64;
        }
        else if (typeof(T) == typeof(UInt64))
        {
            return DBusType.UInt64;
        }
        else if (typeof(T) == typeof(Double))
        {
            return DBusType.Double;
        }
        else if (typeof(T) == typeof(String))
        {
            return DBusType.String;
        }
        else if (typeof(T) == typeof(ObjectPath))
        {
            return DBusType.ObjectPath;
        }
        else if (typeof(T) == typeof(Signature))
        {
            return DBusType.Signature;
        }
        else if (typeof(T).IsArray)
        {
            return DBusType.Array;
        }
        else if (ExtractGenericInterface(typeof(T), typeof(IDictionary<,>)) != null)
        {
            return DBusType.Array;
        }
        else if (typeof(T).IsAssignableTo(typeof(SafeHandle)))
        {
            return DBusType.UnixFd;
        }
        return DBusType.Struct;
    }

    public void WriteVariant(object o)
    {
        Write<object>(o);
    }

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

    public void WriteVariantString(Utf8Span value)
    {
        WriteSignature(ProtocolConstants.StringSignature);
        WriteString(value);
    }

    public void WriteVariantSignature(Utf8Span value)
    {
        WriteSignature(ProtocolConstants.SignatureSignature);
        WriteSignature(value);
    }

    public void WriteVariantObjectPath(Utf8Span value)
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

    public ArrayStart WriteArrayStart(DBusType elementType)
    {
        // Array length.
        WritePadding(DBusType.UInt32);
        Span<byte> lengthSpan = GetSpan(4);
        Advance(4);

        WritePadding(elementType);

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
        WritePadding(DBusType.Struct);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Advance(int count)
    {
        _buffered += count;
        _offset += count;
        _span = _span.Slice(count);
    }

    private void WritePadding(DBusType type)
    {
        int pad = ProtocolConstants.GetPadding(_offset, type);
        if (pad != 0)
        {
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
    private void Flush()
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
