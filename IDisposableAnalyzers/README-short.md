# IDisposableAnalyzers
Roslyn analyzers for IDisposable

## Using IDisposableAnalyzers

The preferable way to use the analyzers is to add the nuget package [IDisposableAnalyzers](https://www.nuget.org/packages/IDisposableAnalyzers/)
to the project(s).

The severity of individual rules may be configured using [rule set files](https://msdn.microsoft.com/en-us/library/dd264996.aspx)
in Visual Studio 2015.

## Installation

IDisposableAnalyzers can be installed using:
- [Paket](https://fsprojects.github.io/Paket/) 
- NuGet command line
- NuGet Package Manager in Visual Studio.


**Install using the command line:**
```bash
paket add IDisposableAnalyzers --project <project>
```

or if you prefer NuGet
```bash
Install-Package IDisposableAnalyzers
```

## Updating

The ruleset editor does not handle changed IDs well, if things get out of sync you can try:

1) Close visual studio.
2) Edit the ProjectName.rulset file and remove the IDisposableAnalyzers element.
3) Start visual studio and add back the desired configuration.

Above is not ideal, sorry about this. Not sure if this is our bug.


## Current status

Early alpha, finds bugs in the code but there are also bugs in the analyzer. The analyzer will probably never be perfect as it is a pretty hard problem to solve but we can improve it one test case at the time.
Write issues for places where it should warn but does not or where it warns where there is no bug.
