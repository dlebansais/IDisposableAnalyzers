namespace IDisposableAnalyzers;

using System;

/// <summary>
/// Represents an attribute indicating that a parameter of a disposable type acquires ownership of the provided value.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class AcquireOwnershipAttribute : Attribute
{
}
