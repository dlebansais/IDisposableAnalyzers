namespace AddonTest;

internal sealed class A : IDisposable
{
    private Mutex _mutex = new Mutex();
    private bool _isDisposed;

    private void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                _mutex?.Dispose();
            }

            _isDisposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
    }
}
