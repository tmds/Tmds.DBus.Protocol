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
    public StringSpan Path { get; }
    public StringSpan Interface { get; }
    public StringSpan Member { get; }
    public StringSpan Error { get; }
    public uint? ReplySerial { get; }
    public StringSpan Destination { get; }
    public StringSpan Sender { get; }
    public StringSpan Signature { get; }
    public int UnixFds { get; }

    public MessageReader CloneAndRewind()
    {
        MessageReader reader = this;
        reader.Rewind();
        return reader;
    }

    private void Rewind()
    {
        Span<byte> span = stackalloc byte[4];
        _reader.Sequence.Slice(Message.HeaderFieldsLengthOffset, 4).CopyTo(span);
        uint headerFieldsLength = _isBigEndian
                                    ? BinaryPrimitives.ReadUInt32BigEndian(span)
                                    : BinaryPrimitives.ReadUInt32LittleEndian(span);
        int bodyOffset = Message.HeaderFieldsLengthOffset + 4 + (int)headerFieldsLength;
        _reader.Rewind(bodyOffset - _reader.Consumed);
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

        ArrayEnd headersEnd = ReadArrayStart(alignment: 8);
        while (HasNext(headersEnd))
        {
            MessageHeader hdrType = (MessageHeader)ReadByte();
            StringSpan sig = ReadSignature();
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
        AlignReader(8);
    }

    public uint ReadUInt32()
    {
        AlignReader(4);
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

    public StringSpan ReadSignature() => ReadStringSpan();

    public StringSpan ReadObjectPath() => ReadStringSpan();

    public StringSpan ReadString() => ReadStringSpan();

    private StringSpan ReadStringSpan()
    {
        int length = (int)ReadUInt32();

        var span = _reader.UnreadSpan;
        if (span.Length <= length)
        {
            _reader.Advance(length + 1);
            return new StringSpan(span.Slice(length));
        }
        else
        {
            var buffer = new byte[length];
            if (!_reader.TryCopyTo(buffer))
            {
                throw new IndexOutOfRangeException();
            }
            _reader.Advance(length + 1);
            return new StringSpan(buffer);
        }
    }

    private void AlignReader(int alignment)
    {
        long pad = _reader.Consumed % alignment;
        if (pad != 0)
        {
            _reader.Advance(alignment - pad);
        }
    }

    public ArrayEnd ReadArrayStart(int alignment)
    {
        uint arrayLength = ReadUInt32();
        AlignReader(alignment);
        int endOfArray = (int)(_reader.Consumed + arrayLength);
        return new ArrayEnd(alignment, endOfArray);
    }

    public bool HasNext(ArrayEnd iterator)
    {
        int consumed = (int)_reader.Consumed;
        int advance = 0;
        int nextElement = consumed;
        int pad = consumed % iterator.Aligmnent;
        if (pad != 0)
        {
            advance = iterator.Aligmnent - pad;
            nextElement += advance;
        }
        if (nextElement >= iterator.EndOfArray)
        {
            return false;
        }
        if (advance != 0)
        {
            _reader.Advance(advance);
        }
        return true;
    }

    public static bool TryReadMessage(ReadOnlySequence<byte> sequence, out MessageReader reader, UnixFdCollection? handles = null)
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
    internal readonly int Aligmnent;
    internal readonly int EndOfArray;

    internal ArrayEnd(int alignment, int endOfArray)
    {
        Aligmnent = alignment;
        EndOfArray = endOfArray;
    }
}