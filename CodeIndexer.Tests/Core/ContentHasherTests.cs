using CodeIndexer.Core.Nodes;
using Xunit;

namespace CodeIndexer.Tests.Core;

public class ContentHasherTests
{
    [Fact]
    public void Hash_SameContent_ProducesSameHash()
    {
        var a = ContentHasher.Hash("public void Foo() {}");
        var b = ContentHasher.Hash("public void Foo() {}");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Hash_DifferentContent_ProducesDifferentHash()
    {
        var a = ContentHasher.Hash("public void Foo() {}");
        var b = ContentHasher.Hash("public void Bar() {}");

        Assert.NotEqual(a, b);
    }
}
