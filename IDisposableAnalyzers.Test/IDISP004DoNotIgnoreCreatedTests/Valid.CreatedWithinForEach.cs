namespace IDisposableAnalyzers.Test.IDISP004DoNotIgnoreCreatedTests;

using Gu.Roslyn.Asserts;
using NUnit.Framework;

public static partial class Valid
{
    [Test]
    public static void StructEnumeratorCreatedWithinForEach()
    {
        var code = @"
using System.Collections;
using System.Collections.Generic;
namespace N
{
    public class C
    {
        public void M()
        {
            foreach(var n in new Enumerator())
            {
            }
        }
    }
    public struct Enumerator : IEnumerator<int>, IEnumerable<int>
    {
        public int Current => 42;
        object IEnumerator.Current => 42;
        public void Dispose() { }
        public bool MoveNext() => true;
        public void Reset() { }
        public IEnumerator<int> GetEnumerator() => this;
        IEnumerator IEnumerable.GetEnumerator() => this;
    }
}";
        RoslynAssert.Valid(Analyzer, code);
    }

    [Test]
    public static void ClassEnumeratorCreatedWithinForEach()
    {
        var code = @"
using System.Collections;
using System.Collections.Generic;
namespace N
{
    public class C
    {
        public void M()
        {
            foreach(var n in new Enumerator())
            {
            }
        }
    }
    public class Enumerator : IEnumerator<int>, IEnumerable<int>
    {
        public int Current => 42;
        object IEnumerator.Current => 42;
        public void Dispose() { }
        public bool MoveNext() => true;
        public void Reset() { }
        public IEnumerator<int> GetEnumerator() => this;
        IEnumerator IEnumerable.GetEnumerator() => this;
    }
}";
        RoslynAssert.Valid(Analyzer, code);
    }

    [Test]
    public static void CreatedEnumerableWithinForEach()
    {
        var code = @"
using System.Collections;
using System.Collections.Generic;
namespace N
{
    public class C
    {
        public void M()
        {
            foreach(var n in All())
            {
            }
        }
        public IEnumerable<int> All() => new Enumerator();
    }
    public class Enumerator : IEnumerator<int>, IEnumerable<int>
    {
        public int Current => 42;
        object IEnumerator.Current => 42;
        public void Dispose() { }
        public bool MoveNext() => true;
        public void Reset() { }
        public IEnumerator<int> GetEnumerator() => this;
        IEnumerator IEnumerable.GetEnumerator() => this;
    }
}";
        RoslynAssert.Valid(Analyzer, code);
    }
}
