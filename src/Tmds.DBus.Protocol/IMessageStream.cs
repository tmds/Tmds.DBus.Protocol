namespace Tmds.DBus.Protocol;

public interface IMessageStream : IDisposable // TODO: make internal
{
    public delegate void MessageReceivedHandler<T>(Exception? exception, ref MessageReader reader, T state);

    void ReceiveMessages<T>(MessageReceivedHandler<T> handler, T state);
    ValueTask SendMessageAsync(Message message);
}