# IDISP007
## Don't dispose injected

| Topic    | Value
| :--      | :--
| Id       | IDISP007
| Severity | Warning
| Enabled  | True
| Category | IDisposableAnalyzers.Correctness
| Code     | [DisposeCallAnalyzer](https://github.com/DotNetAnalyzers/IDisposableAnalyzers/blob/master/IDisposableAnalyzers/Analyzers/DisposeCallAnalyzer.cs)
|          | [LocalDeclarationAnalyzer](https://github.com/DotNetAnalyzers/IDisposableAnalyzers/blob/master/IDisposableAnalyzers/Analyzers/LocalDeclarationAnalyzer.cs)
|          | [UsingStatementAnalyzer](https://github.com/DotNetAnalyzers/IDisposableAnalyzers/blob/master/IDisposableAnalyzers/Analyzers/UsingStatementAnalyzer.cs)

## Description

Don't dispose disposables you do not own.

## Motivation

Disposing `IDisposables` that you have not created and do not own can be a bug.

## How to fix violations

If you don't own the `IDisposable` instance, you should not dispose it.

However, sometimes you want to state that you own the `IDisposable` instance. This section explains the different ways you can do it in the specific case of a constructor parameter.

There are two possible situations: when you can modify the source code, and when you can't. The later case typically arises while using third-party libraries.

### When you can modify the source code

First solution: passing a new parameter called `leaveOpen` or `disposeHandler`. The class code should dispose of the provided instance only when `leaveOpen` is `false` or `disposeHandler` is `true`, respectively. See this [StreamReader constructor](https://learn.microsoft.com/en-us/dotnet/api/system.io.streamreader.-ctor?view=net-9.0#system-io-streamreader-ctor(system-io-stream-system-text-encoding-system-boolean-system-int32-system-boolean)) and this [HttpClient constructor](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.-ctor?view=net-9.0#system-net-http-httpclient-ctor(system-net-http-httpmessagehandler-system-boolean)) for examples of usage.

Second solution: add the `[IDisposableAnalyzers.AcquireOwnership]` attribute to the parameter for which the class is acquiring ownership.

Example:
```csharp
namespace Foo;

class A : IDisposable
{
    //...
}

class B([IDisposableAnalyzers.AcquireOwnership] A input) : IDisposable
{
    private bool disposedValue;
    private A a = input;

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                a.Dispose();
            }

            disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
    }
}
```

### When you cannot modify the source code

The analyzer can be configured to declare that ownership of an instance is transfered when providing a reference to the instance to a constructor. This configuration applies to every occurence of the constructor (if this is not desirable, the constructor should provide a mean to declare it in the first place).

First, create a file called `IDisposableAnalyzers.json` and fill it with the following content:
```json
{
  "ownership_transfer_options": [
    {
      "parameter": 0,
      "symbol": "Foo.B.B(Foo.A)",
      "type": "B",
      "namespace": "Foo",
      "assembly": "Foo"
    }
  ]
}
```

+ In `parameter` specify the index of the parameter that transfers ownership. 0 means the first parameter.
+ In `symbol` specify the constructor signature. A signature include the namespace for all types.
+ in `type` specify the type name. This will typically be the same as in the `symbol` value, but is necessary to avoid confusion when parsing strings.
+ In `namespace` specify the namespace containing the type.
+ In `assembly` specify the assembly name.

Add the following lines to your project to make the configuration file visible to the analyzer

```csproj
  <ItemGroup>
    <AdditionalFiles Include="IDisposableAnalyzer.json" />
  </ItemGroup>
```

The example above assumes the configuration file is in the same directory as the project file.

<!-- start generated config severity -->
## Configure severity

### Via ruleset file.

Configure the severity per project, for more info see [MSDN](https://msdn.microsoft.com/en-us/library/dd264949.aspx).

### Via #pragma directive.
```C#
#pragma warning disable IDISP007 // Don't dispose injected
Code violating the rule here
#pragma warning restore IDISP007 // Don't dispose injected
```

Or put this at the top of the file to disable all instances.
```C#
#pragma warning disable IDISP007 // Don't dispose injected
```

### Via attribute `[SuppressMessage]`.

```C#
[System.Diagnostics.CodeAnalysis.SuppressMessage("IDisposableAnalyzers.Correctness", 
    "IDISP007:Don't dispose injected", 
    Justification = "Reason...")]
```
<!-- end generated config severity -->