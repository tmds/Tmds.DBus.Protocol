using System.Reflection;

namespace Tmds.DBus.Protocol;

public ref partial struct MessageWriter
{
    interface ITypeWriter
    {
        void WriteVariant(ref MessageWriter writer, object value);
    }

    interface ITypeWriter<in T> : ITypeWriter
    {
        void Write(ref MessageWriter writer, T value);
    }

    private static readonly Dictionary<Type, ITypeWriter> _typeWriters = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Write<T>(T value) where T : notnull
    {
        Type type = typeof(T);

        if (type == typeof(object))
        {
            WriteVariant((object)value);
            return;
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
        else if (type == typeof(string))
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
            WriteSignature(((Signature)(object)value).ToString());
            return;
        }
        else
        {
            var typeWriter = (ITypeWriter<T>)GetTypeWriter(type);
            typeWriter.Write(ref this, value);
        }
    }

    private ITypeWriter GetTypeWriter(Type type)
    {
        lock (_typeWriters)
        {
            if (_typeWriters.TryGetValue(type, out ITypeWriter? writer))
            {
                return writer;
            }
            writer = CreateWriterForType(type);
            _typeWriters.Add(type, writer);
            return writer;
        }
    }

    private ITypeWriter CreateWriterForType(Type type)
    {

        // else if (type.IsAssignableTo(typeof(SafeHandle)))
        // {
        //     WriteHandle((SafeHandle)(object)value);
        //     return;
        // }
        // if (type.IsArray)
        // {
        //     var rank = type.GetArrayRank();
        //     if (rank == 1)
        //     {
        //         WriteArrayTyped(type.GetElementType()!, (object)value!);
        //         return;
        //     }
        // }
        // else if (type.IsGenericType && type.FullName!.StartsWith("System.ValueTuple"))
        // {
        //     switch (type.GenericTypeArguments.Length)
        //     {
        //         case 1:
        //             WriteValueTuple1Typed(type.GenericTypeArguments[0], (object)value);
        //             return;
        //         case 2:
        //             WriteValueTuple2Typed(type.GenericTypeArguments[0], type.GenericTypeArguments[1], (object)value);
        //             return;
        //     }
        // }
        // else
        {
            Type? extractedType = TypeModel.ExtractGenericInterface(type, typeof(IDictionary<,>));
            if (extractedType != null)
            {
                if (_typeWriters.TryGetValue(extractedType, out ITypeWriter? writer))
                {
                    return writer;
                }
                Type keyType = extractedType.GenericTypeArguments[0];
                Type valueType = extractedType.GenericTypeArguments[1];
                return CreateDictionaryTypeWriter(keyType, valueType);
            }
        }
        ThrowNotSupportedType(type);
        return default!;
    }

    private static void ThrowNotSupportedType(Type type)
    {
        throw new NotSupportedException($"Cannot write type {type.FullName}");
    }

    // private void Write(object value, bool asVariant)
    // {

    // }
}
