using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class ProjectCheckpointTests
{
    [Fact]
    public async Task CompletedOperationWithoutCheckpointFailsImmediately()
    {
        var checkpoint = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WaitForCheckpoint(checkpoint.Task, Task.CompletedTask));
        Assert.Equal("Operation completed before reaching the expected checkpoint.", error.Message);
    }

    [Fact]
    public async Task FaultedOperationWithoutCheckpointPreservesOriginalFailure()
    {
        var checkpoint = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new IOException("Injected write failure.");
        var error = await Assert.ThrowsAsync<IOException>(() =>
            WaitForCheckpoint(checkpoint.Task, Task.FromException(failure)));
        Assert.Same(failure, error);
    }

    [Fact]
    public async Task ReachedCheckpointDoesNotRequireOperationCompletion()
    {
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await WaitForCheckpoint(Task.CompletedTask, operation.Task);
            Assert.False(operation.Task.IsCompleted);
        }
        finally { operation.TrySetResult(); }
    }
}
