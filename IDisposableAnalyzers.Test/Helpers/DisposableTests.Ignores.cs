﻿namespace IDisposableAnalyzers.Test.Helpers;

using System.Threading;
using Gu.Roslyn.Asserts;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

public static partial class DisposableTests
{
    public static class Ignores
    {
        [TestCase("File.OpenRead(fileName)")]
        [TestCase("true ? File.OpenRead(fileName) : (FileStream)null")]
        [TestCase("Tuple.Create(File.OpenRead(fileName), 1)")]
        [TestCase("new Tuple<FileStream, int>(File.OpenRead(fileName), 1)")]
        [TestCase("new List<FileStream> { File.OpenRead(fileName) }")]
        public static void AssignedToLocal(string statement)
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.IO;

                    public class C
                    {
                        public static void M(string fileName)
                        {
                            var value = File.OpenRead(fileName);
                        }
                    }
                }
                """.AssertReplace("File.OpenRead(fileName)", statement);

            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(fileName)");
            Assert.AreEqual(false, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [TestCase("string.Format(\"{0}\", File.OpenRead(fileName))")]
        public static void ArgumentPassedTo(string expression)
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.IO;

                    public static class C
                    {
                        public static object M() => string.Format("{0}", File.OpenRead(fileName));
                    }
                }
                """.AssertReplace("string.Format(\"{0}\", File.OpenRead(fileName))", expression);
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(fileName)");
            Assert.AreEqual(true, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [TestCase("disposable")]
        ////[TestCase("disposable ?? disposable")]
        ////[TestCase("true ? disposable : (IDisposable)null")]
        ////[TestCase("Tuple.Create(disposable, 1)")]
        ////[TestCase("(disposable, 1)")]
        ////[TestCase("new Tuple<IDisposable, int>(disposable, 1)")]
        ////[TestCase("new List<IDisposable> { disposable }")]
        ////[TestCase("new List<IDisposable>() { disposable }")]
        ////[TestCase("new List<IDisposable> { disposable, null }")]
        public static void ArgumentAssignedToTempLocal(string expression)
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.IO;

                    public class C
                    {
                        public static void M(string fileName)
                        {
                            var value = M(File.OpenRead(fileName));
                        }

                        public static int M(IDisposable disposable)
                        {
                            var temp = new List<IDisposable> { disposable };
                            return 1;
                        }
                    }
                }
                """.AssertReplace("new List<IDisposable> { disposable }", expression);
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(fileName)");
            Assert.AreEqual(true, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [TestCase("var temp = disposable")]
        [TestCase("var temp = true ? disposable : (IDisposable)null")]
        public static void ArgumentAssignedToTempLocalThatIsDisposed(string expression)
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.IO;

                    public class C
                    {
                        public static void M(string fileName)
                        {
                            var value = M(File.OpenRead(fileName));
                        }

                        public static int M(IDisposable disposable)
                        {
                            var temp = disposable;
                            temp.Dispose();
                            return 1;
                        }
                    }
                }
                """.AssertReplace("var temp = disposable", expression);
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(fileName)");
            Assert.AreEqual(false, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [TestCase("var temp = disposable")]
        [TestCase("var temp = true ? disposable : (IDisposable)null")]
        public static void ArgumentAssignedTempLocalInUsing(string expression)
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.IO;

                    public class C
                    {
                        public static void M(string fileName)
                        {
                            var value = M(File.OpenRead(fileName));
                        }

                        public static int M(IDisposable disposable)
                        {
                            using (var temp = disposable)
                            {
                            }

                            temp.Dispose();
                            return 1;
                        }
                    }
                }
                """.AssertReplace("var temp = disposable", expression);
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(fileName)");
            Assert.AreEqual(false, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [TestCase("File.OpenRead(fileName)")]
        [TestCase("Tuple.Create(File.OpenRead(fileName), 1)")]
        [TestCase("new Tuple<FileStream, int>(File.OpenRead(fileName), 1)")]
        [TestCase("new List<FileStream> { File.OpenRead(fileName) }")]
        [TestCase("new FileStream [] { File.OpenRead(fileName) }")]
        public static void AssignedToField(string statement)
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.IO;

                    public class C
                    {
                        private readonly object value;

                        public static void M(string fileName)
                        {
                            this.value = File.OpenRead(fileName);
                        }
                    }
                }
                """.AssertReplace("File.OpenRead(fileName)", statement);
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(fileName)");
            Assert.AreEqual(false, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [TestCase("Task.Run(() => File.OpenRead(fileName))")]
        [TestCase("Task.Run(() => File.OpenRead(fileName)).ConfigureAwait(false)")]
        [TestCase("Task.FromResult(File.OpenRead(fileName))")]
        [TestCase("Task.FromResult(File.OpenRead(fileName)).ConfigureAwait(false)")]
        public static void AssignedToFieldAsync(string expression)
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.IO;

                    public class C
                    {
                        private readonly object value;

                        public static async Task M(string fileName)
                        {
                            this.value = await Task.Run(() => File.OpenRead(fileName));
                        }
                    }
                }
                """.AssertReplace("Task.Run(() => File.OpenRead(fileName))", expression);
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(fileName)");
            Assert.AreEqual(false, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [TestCase("File.OpenRead(fileName)")]
        [TestCase("_ = File.OpenRead(fileName)")]
        [TestCase("var _ = File.OpenRead(fileName)")]
        [TestCase("M(File.OpenRead(fileName))")]
        [TestCase("new List<IDisposable> { File.OpenRead(fileName) }")]
        public static void Discarded(string expression)
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.IO;

                    public class C
                    {
                        public static void M(string fileName)
                        {
                            File.OpenRead(fileName);
                        }

                        public static void M(IDisposable) { }
                    }
                }
                """.AssertReplace("File.OpenRead(fileName)", expression);
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(fileName)");
            Assert.AreEqual(true, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [TestCase("new C(File.OpenRead(fileName))")]
        [TestCase("_ = new C(File.OpenRead(fileName))")]
        [TestCase("var _ = new C(File.OpenRead(fileName))")]
        [TestCase("M(new C(File.OpenRead(fileName)))")]
        [TestCase("new List<IDisposable> { File.OpenRead(fileName) }")]
        [TestCase("_ = new List<IDisposable> { File.OpenRead(fileName) }")]
        [TestCase("new List<C> { new C(File.OpenRead(fileName)) }")]
        [TestCase("_ = new List<C> { new C(File.OpenRead(fileName)) }")]
        public static void DiscardedWrapped(string expression)
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.Collections.Generic;
                    using System.IO;

                    public class C : IDisposable
                    {
                        private readonly IDisposable disposable;

                        public C(IDisposable disposable)
                        {
                            this.disposable = disposable;
                        }

                        public static void M(string fileName)
                        {
                            new C(File.OpenRead(fileName));
                        }

                        public static void M(IDisposable _) { }

                        public void Dispose()
                        {
                            this.disposable.Dispose();
                        }
                    }
                }
                """.AssertReplace("new C(File.OpenRead(fileName))", expression);
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(fileName)");
            Assert.AreEqual(true, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [Test]
        public static void ReturnedExpressionBody()
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.IO;

                    internal class C
                    {
                        internal IDisposable C(string fileName) => File.OpenRead(fileName);
                    }
                }
                """;
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(fileName)");
            Assert.AreEqual(false, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [Test]
        public static void ReturnedStatementBody()
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.IO;

                    internal class C
                    {
                        internal IDisposable C(string fileName)
                        {
                            return File.OpenRead(fileName);
                        }
                    }
                }
                """;
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(fileName)");
            Assert.AreEqual(false, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [Test]
        public static void ReturnedDisposableCtorArg()
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.IO;

                    public class C : IDisposable
                    {
                        private readonly IDisposable disposable;

                        public C(IDisposable disposable)
                        {
                            this.disposable = disposable;
                        }

                        public static C Create(string fileName)
                        {
                            return new C(File.OpenRead(fileName));
                        }

                        public void Dispose()
                        {
                            this.disposable.Dispose();
                        }
                    }
                }
                """;
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(fileName)");
            Assert.AreEqual(false, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [TestCase("new StreamReader(File.OpenRead(string.Empty))")]
        [TestCase("File.OpenRead(string.Empty).M2()")]
        [TestCase("File.OpenRead(string.Empty)?.M2()")]
        [TestCase("M2(File.OpenRead(string.Empty))")]
        public static void ReturnedStreamWrappedInStreamReader(string expression)
        {
            var code = """

                namespace N
                {
                    using System.IO;

                    public static class C
                    {
                        public StreamReader M1() => File.OpenRead(string.Empty).M2();

                        private static StreamReader M2(this Stream stream) => new StreamReader(stream);
                    }
                }
                """.AssertReplace("File.OpenRead(string.Empty).M2()", expression);
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(string.Empty)");
            Assert.AreEqual(false, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [TestCase("await File.OpenRead(string.Empty).ReadAsync(null, 0, 0)")]
        [TestCase("await File.OpenRead(string.Empty)?.ReadAsync(null, 0, 0)")]
        [TestCase("File.OpenRead(string.Empty).ReadAsync(null, 0, 0)")]
        [TestCase("File.OpenRead(string.Empty)?.ReadAsync(null, 0, 0)")]
        public static void FileOpenReadReadAsync(string expression)
        {
            var code = """

                namespace N
                {
                    using System.IO;
                    using System.Threading.Tasks;

                    public class C
                    {
                        public async Task<int> M() => await File.OpenRead(string.Empty).ReadAsync(null, 0, 0);
                    }
                }
                """.AssertReplace("await File.OpenRead(string.Empty).ReadAsync(null, 0, 0)", expression);
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(string.Empty)");
            Assert.AreEqual(true, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [TestCase("new CompositeDisposable(File.OpenRead(fileName))")]
        [TestCase("new CompositeDisposable { File.OpenRead(fileName) }")]
        public static void ReturnedInCompositeDisposable(string expression)
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.IO;
                    using System.Reactive.Disposables;

                    public class C
                    {
                        public static IDisposable M(string fileName) => new CompositeDisposable(File.OpenRead(fileName));
                    }
                }
                """.AssertReplace("new CompositeDisposable(File.OpenRead(fileName))", expression);
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(fileName)");
            Assert.AreEqual(false, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [Test]
        public static void CtorArgAssignedNotDisposable()
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.IO;

                    public class C
                    {
                        private readonly IDisposable disposable;

                        public C(IDisposable disposable)
                        {
                            this.disposable = disposable;
                        }
                    }

                    public static class C2
                    {
                        public static void M(string fileName)
                        {
                            var c = new C(File.OpenRead(fileName));
                        }
                    }
                }
                """;
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(fileName)");
            Assert.AreEqual(true, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [Test]
        public static void CtorArgAssignedNotDisposableFactoryMethod()
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.IO;

                    public class C
                    {
                        private readonly IDisposable disposable;

                        public C(IDisposable disposable)
                        {
                            this.disposable = disposable;
                        }

                        public static C Create(string fileName) => new C(File.OpenRead(fileName));
                    }
                }
                """;
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(fileName)");
            Assert.AreEqual(true, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [Test]
        public static void CtorArgAssignedNotDisposed()
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.IO;

                    public class C : IDisposable
                    {
                        private readonly IDisposable disposable;

                        public C(IDisposable disposable)
                        {
                            this.disposable = disposable;
                        }

                        public void Dispose()
                        {
                        }
                    }

                    public static class C2
                    {
                        public static void M(string fileName)
                        {
                            var c = new C(File.OpenRead(fileName));
                        }
                    }
                }
                """;
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(fileName)");
            Assert.AreEqual(true, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [Test]
        public static void CtorArgAssignedNotDisposedFactoryMethod()
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.IO;

                    public class C : IDisposable
                    {
                        private readonly IDisposable disposable;

                        public C(IDisposable disposable)
                        {
                            this.disposable = disposable;
                        }

                        public void Dispose()
                        {
                        }

                        public static C Create(string fileName) => new C(File.OpenRead(fileName));
                    }
                }
                """;
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(fileName)");
            Assert.AreEqual(true, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [Test]
        public static void CtorArgNotAssigned()
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.IO;

                    public class C : IDisposable
                    {
                        public C(IDisposable disposable)
                        {
                        }

                        public void Dispose()
                        {
                        }

                        public static C Create(string fileName)
                        {
                            return new C(File.OpenRead(fileName));
                        }
                    }
                }
                """;
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(fileName)");
            Assert.AreEqual(true, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [Test]
        public static void CtorArgAssignedDisposed()
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.IO;

                    public class C : IDisposable
                    {
                        private readonly IDisposable disposable;

                        public C(IDisposable disposable)
                        {
                            this.disposable = disposable;
                        }

                        public void Dispose()
                        {
                            this.disposable.Dispose();
                        }

                        public static void M(string fileName)
                        {
                            var c = new C(File.OpenRead(fileName));
                        }
                    }
                }
                """;
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(fileName)");
            Assert.AreEqual(false, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [Test]
        public static void CtorArgAssignedNotAssigned()
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.IO;

                    public class C : IDisposable
                    {
                        public C(IDisposable disposable)
                        {
                        }

                        public void Dispose()
                        {
                        }

                        public static void M(string fileName)
                        {
                            var c = new C(File.OpenRead(fileName));
                        }
                    }
                }
                """;
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(fileName)");
            Assert.AreEqual(true, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [TestCase("disposable.AddAndReturn(File.OpenRead(fileName))")]
        [TestCase("disposable.AddAndReturn(File.OpenRead(fileName)).ToString()")]
        public static void CompositeDisposableExtAddAndReturn(string expression)
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.IO;
                    using System.Reactive.Disposables;

                    public static class CompositeDisposableExt
                    {
                        public static T AddAndReturn<T>(this CompositeDisposable disposable, T item)
                            where T : IDisposable
                        {
                            if (item != null)
                            {
                                disposable.Add(item);
                            }

                            return item;
                        }
                    }

                    public sealed class C : IDisposable
                    {
                        private readonly CompositeDisposable disposable = new CompositeDisposable();

                        public void Dispose()
                        {
                            this.disposable.Dispose();
                        }

                        internal object M(string fileName)
                        {
                            return this.disposable.AddAndReturn(File.OpenRead(fileName));
                        }
                    }
                }
                """.AssertReplace("disposable.AddAndReturn(File.OpenRead(fileName))", expression);
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(fileName)");
            Assert.AreEqual(false, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [TestCase("File.OpenRead(fileName)")]
        [TestCase("Task.FromResult(File.OpenRead(fileName)).Result")]
        [TestCase("Task.FromResult(File.OpenRead(fileName)).GetAwaiter().GetResult()")]
        public static void UsingDeclaration(string expression)
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.IO;
                    using System.Threading.Tasks;

                    class C
                    {
                        C(string fileName)
                        {
                            using var disposable = File.OpenRead(fileName);
                        }
                    }
                }
                """.AssertReplace("File.OpenRead(fileName)", expression);
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(fileName)");
            Assert.AreEqual(false, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [TestCase("Task.FromResult(File.OpenRead(fileName))")]
        [TestCase("Task.FromResult(File.OpenRead(fileName)).ConfigureAwait(true)")]
        [TestCase("Task.Run(() => File.OpenRead(fileName))")]
        [TestCase("Task.Run(() => File.OpenRead(fileName)).ConfigureAwait(true)")]
        public static void UsingDeclarationAwait(string expression)
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.IO;
                    using System.Threading.Tasks;

                    class C
                    {
                        async Task M(string fileName)
                        {
                            using var disposable = await Task.FromResult(File.OpenRead(fileName));
                        }
                    }
                }
                """.AssertReplace("Task.FromResult(File.OpenRead(fileName))", expression);
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(fileName)");
            Assert.AreEqual(false, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [TestCase("Task.FromResult(File.OpenRead(fileName))")]
        [TestCase("Task.FromResult(File.OpenRead(fileName)).ConfigureAwait(true)")]
        [TestCase("Task.Run(() => File.OpenRead(fileName))")]
        [TestCase("Task.Run(() => File.OpenRead(fileName)).ConfigureAwait(true)")]
        public static void AssigningFieldAwait(string expression)
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.IO;
                    using System.Threading.Tasks;

                    public sealed class C : IDisposable
                    {
                        private IDisposable disposable;

                        public async Task M(string fileName)
                        {
                            this.disposable?.Dispose();
                            this.disposable = await Task.FromResult(File.OpenRead(fileName));
                        }

                        public void Dispose()
                        {
                            this.disposable?.Dispose();
                        }
                    }
                }
                """.AssertReplace("Task.FromResult(File.OpenRead(fileName))", expression);
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("File.OpenRead(fileName)");
            Assert.AreEqual(false, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [TestCase("leaveOpen: false", false)]
        [TestCase("leaveOpen: true", true)]
        public static void ReturnAsReadOnlyViewAsReadOnlyFilteredView(string expression, bool expected)
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.Collections.Generic;
                    using Gu.Reactive;

                    public static class C
                    {
                        public static ReadOnlyFilteredView<T> M<T>(
                            this IObservable<IEnumerable<T>> source,
                            Func<T, bool> filter)
                        {
                            return source.AsReadOnlyView().AsReadOnlyFilteredView(filter, leaveOpen: false);
                        }
                    }
                }
                """.AssertReplace("leaveOpen: false", expression);
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindInvocation("AsReadOnlyView()");
            Assert.AreEqual(expected, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [Test]
        public static void UsingAsReadOnlyViewAsReadOnlyFilteredView()
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.Collections.Generic;
                    using Gu.Reactive;

                    public static class C
                    {
                        public static void M<T>(IObservable<IEnumerable<T>> source, Func<T, bool> filter)
                        {
                            using var view = source.AsReadOnlyView().AsReadOnlyFilteredView(filter, leaveOpen: false);
                        }
                    }
                }
                """;
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindInvocation("AsReadOnlyView()");
            Assert.AreEqual(false, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [TestCase("leaveOpen: false", false)]
        [TestCase("leaveOpen: true",  true)]
        public static void AssignAsReadOnlyViewAsReadOnlyFilteredView(string expression, bool expected)
        {
            var code = """

                namespace N
                {
                    using System;
                    using System.Collections.Generic;
                    using Gu.Reactive;

                    public sealed class UsingGuReactive : IDisposable
                    {
                        private readonly IReadOnlyView<int> view;

                        public UsingGuReactive(IObservable<IEnumerable<int>> source, Func<int, bool> filter)
                        {
                          this.view = source.AsReadOnlyView()
                                            .AsReadOnlyFilteredView(filter, leaveOpen: false);
                        }

                        public void Dispose()
                        {
                            this.view.Dispose();
                        }
                    }
                }
                """.AssertReplace("leaveOpen: false", expression);
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindInvocation("AsReadOnlyView()");
            Assert.AreEqual(expected, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [Test]
        public static void ReturnAsReadOnlyFilteredViewAsMappingView()
        {
            var syntaxTree = CSharpSyntaxTree.ParseText("""

                namespace N
                {
                    using System;
                    using System.Collections.Generic;
                    using System.IO;
                    using Gu.Reactive;

                    public static class C
                    {
                        public static IReadOnlyView<IDisposable> M2(IObservable<IEnumerable<int>> source, Func<int, bool> filter)
                        {
                            return source.AsReadOnlyFilteredView(filter).AsMappingView(x => new MemoryStream(), onRemove:x => x.Dispose());
                        }
                    }
                }
                """);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindInvocation("AsReadOnlyFilteredView(filter)");
            Assert.AreEqual(false, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [Test]
        public static void AssigningGenericSerialDisposable()
        {
            var syntaxTree = CSharpSyntaxTree.ParseText("""

                namespace N
                {
                    using System;
                    using System.IO;

                    using Gu.Reactive;

                    public sealed class C : IDisposable
                    {
                        private readonly SerialDisposable<MemoryStream> serialDisposable = new SerialDisposable<MemoryStream>();

                        public C(IObservable<int> observable)
                        {
                            observable.Subscribe(x => this.serialDisposable.Disposable = new MemoryStream());
                        }

                        public void Dispose()
                        {
                            this.serialDisposable.Dispose();
                        }
                    }
                }
                """);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("new MemoryStream()");
            Assert.AreEqual(false, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [TestCase("Create(true)")]
        [TestCase("Create(!false)")]
        [TestCase("CreateAsync(true)")]
        [TestCase("CreateAsync(!false)")]
        public static void AssertThrows(string expressionText)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText("""

                namespace ValidCode
                {
                    using System;
                    using System.Threading.Tasks;
                    using NUnit.Framework;

                    public static class Tests
                    {
                        [Test]
                        public static void Throws()
                        {
                            Assert.Throws<InvalidOperationException>(() => Create(true));
                            Assert.AreEqual(
                                "Expected",
                                Assert.Throws<InvalidOperationException>(() => Create(!false)).Message);

                            Assert.ThrowsAsync<InvalidOperationException>(() => CreateAsync(true));
                            Assert.AreEqual(
                                "Expected",
                                Assert.ThrowsAsync<InvalidOperationException>(() => CreateAsync(!false)).Message);
                        }

                        private static Disposable Create(bool b)
                        {
                            if (b)
                            {
                                throw new InvalidOperationException("Expected");
                            }

                            return new Disposable();
                        }

                        private static async Task<Disposable> CreateAsync(bool b)
                        {
                            if (b)
                            {
                                throw new InvalidOperationException("Expected");
                            }

                            await Task.Delay(1);
                            return new Disposable();
                        }
                    }
                }
                """);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression(expressionText);
            Assert.AreEqual(false, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [TestCase("x1 => new Disposable()")]
        [TestCase("x2 => new Disposable()")]
        [TestCase("x3 => new Disposable()")]
        [TestCase("x4 => new Disposable()")]
        public static void ServiceCollection(string expressionText)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText("""

                namespace ValidCode
                {
                    using System;
                    using Microsoft.Extensions.DependencyInjection;

                    public static class Issue231
                    {
                        public static ServiceCollection M1(ServiceCollection serviceCollection)
                        {
                            serviceCollection.AddScoped(x1 => new Disposable());
                            serviceCollection.AddSingleton(typeof(IDisposable), x2 => new Disposable());
                            return serviceCollection;
                        }

                        public static IServiceCollection M2(IServiceCollection serviceCollection)
                        {
                            serviceCollection.AddScoped(x3 => new Disposable());
                            serviceCollection.AddSingleton(typeof(IDisposable), x4 => new Disposable());
                            return serviceCollection;
                        }
                    }
                }
                """);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression(expressionText);
            Assert.AreEqual(false, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [TestCase("observable.Subscribe(x => Console.WriteLine(x))")]
        [TestCase("observable.Subscribe(x => Console.WriteLine(x)).DisposeWith(this.disposable)")]
        public static void DisposeWith(string expressionText)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText("""

                namespace N
                {
                    using System;
                    using System.Reactive.Disposables;

                    public sealed class Issue298 : IDisposable
                    {
                        private readonly CompositeDisposable disposable = new();

                        public Issue298(IObservable<object> observable)
                        {
                            observable.Subscribe(x => Console.WriteLine(x)).DisposeWith(this.disposable);
                        }

                        public void Dispose()
                        {
                            this.disposable.Dispose();
                        }
                    }
                }
                """);

            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression(expressionText);
            Assert.AreEqual(false, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [TestCase(".Append(1)")]
        [TestCase(".Append(true)")]
        [TestCase(".Append((string?)null)")]
        [TestCase(".Append(condition, string.Empty)")]
        [TestCase(".Append(!condition, string.Empty)")]
        [TestCase(".Append(true).Append(condition).Append(!condition)")]
        public static void Builder(string expression)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText("""
                namespace N;

                using System;

                public record struct S : IDisposable
                {
                    internal Request Append(string? value, string? name = null)
                    {
                        if (value is not null)
                        {
                            foreach (var c in value)
                            {
                                this.bytes[this.position] = (byte)c;
                                this.position++;
                            }
                        }

                        this.bytes[this.position] = 0;
                        this.position++;
                        return this;
                    }

                    public S Append(int value, string? name = null)
                    {
                        if (value > 3)
                        {
                            return this;
                        }
                
                        throw new Exception();
                    }
                
                    internal S Append(bool value, string? name = null) => this.Append(value ? 1 : 0, name);

                    public void Dispose() { }

                    public void M(bool condition)
                    {
                        using var s = new S().Append(true);
                    }
                }
                """.AssertReplace(".Append(true)", expression));

            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("new S()");
            Assert.AreEqual(false, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [Test]
        public static void ReturnedInTargetTypedValueTask()
        {
            var syntaxTree = CSharpSyntaxTree.ParseText("""
                namespace N;

                using System;
                using System.Threading.Tasks;

                public class Disposable : IDisposable
                {
                    public void Dispose() { }
                }

                public class Issue476
                {
                    public static ValueTask<IDisposable> MAsync()
                    {
                        return new(new Disposable());
                    }
                }
                """);

            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindExpression("new Disposable()");
            Assert.AreEqual(false, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }

        [Test]
        public static void AwaitAwaitHttpClientGetAsync()
        {
            var syntaxTree = CSharpSyntaxTree.ParseText("""
                namespace N;

                using System.Net.Http;
                using System.Threading.Tasks;

                public class Issue248
                {
                    private static readonly HttpClient Client = new();

                    public static async Task<string> M()
                    {
                        string versions = await (await Client.GetAsync(string.Empty)).Content.ReadAsStringAsync();
                        return versions;
                    }
                }
                """);
            var compilation = CSharpCompilation.Create("test", new[] { syntaxTree }, Settings.Default.MetadataReferences);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var value = syntaxTree.FindInvocation("Client.GetAsync(string.Empty)");
            Assert.AreEqual(true, Disposable.Ignores(value, new AnalyzerContext(semanticModel, compilation), CancellationToken.None));
        }
    }
}
