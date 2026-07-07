using CodeIndexer.Core.Nodes;
using CodeIndexer.Parsing.TypeScript;
using Xunit;

namespace CodeIndexer.Tests.Parsing;

public class TypeScriptParserTests
{
    private static async Task<IReadOnlyList<CodeNode>> ParseAsync(string source, string fileName = "test.ts")
    {
        var parser = new TypeScriptParser();
        var result = await parser.ParseFileAsync(fileName, source, CancellationToken.None);
        Assert.True(result.Success, result.ErrorMessage);
        return result.Nodes;
    }

    [Fact]
    public async Task Parse_ClassWithTypedMethod_PreservesRealReturnAndParamTypes()
    {
        const string source = """
            export class UserService {
                async getUser(id: number): Promise<string> {
                    return String(id);
                }
            }
            """;

        var nodes = await ParseAsync(source);

        var method = Assert.Single(nodes, n => n.Kind == NodeKind.Method);
        Assert.Equal("UserService.getUser", method.QualifiedName);
        Assert.Equal("Promise<string>", method.Summary.ReturnType);
        Assert.Equal("number", method.Summary.Parameters[0].Type);
        Assert.True(method.Metadata.IsAsync);
    }

    [Fact]
    public async Task Parse_ExportedClass_IsMarkedPublic_NonExportedIsPrivate()
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
    public async Task Parse_Interface_ProducesInterfaceKindWithMembers()
    {
        const string source = """
            export interface User {
                id: number;
                name: string;
                greet(): void;
            }
            """;

        var nodes = await ParseAsync(source);

        var iface = Assert.Single(nodes, n => n.Kind == NodeKind.Interface);
        Assert.Equal("User", iface.Name);
        Assert.Contains(nodes, n => n.Kind == NodeKind.Field && n.Name == "id" && n.Summary.ReturnType == "number");
        Assert.Contains(nodes, n => n.Kind == NodeKind.Method && n.Name == "greet");
    }

    [Fact]
    public async Task Parse_Enum_ProducesEnumKind()
    {
        const string source = """
            export enum Role {
                Admin,
                User,
            }
            """;

        var nodes = await ParseAsync(source);

        Assert.Contains(nodes, n => n.Kind == NodeKind.Enum && n.Name == "Role");
    }

    [Fact]
    public async Task Parse_GenericClassWithHeritage_RendersInSignature()
    {
        const string source = """
            interface Base {}
            export class Repo<T> extends Base implements Base {
                items: T[] = [];
            }
            """;

        var nodes = await ParseAsync(source);

        var cls = Assert.Single(nodes, n => n.Name == "Repo");
        Assert.Equal("class Repo extends Base implements Base", cls.Summary.Signature);
        Assert.Equal("Base;Base", cls.Metadata.Extra[NodeMetadataExtensions.BaseTypesExtraKey]);
    }

    [Fact]
    public async Task Parse_Decorators_DoNotBreakParsing()
    {
        const string source = """
            function Injectable() { return (target: any) => target; }

            @Injectable()
            export class Service {
                @Input() value: string = "";
            }
            """;

        var nodes = await ParseAsync(source);

        Assert.Contains(nodes, n => n.Kind == NodeKind.Class && n.Name == "Service");
        Assert.Contains(nodes, n => n.Kind == NodeKind.Field && n.Name == "value");
    }

    [Fact]
    public async Task Parse_PrivateAndReadonlyModifiers_AreCaptured()
    {
        const string source = """
            export class Account {
                private balance: number = 0;
                public readonly id: string = "1";

                private computeInterest(): number {
                    return this.balance * 0.01;
                }
            }
            """;

        var nodes = await ParseAsync(source);

        var balance = Assert.Single(nodes, n => n.Name == "balance");
        Assert.True(balance.Metadata.IsPrivate);

        var id = Assert.Single(nodes, n => n.Name == "id");
        Assert.True(id.Metadata.Extra.ContainsKey("readonly"));

        var method = Assert.Single(nodes, n => n.Name == "computeInterest");
        Assert.True(method.Metadata.IsPrivate);
    }

