using Avalonia;
using Xunit;

namespace SpanDraft.Tests;

[CollectionDefinition("Avalonia", DisableParallelization = true)]
public sealed class AvaloniaCollection : ICollectionFixture<SchematicRenderingFixture>;

public sealed class SchematicRenderingFixture
{
    public SchematicRenderingFixture() => AppBuilder.Configure<Application>()
        // Initialize runtime services and the real Skia text/geometry backend
        // shared by measurement and recorded drawing tests, without windows.
        .UseWindowingSubsystem(() => { }, "Schematic rendering tests")
        .UseStandardRuntimePlatformSubsystem().UseSkia().UseHarfBuzz().SetupWithoutStarting();
}
