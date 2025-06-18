namespace IDisposableAnalyzers;

using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

internal static class Json
{
    private static int lockCount;

    /// <summary>
    /// Loads the System.Text.Json assembly if the current assembly is being compiled by VBCSCompiler.
    /// Loads it once to avoid multiple loads in a single process.
    /// </summary>
    internal static void LoadJsonAssembly()
    {
        Assembly executingAssembly = Assembly.GetExecutingAssembly();
        if (executingAssembly.Location.Contains("VBCSCompiler") &&
            Interlocked.CompareExchange(ref lockCount, 1, 0) == 0)
        {
#if DEBUG
            string configuration = "Debug";
#else
            string configuration = "Release";
#endif

            // We rely on the location of this source file to find the assembly path since we're running in the context of a compilation.
            string rootFolder = GetRootFolder();
            string path = Path.Combine(rootFolder, $@"bin\{configuration}\netstandard2.0\System.Text.Json.dll");
            _ = Assembly.LoadFrom(path);
        }
    }

    private static string GetRootFolder([CallerFilePath] string callerFilePath = "")
        => Path.GetDirectoryName(Path.GetDirectoryName(callerFilePath))!;
}
