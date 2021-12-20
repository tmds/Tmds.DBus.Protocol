namespace Tmds.DBus.Protocol;

public class MessagePool
{
    public static readonly MessagePool Shared = new MessagePool(Environment.ProcessorCount * 2);

    private const int MinimumSpanLength = 512;

    private readonly int _maxSize;
    private readonly Stack<Message> _pool = new Stack<Message>();

    private readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Create(80 * 1024, 100);

    internal MessagePool(int maxSize)
    {
        _maxSize = maxSize;
    }

    public Rental Rent()
    {
        lock (_pool)
        {
            if (_pool.Count > 0)
            {
                return new Rental(this, _pool.Pop());
            }
        }

        var sequence = new Sequence<byte>(_arrayPool) { MinimumSpanLength = MinimumSpanLength };

        return new Rental(this, new Message(sequence));
    }

    private void Return(Message value)
    {
        value.Reset();
        lock (_pool)
        {
            if (_pool.Count < _maxSize)
            {
                _pool.Push(value);
            }
        }
    }

    public struct Rental : IDisposable
    {
        private readonly MessagePool _owner;

        internal Rental(MessagePool owner, Message value)
        {
            _owner = owner;
            Message = value;
        }

        public Message Message { get; }

        public void Dispose()
        {
            _owner.Return(Message);
        }
    }
}
