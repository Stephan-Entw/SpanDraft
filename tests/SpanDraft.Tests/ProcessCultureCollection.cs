using Xunit;

namespace SpanDraft.Tests;

// Startup culture changes process-wide defaults; exclude all other Product collections.
[CollectionDefinition("Process culture", DisableParallelization = true)]
public sealed class ProcessCultureCollection;
