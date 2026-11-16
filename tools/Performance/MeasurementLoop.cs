using SpanDraft.Desktop.Diagnostics;

namespace SpanDraft.Performance;

internal static class MeasurementLoop
{
    public static async Task<ActionResult> RunAsync(string scenario, string action, int warmup, int iterations,
        Func<Task<InteractionPerformanceEntry?>> measure, Func<Task<InteractionPerformanceEntry?>> reset)
    {
        int preparationDiscarded = 0, warmupDiscarded = 0, discarded = 0;
        var samples = new List<PhaseValues>();
        // Prime the same history shape that every later reset restores: zero undo entries, one redo entry.
        if (await measure() is null) preparationDiscarded++;
        if (await reset() is null) preparationDiscarded++;
        for (long i = 0; i < (long)warmup + iterations; i++)
        {
            var entry = await measure();
            if (i < warmup) { if (entry is null) warmupDiscarded++; }
            else if (entry is null) discarded++;
            else samples.Add(PhaseValues.From(entry));
            if (await reset() is null) preparationDiscarded++;
        }
        return ActionResult.Create(scenario, action, samples, discarded, warmupDiscarded, preparationDiscarded);
    }
}
