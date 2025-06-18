namespace IDisposableAnalyzers;

using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

internal static class Json
{
    private static int lockCount;

    /// <summary>
    /// Manually loads the System.Text.Json assembly from the package folder if the current assembly is executing in the context of VBCSCompiler.
    /// Loads it once to avoid multiple loads in a single process.
    /// </summary>
    internal static void LoadJsonAssembly()
    {
        if (Interlocked.CompareExchange(ref lockCount, 1, 0) == 0)
        {
            Assembly executingAssembly = Assembly.GetExecutingAssembly();
            if (Path.GetDirectoryName(executingAssembly.Location) is string executingFolder)
            {
                string executingFile = Path.Combine(executingFolder, "System.Text.Json.dll");
                if (!File.Exists(executingFile))
                {
                    AssemblyName assemblyName = executingAssembly.GetName();
                    if (assemblyName.Version is Version version)
                    {
                        string userFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        string path = $@"{userFolder}\.nuget\packages\dlebansais.idisposableanalyzers\{version}\analyzers\dotnet\cs\System.Text.Json.dll";
                        if (File.Exists(path))
                        {
                            _ = Assembly.LoadFrom(path);
                        }
                    }
                }
            }
        }
    }
}
