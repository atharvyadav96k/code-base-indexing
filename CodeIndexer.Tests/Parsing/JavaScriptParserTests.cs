using CodeIndexer.Core.Nodes;
using CodeIndexer.Parsing.JavaScript;
using Xunit;

namespace CodeIndexer.Tests.Parsing;

public class JavaScriptParserTests
{
    private static async Task<IReadOnlyList<CodeNode>> ParseAsync(string source, string fileName = "test.js")
    {
        var parser = new JavaScriptParser();
        var result = await parser.ParseFileAsync(fileName, source, CancellationToken.None);
        Assert.True(result.Success, result.ErrorMessage);
        return result.Nodes;
    }

    [Fact]
    public async Task Parse_ClassWithMethod_ProducesQualifiedNames()
    {
        const string source = """
            class UserService {
                async getUser(id) {
                    return await fetch(id);
                }
            }
            """;

        var nodes = await ParseAsync(source);

        Assert.Contains(nodes, n => n.Kind == NodeKind.Class && n.QualifiedName == "UserService");
        var method = Assert.Single(nodes, n => n.Kind == NodeKind.Method);
        Assert.Equal("UserService.getUser", method.QualifiedName);
        Assert.True(method.Metadata.IsAsync);
    }

    [Fact]
    public async Task Parse_NestedClass_BuildsFullScopeChain()
    {
        const string source = """
            class Outer {
                helper() {}
            }
            """;

        var nodes = await ParseAsync(source);
        var method = Assert.Single(nodes, n => n.Kind == NodeKind.Method);
        Assert.Equal(new[] { "Outer", "helper" }, method.ScopeChain);
    }

    [Fact]
    public async Task Parse_ArrowFunctionConst_IsTreatedAsMethod()
    {
        const string source = "const getUser = async (id) => { return id; };";

        var nodes = await ParseAsync(source);

        var node = Assert.Single(nodes);
        Assert.Equal(NodeKind.Method, node.Kind);
        Assert.Equal("getUser", node.Name);
        Assert.True(node.Metadata.IsAsync);
        Assert.Single(node.Summary.Parameters);
        Assert.Equal("id", node.Summary.Parameters[0].Name);
    }

    [Fact]
    public async Task Parse_ClassFieldArrowFunction_IsTreatedAsMethod()
    {
        const string source = """
            class Widget {
                handleClick = (event) => {
                    console.log(event);
                };
            }
            """;

        var nodes = await ParseAsync(source);

        var method = Assert.Single(nodes, n => n.Name == "handleClick");
        Assert.Equal(NodeKind.Method, method.Kind);
    }

    [Fact]
    public async Task Parse_ConstAndLetTopLevel_DistinguishedByKind()
    {
        const string source = """
            const MAX_RETRIES = 3;
            let counter = 0;
            """;

        var nodes = await ParseAsync(source);

        Assert.Contains(nodes, n => n.Kind == NodeKind.Constant && n.Name == "MAX_RETRIES");
        Assert.Contains(nodes, n => n.Kind == NodeKind.Field && n.Name == "counter");
    }

    [Fact]
    public async Task Parse_ImportDeclarations_EmitImportNodes()
    {
        const string source = """
            import React from 'react';
            import { useState } from "react";
            """;

        var nodes = await ParseAsync(source);

        Assert.Contains(nodes, n => n.Kind == NodeKind.Import && n.Name == "react");
    }

    [Fact]
    public async Task Parse_ExportedDeclaration_IsMarkedPublic()
    {
        const string source = """
            export class Foo {}
            class Bar {}
            """;

        var nodes = await ParseAsync(source);

        var foo = Assert.Single(nodes, n => n.Name == "Foo");
        var bar = Assert.Single(nodes, n => n.Name == "Bar");
        Assert.True(foo.Metadata.IsPublic);
        Assert.True(bar.Metadata.IsPrivate);
    }

