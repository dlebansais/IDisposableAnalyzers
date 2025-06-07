namespace AddonTest;

internal sealed class D : IDisposable
{
    public void Init(C input)
    {
        class1 = input;
    }

    private bool disposedValue;
    private C? class1;

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
