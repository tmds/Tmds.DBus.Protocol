// using System.Reflection;

// namespace Tmds.DBus.Protocol;

// public ref partial struct MessageWriter
// {
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     public void WriteArray<T>(T[] value)
//     {
//         ArrayStart arrayStart = WriteArrayStart(TypeMarshalling.GetTypeAlignment<T>());
//         foreach (T item in value)
//         {
//             Write<T>(item);
//         }
//         WriteArrayEnd(ref arrayStart);
//     }

//     private static void WriteArrayCore<TElement>(ref MessageWriter writer, object o)
//     {
//         writer.WriteArray<TElement>((TElement[])o);
//     }

//     private void WriteArrayTyped(Type elementType, object o)
//     {
//         if (RuntimeFeature.IsDynamicCodeSupported)
//         {
//             var method = typeof(MessageWriter).GetMethod(nameof(WriteArrayCore), BindingFlags.Static | BindingFlags.NonPublic)
//                 .MakeGenericMethod(new[] { elementType });
//             var dlg = method!.CreateDelegate<ValueWriter>();
//             dlg.Invoke(ref this, o);
//         }
//         else
//         {
//             Array array = (Array)o;
//             ArrayStart arrayStart = WriteArrayStart(TypeMarshalling.GetTypeAlignment(elementType));
//             foreach (var item in array)
//             {
//                 Write(item, asVariant: elementType == typeof(object));
//             }
//             WriteArrayEnd(ref arrayStart);
//         }
//     }


    // public void WriteArray<T>(ICollection<T> elements)
    // {
    //     var writer = GeneratedWriters.Instance.GetWriter<T>();
    //     ArrayStart start = WriteArrayStart(writer.Alignment);

    //     foreach (var element in elements)
    //     {
    //         writer.Write(ref this, element);
    //     }

    //     WriteArrayEnd(ref start);
    // }
// }
