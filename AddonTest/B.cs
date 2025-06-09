namespace AddonTest;

internal sealed class B(A a, bool leaveOpen = false) : IDisposable
{
    private A _a = a;
    private bool _leaveOpen = leaveOpen;
    private bool _isDisposed;

    private void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                if (!_leaveOpen)
                {
                    _a?.Dispose();
                }
            }

            _isDisposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
    }
}
