using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CodeIndexer.Core.Nodes;

namespace CodeIndexer.Parsing.CSharp.Internal;

internal static class MetadataBuilder
{
    private static readonly HashSet<string> TestAttributeNames = new(StringComparer.Ordinal)
    {
        "Fact", "FactAttribute", "Theory", "TheoryAttribute", "Test", "TestAttribute", "TestMethod", "TestMethodAttribute",
    };

    public static NodeMetadata Build(SyntaxTokenList modifiers, bool isTopLevelType, IEnumerable<AttributeListSyntax>? attributeLists = null)
    {
        var hasPublic = modifiers.Any(SyntaxKind.PublicKeyword);
        var hasPrivate = modifiers.Any(SyntaxKind.PrivateKeyword);
        var hasProtected = modifiers.Any(SyntaxKind.ProtectedKeyword);
        var hasInternal = modifiers.Any(SyntaxKind.InternalKeyword);
        var hasAnyVisibility = hasPublic || hasPrivate || hasProtected || hasInternal;

        if (!hasAnyVisibility)
        {
            if (isTopLevelType)
            {
                hasInternal = true;
            }
            else
            {
                hasPrivate = true;
            }
        }

        var isTest = attributeLists is not null && attributeLists
            .SelectMany(list => list.Attributes)
            .Any(a => TestAttributeNames.Contains(a.Name.ToString().Split('.').Last()));

        return new NodeMetadata
        {
            IsPublic = hasPublic,
            IsPrivate = hasPrivate,
            IsProtected = hasProtected,
            IsInternal = hasInternal,
            IsStatic = modifiers.Any(SyntaxKind.StaticKeyword),
            IsAsync = modifiers.Any(SyntaxKind.AsyncKeyword),
            IsAbstract = modifiers.Any(SyntaxKind.AbstractKeyword),
            IsVirtual = modifiers.Any(SyntaxKind.VirtualKeyword),
            IsOverride = modifiers.Any(SyntaxKind.OverrideKeyword),
            IsTest = isTest,
        };
    }
}
