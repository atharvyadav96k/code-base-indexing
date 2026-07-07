using CodeIndexer.Core.Nodes;
using CodeIndexer.Parsing.CSharp;
using Xunit;

namespace CodeIndexer.Tests.Parsing;

public class CSharpParserTests
{
    private static async Task<IReadOnlyList<CodeNode>> ParseAsync(string source)
    {
        var parser = new CSharpParser();
        var result = await parser.ParseFileAsync("Test.cs", source, CancellationToken.None);
        Assert.True(result.Success, result.ErrorMessage);
        return result.Nodes;
    }

    [Fact]
    public async Task Parse_NamespaceClassAndMethod_ProducesQualifiedNames()
    {
        const string source = """
            namespace MyApp.Services;

            public class UserService
            {
                public string GetName(int id)
                {
                    return "x";
                }
            }
            """;

        var nodes = await ParseAsync(source);

        Assert.Contains(nodes, n => n.Kind == NodeKind.Namespace && n.QualifiedName == "MyApp.Services");
        Assert.Contains(nodes, n => n.Kind == NodeKind.Class && n.QualifiedName == "MyApp.Services.UserService");
        Assert.Contains(nodes, n => n.Kind == NodeKind.Method && n.QualifiedName == "MyApp.Services.UserService.GetName");
    }

    [Fact]
    public async Task Parse_NestedTypes_BuildFullScopeChain()
    {
        const string source = """
            namespace Outer;

            public class Container
            {
                public class Inner
                {
                    public void DoWork() {}
                }
            }
            """;

        var nodes = await ParseAsync(source);

        var inner = Assert.Single(nodes, n => n.Kind == NodeKind.Class && n.Name == "Inner");
        Assert.Equal("Outer.Container.Inner", inner.QualifiedName);
        Assert.Equal(new[] { "Outer", "Container", "Inner" }, inner.ScopeChain);

        var method = Assert.Single(nodes, n => n.Kind == NodeKind.Method);
        Assert.Equal("Outer.Container.Inner.DoWork", method.QualifiedName);
    }

    [Fact]
    public async Task Parse_GenericMethodAndMultiLineSignature_ExtractsSignatureAndParameters()
    {
        const string source = """
            namespace App;

            public class Repo
            {
                public async Task<TResult> QueryAsync<TResult>(
                    int id,
                    string filter)
                {
                    return default!;
                }
            }
            """;

        var nodes = await ParseAsync(source);

        var method = Assert.Single(nodes, n => n.Kind == NodeKind.Method);
        Assert.Equal("Task<TResult>", method.Summary.ReturnType);
        Assert.Equal(2, method.Summary.Parameters.Count);
        Assert.Equal("id", method.Summary.Parameters[0].Name);
        Assert.Equal("filter", method.Summary.Parameters[1].Name);
        Assert.Contains("QueryAsync<TResult>", method.Summary.Signature);
        Assert.True(method.Metadata.IsAsync);
        Assert.True(method.Metadata.IsPublic);
    }

    [Fact]
    public async Task Parse_ExpressionBodiedMember_IsCaptured()
    {
        const string source = """
            namespace App;

            public class Point
            {
                public int X { get; set; }
                public int DoubleX => X * 2;
            }
            """;

        var nodes = await ParseAsync(source);

        var prop = Assert.Single(nodes, n => n.Name == "DoubleX");
        Assert.Equal(NodeKind.Property, prop.Kind);
        Assert.Contains("=>", prop.Body);
    }

    [Fact]
    public async Task Parse_StringsAndCommentsContainingBracesAndKeywords_DoNotConfuseParser()
    {
        const string source = """
            namespace App;

            public class Weird
            {
                // this method has "class" and { in a comment
                public string Render()
                {
                    var s = "public class { Fake() { return \"}\"; } }";
                    return s;
                }
            }
            """;

        var nodes = await ParseAsync(source);

        Assert.Single(nodes, n => n.Kind == NodeKind.Class);
        Assert.Single(nodes, n => n.Kind == NodeKind.Method);
    }

    [Fact]
    public async Task Parse_ConstAndRegularField_DistinguishedByKind()
    {
        const string source = """
            namespace App;

            public class Config
            {
                public const int MaxRetries = 3;
                private readonly string _name;
            }
            """;

        var nodes = await ParseAsync(source);

        Assert.Contains(nodes, n => n.Kind == NodeKind.Constant && n.Name == "MaxRetries");
        var field = Assert.Single(nodes, n => n.Name == "_name");
        Assert.Equal(NodeKind.Field, field.Kind);
        Assert.True(field.Metadata.IsPrivate);
    }

    [Fact]
    public async Task Parse_UsingDirectives_EmitImportNodes()
    {
        const string source = """
            using System;
            using System.Collections.Generic;

            namespace App;

            public class Foo {}
            """;

        var nodes = await ParseAsync(source);

        Assert.Contains(nodes, n => n.Kind == NodeKind.Import && n.Name == "System");
        Assert.Contains(nodes, n => n.Kind == NodeKind.Import && n.Name == "System.Collections.Generic");
    }

    [Fact]
    public async Task Parse_DocComment_IsExtracted()
    {
        const string source = """
            namespace App;

            public class Foo
            {
                /// <summary>Adds two numbers.</summary>
                public int Add(int a, int b) => a + b;
            }
            """;

        var nodes = await ParseAsync(source);

        var method = Assert.Single(nodes, n => n.Kind == NodeKind.Method);
        Assert.NotNull(method.Summary.DocComment);
        Assert.Contains("Adds two numbers", method.Summary.DocComment);
    }

    [Fact]
    public async Task ParseFileAsync_SyntaxError_ReturnsFailureNotException()
    {
        const string source = "public class {{{ broken";

        var parser = new CSharpParser();
        var result = await parser.ParseFileAsync("Broken.cs", source, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task Parse_InterfaceAndEnum_ProduceExpectedKinds()
    {
        const string source = """
            namespace App;

            public interface IShape
            {
                double Area();
            }

            public enum Color
            {
                Red,
                Green,
                Blue,
            }
            """;

        var nodes = await ParseAsync(source);

        Assert.Contains(nodes, n => n.Kind == NodeKind.Interface && n.Name == "IShape");
        Assert.Contains(nodes, n => n.Kind == NodeKind.Enum && n.Name == "Color");
    }

    [Fact]
    public async Task Parse_SameNodeTwice_ProducesSameId()
    {
        const string source = """
            namespace App;
            public class Foo
            {
                public void Bar() {}
            }
            """;

        var first = await ParseAsync(source);
        var second = await ParseAsync(source);

        var firstMethod = Assert.Single(first, n => n.Kind == NodeKind.Method);
        var secondMethod = Assert.Single(second, n => n.Kind == NodeKind.Method);

        Assert.Equal(firstMethod.Id, secondMethod.Id);
        Assert.Equal(firstMethod.ContentHash, secondMethod.ContentHash);
    }
}
