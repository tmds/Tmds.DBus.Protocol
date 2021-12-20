namespace Tmds.DBus.Protocol;

public ref struct StringSpan
{
    public ReadOnlySpan<byte> Span { get; }

    public StringSpan(ReadOnlySpan<byte> span) => Span = span;

    public bool IsEmpty => Span.IsEmpty;

    public override string ToString() => Encoding.UTF8.GetString(Span);
}