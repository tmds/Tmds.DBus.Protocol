namespace Tmds.DBus.Protocol;

public ref struct MessageReader
{
    private readonly bool _isBigEndian;
    private UnixFdCollection? _handles;
    private SequenceReader<byte> _reader;

    public MessageType Type { get; }
    public MessageFlags Flags { get; }
    public uint Serial { get; }

    // Header Fields
    public ReadOnlySpan<byte> Path { get; }
    public ReadOnlySpan<byte> Interface { get; }
    public ReadOnlySpan<byte> Member { get; }
    public ReadOnlySpan<byte> Error { get; }
    public uint? ReplySerial { get; }
    public ReadOnlySpan<byte> Destination { get; }
    public ReadOnlySpan<byte> Sender { get; }
    public ReadOnlySpan<byte> Signature { get; }
    public int UnixFds { get; }

    public MessageReader CloneAndRewind()
    {
        MessageReader reader = this;
        reader.Rewind();
        return reader;
    }

    private void Rewind()
    {
        _reader = new SequenceReader<byte>(_reader.Sequence);
        _reader.Advance(Message.HeaderFieldsLengthOffset);
        uint headerFieldsLength = ReadUInt32();
        _reader.Advance(headerFieldsLength);
        AlignReader(DBusType.Struct);
    }

    private MessageReader(ReadOnlySequence<byte> sequence, bool isBigEndian, MessageType type, MessageFlags flags, uint serial, UnixFdCollection? handles)
    {
        _reader = new(sequence);
        _reader.Advance(Message.HeaderFieldsLengthOffset);

        _isBigEndian = isBigEndian;
        Type = type;
        Flags = flags;
        Serial = serial;
        _handles = handles;

        Path = default;
        Interface = default;
        Member = default;
        Error = default;
        ReplySerial = default;
        Destination = default;
        Sender = default;
        Signature = default;
        UnixFds = default;

        ArrayEnd headersEnd = ReadArrayStart(DBusType.Struct);
        while (HasNext(headersEnd))
        {
            MessageHeader hdrType = (MessageHeader)ReadByte();
            ReadOnlySpan<byte> sig = ReadSignature();
            switch (hdrType)
            {
                case MessageHeader.Path:
                    Path = ReadObjectPath();
                    break;
                case MessageHeader.Interface:
                    Interface = ReadString();
                    break;
                case MessageHeader.Member:
                    Member = ReadString();
                    break;
                case MessageHeader.ErrorName:
                    Error = ReadString();
                    break;
                case MessageHeader.ReplySerial:
                    ReplySerial = ReadUInt32();
                    break;
                case MessageHeader.Destination:
                    Destination = ReadString();
                    break;
                case MessageHeader.Sender:
                    Sender = ReadString();
                    break;
                case MessageHeader.Signature:
                    Signature = ReadSignature();
                    break;
                case MessageHeader.UnixFds:
                    UnixFds = (int)ReadUInt32();
                    // TODO: throw if handles contains less.
                    break;
                default:
                    throw new NotSupportedException();
            }
        }
        AlignReader(DBusType.Struct);
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
        if (_handles != null)
        {
            (handle, bool dispose) = _handles[idx];
            if (own)
            {
                _handles[idx] = (handle, false);
            }
        }
        return handle;
    }

    public ReadOnlySpan<byte> ReadSignature()
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
    public ReadOnlySpan<byte> ReadObjectPath() => ReadSpan();

    public ReadOnlySpan<byte> ReadString() => ReadSpan();

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

    private void AlignReader(DBusType type)
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

    public static bool TryReadMessage(ref ReadOnlySequence<byte> sequence, out MessageReader reader, UnixFdCollection? handles = null)
    {
        reader = default;

        SequenceReader<byte> seqReader = new(sequence);
        if (!seqReader.TryRead(out byte endianness) ||
            !seqReader.TryRead(out byte msgType) ||
            !seqReader.TryRead(out byte flags) ||
            !seqReader.TryRead(out byte version))
        {
            return false;
        }

        if (version != 1)
        {
            throw new NotSupportedException();
        }

        bool isBigEndian = endianness == 'B';

        if (!TryReadUInt32(ref seqReader, isBigEndian, out uint bodyLength) ||
            !TryReadUInt32(ref seqReader, isBigEndian, out uint serial) ||
            !TryReadUInt32(ref seqReader, isBigEndian, out uint headerFieldLength))
        {
            return false;
        }

        headerFieldLength = (uint)ProtocolConstants.Align((int)headerFieldLength, DBusType.Struct);

        long totalLength = seqReader.Consumed + headerFieldLength + bodyLength;

        if (sequence.Length < totalLength)
        {
            return false;
        }

        reader = new MessageReader(sequence.Slice(0, totalLength),
                                    isBigEndian,
                                    (MessageType)msgType,
                                    (MessageFlags)flags,
                                    serial,
                                    handles);

        sequence = sequence.Slice(totalLength);

        return true;

        static bool TryReadUInt32(ref SequenceReader<byte> seqReader, bool isBigEndian, out uint value)
        {
            int v;
            bool rv = (isBigEndian && seqReader.TryReadBigEndian(out v) || seqReader.TryReadLittleEndian(out v));
            value = (uint)v;
            return rv;
        }
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