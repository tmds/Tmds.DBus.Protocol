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

    public static ReadOnlySpan<byte> ByteSignature => (ReadOnlySpan<byte>)s_byteSignature;
    public static ReadOnlySpan<byte> BooleanSignature => (ReadOnlySpan<byte>)s_booleanSignature;
    public static ReadOnlySpan<byte> Int16Signature => (ReadOnlySpan<byte>)s_int16Signature;
    public static ReadOnlySpan<byte> UInt16Signature => (ReadOnlySpan<byte>)s_uint16Signature;
    public static ReadOnlySpan<byte> Int32Signature => (ReadOnlySpan<byte>)s_int32Signature;
    public static ReadOnlySpan<byte> UInt32Signature => (ReadOnlySpan<byte>)s_uint32Signature;
    public static ReadOnlySpan<byte> Int64Signature => (ReadOnlySpan<byte>)s_int64Signature;
    public static ReadOnlySpan<byte> UInt64Signature => (ReadOnlySpan<byte>)s_uint64Signature;
    public static ReadOnlySpan<byte> DoubleSignature => (ReadOnlySpan<byte>)s_doubleSignature;
    public static ReadOnlySpan<byte> UnixFdSignature => (ReadOnlySpan<byte>)s_unixFdSignature;
    public static ReadOnlySpan<byte> StringSignature => (ReadOnlySpan<byte>)s_stringSignature;
    public static ReadOnlySpan<byte> ObjectPathSignature => (ReadOnlySpan<byte>)s_objectpathSignature;
    public static ReadOnlySpan<byte> SignatureSignature => (ReadOnlySpan<byte>)s_signatureSignature;

    public static int GetFirstTypeAlignment(ReadOnlySpan<byte> signature)
    {
        if (signature.IsEmpty)
        {
            return 1;
        }
        return GetTypeAlignment((DBusType)signature[0]);
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