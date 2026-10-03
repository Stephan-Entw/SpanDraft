using SpanDraft.Core.Beams;
using Xunit;

namespace SpanDraft.Tests;

public class SmokeTests
{
    [Fact]
    public void CoreHasNoUiNumericsOrReportingDependencies()
    {
        var assembly = typeof(BeamModel).Assembly;
        Assert.Equal("SpanDraft.Core", assembly.GetName().Name);
        Assert.All(assembly.GetReferencedAssemblies(), reference =>
            Assert.True(reference.Name is not null && reference.Name.StartsWith("System", StringComparison.Ordinal),
                $"Unexpected Core dependency: {reference.Name}"));
    }
}