    [Fact]
    public async Task Parse_ArrowFunctionConst_IsTreatedAsMethod()
    {
        const string source = "export const add = (a: number, b: number): number => a + b;";

        var nodes = await ParseAsync(source);

        var node = Assert.Single(nodes);
        Assert.Equal(NodeKind.Method, node.Kind);
        Assert.True(node.Metadata.IsPublic);
        Assert.Equal("number", node.Summary.Parameters[0].Type);
    }

    [Fact]
    public async Task Parse_ConstAndLetTopLevel_DistinguishedByKind()
    {
        const string source = """
            const MAX: number = 3;
            let counter: number = 0;
            """;

        var nodes = await ParseAsync(source);

        Assert.Contains(nodes, n => n.Kind == NodeKind.Constant && n.Name == "MAX");
        Assert.Contains(nodes, n => n.Kind == NodeKind.Field && n.Name == "counter");
    }

    [Fact]
    public async Task Parse_Namespace_BuildsScopeChain()
    {
        const string source = """
            namespace MyNamespace {
                export function helper(): void {}
            }
            """;

        var nodes = await ParseAsync(source);

        Assert.Contains(nodes, n => n.Kind == NodeKind.Namespace && n.Name == "MyNamespace");
        var fn = Assert.Single(nodes, n => n.Kind == NodeKind.Method);
        Assert.Equal("MyNamespace.helper", fn.QualifiedName);
    }

    [Fact]
    public async Task Parse_TypeAlias_MapsToInterfaceKind()
    {
        const string source = "export type Handler = (event: string) => void;";

        var nodes = await ParseAsync(source);

        var alias = Assert.Single(nodes);
        Assert.Equal(NodeKind.Interface, alias.Kind);
        Assert.Equal("Handler", alias.Name);
    }

    [Fact]
    public async Task Parse_ImportDeclaration_EmitsImportNode()
    {
        const string source = "import { Injectable } from '@angular/core';";

        var nodes = await ParseAsync(source);

        var import = Assert.Single(nodes);
        Assert.Equal(NodeKind.Import, import.Kind);
        Assert.Equal("@angular/core", import.Name);
    }

    [Fact]
    public async Task Parse_JsDocComment_IsExtracted()
    {
        const string source = """
            /**
             * Adds two numbers.
             */
            export function add(a: number, b: number): number {
                return a + b;
            }
            """;

        var nodes = await ParseAsync(source);

        var fn = Assert.Single(nodes);
        Assert.NotNull(fn.Summary.DocComment);
        Assert.Contains("Adds two numbers", fn.Summary.DocComment);
    }

    [Fact]
    public async Task Parse_UnionAndUtilityTypes_DoNotBreakParsing()
    {
        const string source = """
            export class Box<T extends { id: number } = { id: number }> {
                private cache: Map<number, T | undefined> = new Map();

                get(id: number): T | undefined {
                    return this.cache.get(id);
                }
            }
            """;

        var nodes = await ParseAsync(source);

        var method = Assert.Single(nodes, n => n.Kind == NodeKind.Method);
        Assert.Equal("T | undefined", method.Summary.ReturnType);
    }

    [Fact]
    public async Task ParseFileAsync_SeverelyMalformedInput_ReturnsFailureNotException()
    {
        const string source = "class {{{ totally broken !!! ***";

        var parser = new TypeScriptParser();
        var result = await parser.ParseFileAsync("Broken.ts", source, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task Parse_SameNodeTwice_ProducesSameId()
    {
        const string source = "export class Foo { bar(): void {} }";

        var first = await ParseAsync(source);
        var second = await ParseAsync(source);

        var firstMethod = Assert.Single(first, n => n.Kind == NodeKind.Method);
        var secondMethod = Assert.Single(second, n => n.Kind == NodeKind.Method);

        Assert.Equal(firstMethod.Id, secondMethod.Id);
        Assert.Equal(firstMethod.ContentHash, secondMethod.ContentHash);
    }

    [Fact]
    public void SupportedExtensions_DoesNotClaimTsx()
    {
        var parser = new TypeScriptParser();

        Assert.DoesNotContain(".tsx", parser.SupportedExtensions);
        Assert.Contains(".ts", parser.SupportedExtensions);
    }
}
