using SpanDraft.Solver.Kernel;
using Xunit;

namespace SpanDraft.Tests;

public sealed class SolverKernelTests
{
    [Fact]
    public void SolverKernelIsInternal()
    {
        Assert.True(typeof(SolverKernel).IsNotPublic);
    }
}
