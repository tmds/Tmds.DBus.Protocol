namespace Tmds.DBus.Protocol;

ref struct SignatureReader
{
    private ReadOnlySpan<byte> _signature;

    public StringSpan Signature => new StringSpan(_signature);

    public SignatureReader(StringSpan signature)
    {
        _signature = signature.Span;
    }

    public bool TryRead(out DBusType type, out SignatureReader subReader) // TODO: change to out ReadOnlySpan<byte>
    {
        subReader = default;

        if (_signature.IsEmpty)
        {
            type = DBusType.Invalid;
            return false;
        }

        type = ReadSingleType(_signature, out int length);

        if (length > 1)
        {
            switch (type)
            {
                case DBusType.Array:
                    subReader = new SignatureReader(new StringSpan(_signature.Slice(1, length - 1)));
                    break;
                case DBusType.Struct:
                case DBusType.DictEntry:
                    subReader = new SignatureReader(new StringSpan(_signature.Slice(1, length - 2)));
                    break;
            }
        }

        _signature = _signature.Slice(length);

        return true;
    }

    private static DBusType ReadSingleType(ReadOnlySpan<byte> signature, out int length)
    {
        length = 0;

        if (signature.IsEmpty)
        {
            return DBusType.Invalid;
        }

        DBusType type = (DBusType)signature[0];

        if (IsBasicType(type))
        {
            length = 1;
        }
        else if (type == DBusType.Variant)
        {
            length = 1;
        }
        else if (type == DBusType.Array)
        {
            if (ReadSingleType(signature.Slice(1), out int elementLength) != DBusType.Invalid)
            {
                type = DBusType.Array;
                length = elementLength + 1;
            }
            else
            {
                type = DBusType.Invalid;
            }
        }
        else if (type == DBusType.Struct)
        {
            length = signature.IndexOf((byte)')') + 1;
            if (length == 0)
            {
                type = DBusType.Invalid;
            }
        }
        else if (type == DBusType.DictEntry)
        {
            length = signature.IndexOf((byte)'}') + 1;
            if (length < 4 ||
                !IsBasicType((DBusType)signature[1]) ||
                ReadSingleType(signature.Slice(2), out int valueTypeLength) == DBusType.Invalid ||
                length != valueTypeLength + 3)
            {
                type = DBusType.Invalid;
            }
        }
        else
        {
            type = DBusType.Invalid;
        }

        return type;
    }

    private static bool IsBasicType(DBusType type)
    {
        return BasicTypes.IndexOf((byte)type) != -1;
    }

    private static ReadOnlySpan<byte> BasicTypes => new byte[] {
        (byte)DBusType.Byte,
        (byte)DBusType.Bool,
        (byte)DBusType.Int16,
        (byte)DBusType.UInt16,
        (byte)DBusType.Int32,
        (byte)DBusType.UInt32,
        (byte)DBusType.Int64,
        (byte)DBusType.UInt64,
        (byte)DBusType.Double,
        (byte)DBusType.String,
        (byte)DBusType.ObjectPath,
        (byte)DBusType.Signature,
        (byte)DBusType.UnixFd };
}