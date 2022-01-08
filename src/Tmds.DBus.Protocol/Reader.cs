namespace Tmds.DBus.Protocol;

public ref struct Reader
{
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

    public uint ReadUInt32()
    {
        AlignReader(DBusType.UInt32);
        bool dataRead = _isBigEndian ? _reader.TryReadBigEndian(out int rv) : _reader.TryReadLittleEndian(out rv);
        if (!dataRead)
        {
            throw new IndexOutOfRangeException();
        }
        return (uint)rv;
    }

    public int ReadInt32()
    {
        AlignReader(DBusType.UInt32);
        bool dataRead = _isBigEndian ? _reader.TryReadBigEndian(out int rv) : _reader.TryReadLittleEndian(out rv);
        if (!dataRead)
        {
            throw new IndexOutOfRangeException();
        }
        return rv;
    }

    public byte ReadByte()
    {
        if (!_reader.TryRead(out byte b))
        {
            throw new IndexOutOfRangeException();
        }
        return b;
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
                throw new IndexOutOfRangeException();
            }
            _reader.Advance(length + 1);
            return new ReadOnlySpan<byte>(buffer);
        }
    }
    public Utf8Span ReadObjectPath() => ReadSpan();

    public Utf8Span ReadString() => ReadSpan();

    public string ReadStringAsString() => Encoding.UTF8.GetString(ReadString());

    private ReadOnlySpan<byte> ReadSpan()
    {
        int length = (int)ReadUInt32();

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
                throw new IndexOutOfRangeException();
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