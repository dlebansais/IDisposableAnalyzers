namespace AddonTest;

using IDisposableAnalyzers;

internal sealed class F([AcquireOwnership] E e1, [AcquireOwnership] E e2) : IDisposable
{
    private E _e1 = e1;
    private E _e2 = e2;
    private bool _isDisposed;

    private void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                _e1?.Dispose();
                _e2?.Dispose();
            }

            _isDisposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
    }
}
