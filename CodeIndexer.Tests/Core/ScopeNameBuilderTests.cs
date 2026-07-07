using CodeIndexer.Core.Scope;
using Xunit;

namespace CodeIndexer.Tests.Core;

public class ScopeNameBuilderTests
{
    [Fact]
    public void Build_JoinsScopeChainWithSeparator()
    {
        var result = ScopeNameBuilder.Build(new[] { "MyApp", "Services", "UserService" }, ".");

        Assert.Equal("MyApp.Services.UserService", result);
    }

    [Fact]
    public void Build_SingleElement_ReturnsElementUnchanged()
    {
        var result = ScopeNameBuilder.Build(new[] { "Root" }, ".");

        Assert.Equal("Root", result);
    }

    [Fact]
    public void Build_UsesLanguageSuppliedSeparator()
    {
        var result = ScopeNameBuilder.Build(new[] { "pkg", "sub", "Mod" }, "::");

        Assert.Equal("pkg::sub::Mod", result);
    }
}
