#pragma warning disable CA1515 // Consider making public types internal
#pragma warning disable CA2000 // Dispose objects before losing scope

namespace AddonTest;

public static class Helper
{
    public static IDisposable CreateB()
    {
        A a = new();
        B b = new(a);
        return b;
    }

    public static IDisposable CreateD()
    {
        C c = new();
        D d = new();
        d.Init(c);

        return d;
    }

    public static IDisposable CreateF()
    {
        E e1 = new();
        E e2 = new();
        F f = new(e1, e2);
        return f;
    }
}
