namespace IDisposableAnalyzers;

using System;
using System.IO;
using System.Reflection;
using System.Threading;

internal static class AssemblyLoading
{
    private static int lockCount;

    /// <summary>
    /// Manually loads the System.Text.Json assembly from the package folder if the current assembly is executing in the context of VBCSCompiler.
    /// Loads it once to avoid multiple loads in a single process.
    /// </summary>
    internal static void LoadAssembliesManually()
    {
        if (Interlocked.CompareExchange(ref lockCount, 1, 0) == 0)
        {
            Assembly executingAssembly = Assembly.GetExecutingAssembly();
            if (Path.GetDirectoryName(executingAssembly.Location) is string executingFolder && executingAssembly.GetName().Version is Version version)
            {
                string[] files =
                [
#if DEBUG_LOGGING
                    "IDisposableAnalyzers.Attributes.dll",
                    "dlebansais.Method.Contracts.dll",
                    "Microsoft.Bcl.AsyncInterfaces.dll",
                    "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
                    "Microsoft.Extensions.Logging.Abstractions.dll",
                    "System.Buffers.dll",
                    "System.Diagnostics.DiagnosticSource.dll",
                    "System.Memory.dll",
                    "System.Numerics.Vectors.dll",
                    "System.Runtime.CompilerServices.Unsafe.dll",
                    "System.Text.Encodings.Web.dll",
                    "System.Threading.Tasks.Extensions.dll",
                    "ProcessCommunication.dll",
                    "DebugLogging.dll",
#endif
                    "System.Text.Json.dll",
                ];

                foreach (var file in files)
                {
                    LoadAssembly(executingFolder, version, file);
                }
            }
        }
    }

    private static void LoadAssembly(string executingFolder, Version version, string assemblyFileName)
    {
        string executingFile = Path.Combine(executingFolder, assemblyFileName);
        if (!File.Exists(executingFile))
        {
            string userFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string path = $@"{userFolder}\.nuget\packages\dlebansais.idisposableanalyzers\{version}\analyzers\dotnet\cs\{assemblyFileName}";

            if (File.Exists(path))
            {
                _ = Assembly.LoadFrom(path);
            }
        }
    }
}