    [Fact]
    public async Task Parse_ExportDefaultFunction_IsMarkedPublicWithFallbackName()
    {
        const string source = "export default function() { return 1; }";

        var nodes = await ParseAsync(source);

        var fn = Assert.Single(nodes);
        Assert.Equal("default", fn.Name);
        Assert.True(fn.Metadata.IsPublic);
    }

    [Fact]
    public async Task Parse_GetterAndSetter_ProduceDistinctSignatures()
    {
        const string source = """
            class Box {
                get value() { return this._v; }
                set value(v) { this._v = v; }
            }
            """;

        var nodes = await ParseAsync(source);

        var methods = nodes.Where(n => n.Kind == NodeKind.Method).ToList();
        Assert.Equal(2, methods.Count);
        Assert.Contains(methods, m => m.Summary.Signature.StartsWith("get "));
        Assert.Contains(methods, m => m.Summary.Signature.StartsWith("set "));
    }

    [Fact]
    public async Task Parse_PrivateClassField_IsMarkedPrivate()
    {
        const string source = """
            class Account {
                #balance = 0;
                #computeInterest() { return this.#balance * 0.01; }
            }
            """;

        var nodes = await ParseAsync(source);

        var field = Assert.Single(nodes, n => n.Name == "#balance");
        Assert.True(field.Metadata.IsPrivate);
        var method = Assert.Single(nodes, n => n.Name == "#computeInterest");
        Assert.True(method.Metadata.IsPrivate);
    }

    [Fact]
    public async Task Parse_JsDocComment_IsExtracted()
    {
        const string source = """
            /**
             * Adds two numbers.
             */
            function add(a, b) {
                return a + b;
            }
            """;

        var nodes = await ParseAsync(source);

        var fn = Assert.Single(nodes);
        Assert.NotNull(fn.Summary.DocComment);
        Assert.Contains("Adds two numbers", fn.Summary.DocComment);
    }

    [Fact]
    public async Task Parse_StringsAndCommentsContainingBracesAndKeywords_DoNotConfuseParser()
    {
        const string source = """
            class Weird {
                // this method has "class" and { in a comment
                render() {
                    const s = "class { fake() { return '}' } }";
                    return s;
                }
            }
            """;

        var nodes = await ParseAsync(source);

        Assert.Single(nodes, n => n.Kind == NodeKind.Class);
        Assert.Single(nodes, n => n.Kind == NodeKind.Method);
    }

    [Fact]
    public async Task Parse_CommonJsStyleScript_FallsBackToScriptParsing()
    {
        const string source = """
            function helper() { return 1; }
            module.exports = { helper };
            """;

        var nodes = await ParseAsync(source);

        Assert.Contains(nodes, n => n.Kind == NodeKind.Method && n.Name == "helper");
    }

    [Fact]
    public async Task ParseFileAsync_SyntaxError_ReturnsFailureNotException()
    {
        const string source = "function broken( {{{ nope";

        var parser = new JavaScriptParser();
        var result = await parser.ParseFileAsync("Broken.js", source, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task Parse_SameNodeTwice_ProducesSameId()
    {
        const string source = "class Foo { bar() {} }";

        var first = await ParseAsync(source);
        var second = await ParseAsync(source);

        var firstMethod = Assert.Single(first, n => n.Kind == NodeKind.Method);
        var secondMethod = Assert.Single(second, n => n.Kind == NodeKind.Method);

        Assert.Equal(firstMethod.Id, secondMethod.Id);
        Assert.Equal(firstMethod.ContentHash, secondMethod.ContentHash);
    }

    [Fact]
    public async Task Parse_GeneratorFunction_MarksGeneratorInExtraMetadata()
    {
        const string source = "function* idGenerator() { yield 1; }";

        var nodes = await ParseAsync(source);

        var fn = Assert.Single(nodes);
        Assert.True(fn.Metadata.Extra.ContainsKey("generator"));
    }
}
