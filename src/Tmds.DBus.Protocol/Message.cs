namespace Tmds.DBus.Protocol;

public class Message
{
    internal const int LengthOffset = 4;
    internal const int SerialOffset = 8;
    internal const int HeaderFieldsLengthOffset = 12;
    internal const int UnixFdLengthOffset = 28;

    private Sequence<byte> _sequence;
    private ArraySegment<byte> _firstSpan;
    private List<SafeHandle>? _handles;

    internal Message(Sequence<byte> sequence)
    {
        _sequence = sequence;
    }

    internal void Reset()
    {
        _sequence.Reset();
        _firstSpan = default;
    }

    internal Span<byte> GetSpan(int sizeHint)
    {
        var memory = _sequence.GetMemory(sizeHint);
        if (_firstSpan.Count == 0)
        {
            bool arrayInitialized = MemoryMarshal.TryGetArray(memory, out _firstSpan);
            Debug.Assert(arrayInitialized);
        }
        return memory.Span;
    }

    internal void Advance(int count)
    {
        _sequence.Advance(count);
    }

    public MessageWriter GetWriter() => new MessageWriter(this);

    public void WriteSerial(uint serial)
    {
        Span<byte> span = _firstSpan;
        Unsafe.WriteUnaligned<uint>(ref MemoryMarshal.GetReference(span.Slice(SerialOffset)), serial);
    }

    private void CompleteMessage()
    {
        Span<byte> span = _firstSpan;

        // Length
        uint headerFieldsLength = Unsafe.ReadUnaligned<uint>(ref MemoryMarshal.GetReference(span.Slice(HeaderFieldsLengthOffset)));
        uint length = (uint)_sequence.Length             // Total length
                      - headerFieldsLength               // Header fields
                      - 4                                // Header fields length
                      - (uint)HeaderFieldsLengthOffset;  // Preceeding header fields
        Unsafe.WriteUnaligned<uint>(ref MemoryMarshal.GetReference(span.Slice(LengthOffset)), length);

        // UnixFdLength
        Unsafe.WriteUnaligned<uint>(ref MemoryMarshal.GetReference(span.Slice(UnixFdLengthOffset)), _handles == null ? 0 : (uint)_handles.Count);
    }

    public ReadOnlySequence<byte> AsReadOnlySequence()
    {
        CompleteMessage();

        return _sequence.AsReadOnlySequence;
    }
}