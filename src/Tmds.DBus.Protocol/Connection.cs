// delegate void MethodReturnHandler<T>(Exception? exception, ref MessageReader reader, T state);

// class Connection : IDisposable
// {
//     public void CallMethod<T>(Message message, MethodReturnHandler<T> returnHandler, T state)
//     {
//         try
//         {
//             // TODO: send the message

//             // When the response is received:
//             MessageReader reader = default;
//             returnHandler(null, ref reader, state);
//         }
//         catch (Exception e)
//         {
//             MessageReader reader = default;
//             returnHandler(e, ref reader, state);
//         }
//     }

//     public void Dispose()
//     {
        
//     }
// }