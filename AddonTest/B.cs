namespace AddonTest;

internal sealed class B(A input) : IDisposable
{
    private bool disposedValue;
    private A class1 = input;

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                class1?.Dispose();
            }

            disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
    }
}
