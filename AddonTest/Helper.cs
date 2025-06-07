namespace AddonTest;

#pragma warning disable CA1515 // Consider making public types internal
public static class Helper
#pragma warning restore CA1515 // Consider making public types internal
{
    public static IDisposable CreateB()
    {
#pragma warning disable CA2000 // Dispose objects before losing scope
        return new B(new A());
#pragma warning restore CA2000 // Dispose objects before losing scope
    }

    public static IDisposable CreateD()
    {
#pragma warning disable CA2000 // Dispose objects before losing scope
        D instance = new();
        instance.Init(new C());
#pragma warning restore CA2000 // Dispose objects before losing scope

        return instance;
    }
}
