using System.Diagnostics.CodeAnalysis;

static class ThrowHelper
{
    public static void ThrowIfDisposed([DoesNotReturnIf(true)] bool condition, object instance)
    {
        if (condition)
        {
            ThrowObjectDisposedException(instance);
        }
    }

    private static void ThrowObjectDisposedException(object instance)
    {
        throw new ObjectDisposedException(instance?.GetType().FullName);
    }
}