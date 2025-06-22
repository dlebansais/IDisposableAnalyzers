#pragma warning disable CA1812

using System.Buffers;

sealed class Manager : MemoryManager<byte>
{
    /// <inheritdoc />
    public override void Unpin() { }

    /// <inheritdoc />
    public override unsafe MemoryHandle Pin(int elementIndex = 0) => default;

    /// <inheritdoc />
    public override unsafe Span<byte> GetSpan() => default;

    /// <inheritdoc />
    protected override void Dispose(bool disposing) { } // <-- lint occurs here
}
