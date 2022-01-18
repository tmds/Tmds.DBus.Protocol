// using System.Reflection;

// namespace Tmds.DBus.Protocol;

// public ref partial struct MessageWriter
// {
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     public void WriteStruct<T1>(T1 item1)
//     {
//         WriteStructureStart();
//         Write<T1>(item1);
//     }

//     private static void WriteValueTuple1Core<T1>(ref MessageWriter writer, object o)
//     {
//         var value = (ValueTuple<T1>)o;
//         writer.WriteStruct(value.Item1);
//     }

//     private void WriteValueTuple1Typed(Type t1Type, object o)
//     {
//         if (RuntimeFeature.IsDynamicCodeSupported)
//         {
//             var method = typeof(MessageWriter).GetMethod(nameof(WriteValueTuple1Core), BindingFlags.Static | BindingFlags.NonPublic)
//                 .MakeGenericMethod(new[] { t1Type });
//             var dlg = method!.CreateDelegate<ValueWriter>();
//             dlg.Invoke(ref this, o);
//         }
//         else
//         {
//             var tuple = (ITuple)o;
//             WriteStructureStart();
//             Write(tuple[0], asVariant: t1Type == typeof(object));
//         }
//     }

//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     public void WriteStruct<T1, T2>(T1 item1, T2 item2)
//     {
//         WriteStructureStart();
//         Write<T1>(item1);
//         Write<T2>(item2);
//     }

//     private static void WriteValueTuple2Core<T1, T2>(ref MessageWriter writer, object o)
//     {
//         var value = (ValueTuple<T1, T2>)o;
//         writer.WriteStruct(value.Item1, value.Item2);
//     }

//     private void WriteValueTuple2Typed(Type t1Type, Type t2Type, object o)
//     {
//         if (RuntimeFeature.IsDynamicCodeSupported)
//         {
//             var method = typeof(MessageWriter).GetMethod(nameof(WriteValueTuple2Core), BindingFlags.Static | BindingFlags.NonPublic)
//                 .MakeGenericMethod(new[] { t1Type, t2Type });
//             var dlg = method!.CreateDelegate<ValueWriter>();
//             dlg.Invoke(ref this, o);
//         }
//         else
//         {
//             var tuple = (ITuple)o;
//             WriteStructureStart();
//             Write(tuple[0], asVariant: t1Type == typeof(object));
//             Write(tuple[1], asVariant: t1Type == typeof(object));
//         }
//     }
// }
