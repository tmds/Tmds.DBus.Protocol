using System.Reflection;

namespace Tmds.DBus.Protocol;

public ref struct Reader
{
    private delegate object ValueReader(ref Reader reader);

    private readonly bool _isBigEndian;
    private UnixFdCollection? _handles;
    private SequenceReader<byte> _reader;

    internal ReadOnlySequence<byte> UnreadSequence => _reader.UnreadSequence;

    internal void Advance(long count) => _reader.Advance(count);

    internal Reader(bool isBigEndian, ReadOnlySequence<byte> sequence, UnixFdCollection? handles)
    {
        _reader = new(sequence);

        _isBigEndian = isBigEndian;
        _handles = handles;
    }

    public byte ReadByte()
    {
        if (!_reader.TryRead(out byte b))
        {
            ThrowHelper.ThrowIndexOutOfRange();
        }
        return b;
    }

    public bool ReadBool()
    {
        return ReadInt32() != 0;
    }

    public UInt16 ReadUInt16()
        => (UInt16)ReadInt16();

    public Int16 ReadInt16()
    {
        AlignReader(DBusType.Int16);
        bool dataRead = _isBigEndian ? _reader.TryReadBigEndian(out Int16 rv) : _reader.TryReadLittleEndian(out rv);
        if (!dataRead)
        {
            ThrowHelper.ThrowIndexOutOfRange();
        }
        return rv;
    }

    public uint ReadUInt32()
        => (uint)ReadInt32();

    public int ReadInt32()
    {
        AlignReader(DBusType.Int32);
        bool dataRead = _isBigEndian ? _reader.TryReadBigEndian(out int rv) : _reader.TryReadLittleEndian(out rv);
        if (!dataRead)
        {
            ThrowHelper.ThrowIndexOutOfRange();
        }
        return rv;
    }

    public UInt64 ReadUInt64()
        => (UInt64)ReadInt64();

    public Int64 ReadInt64()
    {
        AlignReader(DBusType.Int64);
        bool dataRead = _isBigEndian ? _reader.TryReadBigEndian(out Int64 rv) : _reader.TryReadLittleEndian(out rv);
        if (!dataRead)
        {
            ThrowHelper.ThrowIndexOutOfRange();
        }
        return rv;
    }

    public unsafe double ReadDouble()
    {
        double value;
        *(Int64*)&value = ReadInt64();
        return value;
    }

    public T[] ReadArray<T>()
    {
        List<T> items = new();
        ArrayEnd headersEnd = ReadArrayStart(TypeModel.GetTypeAlignment<T>());
        while (HasNext(headersEnd))
        {
            items.Add(Read<T>());
        }
        return items.ToArray();
    }

    private static object ReadArrayCore<TElement>(ref Reader reader)
    {
        return reader.ReadArray<TElement>();
    }

    private object ReadArrayTyped(Type elementType)
    {
        var method = typeof(Reader).GetMethod(nameof(ReadArrayCore), BindingFlags.Static | BindingFlags.NonPublic)
            .MakeGenericMethod(new[] { elementType });
        var dlg = method!.CreateDelegate<ValueReader>();
        return dlg.Invoke(ref this);
    }

    public Dictionary<TKey, TValue> ReadDictionary<TKey, TValue>() where TKey : notnull
    {
        Dictionary<TKey, TValue> dictionary = new();
        ArrayEnd headersEnd = ReadArrayStart(DBusType.Struct);
        while (HasNext(headersEnd))
        {
            var key = Read<TKey>();
            var value = Read<TValue>();
            dictionary.Add(key, value);
        }
        return dictionary;
    }

    private static object ReadDictionaryCore<TKey, TValue>(ref Reader reader)
    {
        return reader.ReadDictionary<TKey, TValue>();
    }

    private object ReadDictionaryTyped(Type keyType, Type valueType)
    {
        var method = typeof(Reader).GetMethod(nameof(ReadDictionaryCore), BindingFlags.Static | BindingFlags.NonPublic)
            .MakeGenericMethod(new[] { keyType, valueType });
        var dlg = method!.CreateDelegate<ValueReader>();
        return dlg.Invoke(ref this);
    }

    public ValueTuple<T1> ReadStruct<T1>()
    {
        return new ValueTuple<T1>(Read<T1>());;
    }

    private static object ReadValueTuple1Core<T1>(ref Reader reader)
    {
        return reader.ReadStruct<T1>();
    }

    private object ReadValueTuple1Typed(Type t1Type)
    {
        var method = typeof(Reader).GetMethod(nameof(ReadValueTuple1Core), BindingFlags.Static | BindingFlags.NonPublic)
            .MakeGenericMethod(new[] { t1Type });
        var dlg = method!.CreateDelegate<ValueReader>();
        return dlg.Invoke(ref this);
    }

    public ValueTuple<T1, T2> ReadStruct<T1, T2>()
    {
        return new ValueTuple<T1, T2>(Read<T1>(), Read<T2>());;
    }

    private static object ReadValueTuple2Core<T1, T2>(ref Reader reader)
    {
        return reader.ReadStruct<T1, T2>();
    }

    private object ReadValueTuple2Typed(Type t1Type, Type t2Type)
    {
        var method = typeof(Reader).GetMethod(nameof(ReadValueTuple2Core), BindingFlags.Static | BindingFlags.NonPublic)
            .MakeGenericMethod(new[] { t1Type, t2Type });
        var dlg = method!.CreateDelegate<ValueReader>();
        return dlg.Invoke(ref this);
    }

    private static object ReadTuple1Core<T1>(ref Reader reader)
    {
        return new Tuple<T1>(reader.Read<T1>());
    }

    private object ReadTuple1Typed(Type t1Type)
    {
        var method = typeof(Reader).GetMethod(nameof(ReadTuple1Core), BindingFlags.Static | BindingFlags.NonPublic)
            .MakeGenericMethod(new[] { t1Type });
        var dlg = method!.CreateDelegate<ValueReader>();
        return dlg.Invoke(ref this);
    }

    private static object ReadTuple2Core<T1, T2>(ref Reader reader)
    {
        return new Tuple<T1, T2>(reader.Read<T1>(), reader.Read<T2>());
    }

    private object ReadTuple2Typed(Type t1Type, Type t2Type)
    {
        var method = typeof(Reader).GetMethod(nameof(ReadTuple2Core), BindingFlags.Static | BindingFlags.NonPublic)
            .MakeGenericMethod(new[] { t1Type, t2Type });
        var dlg = method!.CreateDelegate<ValueReader>();
        return dlg.Invoke(ref this);
    }

    private static object ReadHandleCore<T>(ref Reader reader)
    {
        return reader.ReadHandle<T>();
    }

    private object ReadHandleTyped(Type handleType)
    {
        var method = typeof(Reader).GetMethod(nameof(ReadHandleCore), BindingFlags.Static | BindingFlags.NonPublic)
            .MakeGenericMethod(new[] { handleType });
        var dlg = method!.CreateDelegate<ValueReader>();
        return dlg.Invoke(ref this);
    }

    public object ReadVariant() => Read<object>();

    private T Read<T>()
    {
        Type type = typeof(T);

        if (type == typeof(object))
        {
            Utf8Span signature = ReadSignature();
            type = TypeModel.DetermineVariantType(signature);
        }

        if (type == typeof(byte))
        {
            return (T)(object)ReadByte();
        }
        else if (type == typeof(bool))
        {
            return (T)(object)ReadBool();
        }
        else if (type == typeof(Int16))
        {
            return (T)(object)ReadInt16();
        }
        else if (type == typeof(UInt16))
        {
            return (T)(object)ReadUInt16();
        }
        else if (type == typeof(Int32))
        {
            return (T)(object)ReadInt32();
        }
        else if (type == typeof(UInt32))
        {
            return (T)(object)ReadUInt32();
        }
        else if (type == typeof(Int64))
        {
            return (T)(object)ReadInt64();
        }
        else if (type == typeof(UInt64))
        {
            return (T)(object)ReadUInt64();
        }
        else if (type == typeof(Double))
        {
            return (T)(object)ReadDouble();
        }
        else if (type == typeof(string))
        {
            return (T)(object)ReadString().ToString();
        }
        else if (type.IsAssignableTo(typeof(SafeHandle)))
        {
            return (T)ReadHandleTyped(type);
        }
        else
        {
            if (type.IsArray)
            {
                var rank = type.GetArrayRank();
                if (rank == 1)
                {
                    return (T)ReadArrayTyped(type.GetElementType()!);
                }
            }
            else if (type.IsGenericType && type.FullName!.StartsWith("System.ValueTuple"))
            {
                switch (type.GenericTypeArguments.Length)
                {
                    case 1:
                        return (T)ReadValueTuple1Typed(type.GenericTypeArguments[0]);
                    case 2:
                        return (T)ReadValueTuple2Typed(type.GenericTypeArguments[0], type.GenericTypeArguments[1]);
                }
            }
            else if (type.IsGenericType && type.FullName!.StartsWith("System.Tuple"))
            {
                switch (type.GenericTypeArguments.Length)
                {
                    case 1:
                        return (T)ReadTuple1Typed(type.GenericTypeArguments[0]);
                    case 2:
                        return (T)ReadTuple2Typed(type.GenericTypeArguments[0], type.GenericTypeArguments[1]);
                }
            }
            else
            {
                Type? extractedType = TypeModel.ExtractGenericInterface(type, typeof(IDictionary<,>));
                if (extractedType != null)
                {
                    return (T)ReadDictionaryTyped(extractedType.GenericTypeArguments[0], extractedType.GenericTypeArguments[1]);
                }
            }
        }
        ThrowHelper.ThrowReadingTypeNotSupported(type);
        return default;
    }

    public T ReadHandle<T>()
    {
        return default; // TODO
    }

    public IntPtr ReadHandle(bool own)
    {
        int idx = (int)ReadUInt32();
        IntPtr handle = (IntPtr)(-1);
        if (_handles is not null)
        {
            (handle, bool dispose) = _handles[idx];
            if (own)
            {
                _handles[idx] = (handle, false);
            }
        }
        return handle;
    }

    public Utf8Span ReadSignature()
    {
        int length = ReadByte();
        return ReadSpan(length);
    }

    public Utf8Span ReadObjectPath() => ReadSpan();

    public Utf8Span ReadString() => ReadSpan();

    public string ReadStringAsString() => Encoding.UTF8.GetString(ReadString());

    private ReadOnlySpan<byte> ReadSpan()
    {
        int length = (int)ReadUInt32();
        return ReadSpan(length);
    }

     private ReadOnlySpan<byte> ReadSpan(int length)
     {
        var span = _reader.UnreadSpan;
        if (span.Length <= length)
        {
            _reader.Advance(length + 1);
            return span.Slice(length);
        }
        else
        {
            var buffer = new byte[length];
            if (!_reader.TryCopyTo(buffer))
            {
                ThrowHelper.ThrowIndexOutOfRange();
            }
            _reader.Advance(length + 1);
            return new ReadOnlySpan<byte>(buffer);
        }
     }

    public void AlignReader(DBusType type)
    {
        long pad = ProtocolConstants.GetPadding((int)_reader.Consumed, type);
        if (pad != 0)
        {
            _reader.Advance(pad);
        }
    }

    public ArrayEnd ReadArrayStart(DBusType elementType)
    {
        uint arrayLength = ReadUInt32();
        AlignReader(elementType);
        int endOfArray = (int)(_reader.Consumed + arrayLength);
        return new ArrayEnd(elementType, endOfArray);
    }

    public bool HasNext(ArrayEnd iterator)
    {
        int consumed = (int)_reader.Consumed;
        int nextElement = ProtocolConstants.Align(consumed, iterator.Type);
        if (nextElement >= iterator.EndOfArray)
        {
            return false;
        }
        int advance = nextElement - consumed;
        if (advance != 0)
        {
            _reader.Advance(advance);
        }
        return true;
    }
}

public ref struct ArrayEnd
{
    internal readonly DBusType Type;
    internal readonly int EndOfArray;

    internal ArrayEnd(DBusType type, int endOfArray)
    {
        Type = type;
        EndOfArray = endOfArray;
    }
}