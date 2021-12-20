namespace Tmds.DBus.Protocol;

static class ProtocolConstants
{
    private static ReadOnlySpan<byte> s_byteSignature => new byte[] { (byte)'y' };
    private static ReadOnlySpan<byte> s_booleanSignature => new byte[] { (byte)'b' };
    private static ReadOnlySpan<byte> s_int16Signature => new byte[] { (byte)'n' };
    private static ReadOnlySpan<byte> s_uint16Signature => new byte[] { (byte)'q' };
    private static ReadOnlySpan<byte> s_int32Signature => new byte[] { (byte)'i' };
    private static ReadOnlySpan<byte> s_uint32Signature => new byte[] { (byte)'u' };
    private static ReadOnlySpan<byte> s_int64Signature => new byte[] { (byte)'x' };
    private static ReadOnlySpan<byte> s_uint64Signature => new byte[] { (byte)'t' };
    private static ReadOnlySpan<byte> s_doubleSignature => new byte[] { (byte)'d' };
    private static ReadOnlySpan<byte> s_unixFdSignature => new byte[] { (byte)'h' };
    private static ReadOnlySpan<byte> s_stringSignature => new byte[] { (byte)'s' };
    private static ReadOnlySpan<byte> s_objectpathSignature => new byte[] { (byte)'o' };
    private static ReadOnlySpan<byte> s_signatureSignature => new byte[] { (byte)'g' };

    public static StringSpan ByteSignature => new StringSpan(s_byteSignature);
    public static StringSpan BooleanSignature => new StringSpan(s_booleanSignature);
    public static StringSpan Int16Signature => new StringSpan(s_int16Signature);
    public static StringSpan UInt16Signature => new StringSpan(s_uint16Signature);
    public static StringSpan Int32Signature => new StringSpan(s_int32Signature);
    public static StringSpan UInt32Signature => new StringSpan(s_uint32Signature);
    public static StringSpan Int64Signature => new StringSpan(s_int64Signature);
    public static StringSpan UInt64Signature => new StringSpan(s_uint64Signature);
    public static StringSpan DoubleSignature => new StringSpan(s_doubleSignature);
    public static StringSpan UnixFdSignature => new StringSpan(s_unixFdSignature);
    public static StringSpan StringSignature => new StringSpan(s_stringSignature);
    public static StringSpan ObjectPathSignature => new StringSpan(s_objectpathSignature);
    public static StringSpan SignatureSignature => new StringSpan(s_signatureSignature);

    public static int GetFirstTypeAlignment(StringSpan signature)
    {
        if (signature.IsEmpty)
        {
            return 1;
        }
        return GetTypeAlignment((DBusType)signature.Span[0]);
    }

    public static int GetTypeAlignment(DBusType type)
    {
        switch (type)
        {
            case DBusType.Byte: return 1;
            case DBusType.Bool: return 4;
            case DBusType.Int16: return 2;
            case DBusType.UInt16: return 2;
            case DBusType.Int32: return 4;
            case DBusType.UInt32: return 4;
            case DBusType.Int64: return 8;
            case DBusType.UInt64: return 8;
            case DBusType.Double: return 8;
            case DBusType.String: return 4;
            case DBusType.ObjectPath: return 4;
            case DBusType.Signature: return 4;
            case DBusType.Array: return 4;
            case DBusType.Struct: return 8;
            case DBusType.Variant: return 1;
            case DBusType.DictEntry: return 8;
            case DBusType.UnixFd: return 4;
            default: return 1;
        }
    }
}