namespace Tmds.DBus.Protocol;

public interface IMessageStream
{
    public delegate void MessageReceivedHandler<T>(Exception? closeReason, ref Message message, T state);

    void ReceiveMessages<T>(MessageReceivedHandler<T> handler, T state);

    ValueTask<bool> TrySendMessageAsync(MessageBuffer message);

    void Close(Exception closeReason);
}