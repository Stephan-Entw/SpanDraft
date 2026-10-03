using System.Reflection;
using Xunit;

namespace SpanDraft.Tests;

public class SmokeTests
{
    [Fact]
    public void ReferencedAssembliesCanBeLoaded()
    {
        Assert.Equal("SpanDraft.Core", Assembly.Load("SpanDraft.Core").GetName().Name);
        Assert.Equal("SpanDraft.Solver", Assembly.Load("SpanDraft.Solver").GetName().Name);
    }
}
