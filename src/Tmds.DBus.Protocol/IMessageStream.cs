namespace Tmds.DBus.Protocol;

public interface IMessageStream
{
    public delegate void MessageReceivedHandler<T>(Exception? closeReason, ref MessageReader reader, T state);

    void ReceiveMessages<T>(MessageReceivedHandler<T> handler, T state);

    ValueTask<bool> TrySendMessageAsync(Message message);

    void Close(Exception closeReason);
}