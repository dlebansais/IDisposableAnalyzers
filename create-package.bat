nuget install Packager -DependencyVersion Highest -OutputDirectory packages
packages\Packager.2.1.0\lib\net481\Packager.exe --prefix:dlebansais
nuget pack nuget\dlebansais.IDisposableAnalyzers.nuspec  
pause