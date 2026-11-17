using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.State;

/// <summary>
/// The one place that knows which supports may share a station and how the supports of a station become the Core
/// supports. In the editor every spring is a support of its own; only a translational and a rotational spring at the
/// same position are joined, and only here, when the Core model is built. Supports and moment releases are a different
/// rule: <see cref="MomentReleaseRules"/> decides that.
/// </summary>
internal static class SupportStations
{
    /// <summary>One Core support and the editor supports it was built from, in document order.</summary>
    public sealed record CoreSupport(Support Support, IReadOnlyList<Guid> EditorIds);

    /// <summary>A translational spring is a spring support whose only spring is translational and not held rigidly.</summary>
    public static bool IsTranslationalSpring(SupportType type, SpringData? spring) =>
        type == SupportType.Spring && spring is { Translational: not null, Rotational: null, RigidTranslation: false };

    /// <summary>A rotational spring is a spring support whose only spring is rotational; a rigid translation is separate.</summary>
    public static bool IsRotationalSpring(SupportType type, SpringData? spring) =>
        type == SupportType.Spring && spring is { Translational: null, Rotational: not null };

    /// <summary>
    /// Several supports stand at one position only as exactly one translational and one rotational spring, neither with
    /// a rigid translation. One support alone is always fine here; other rules (Core, hinges) apply on top.
    /// </summary>
    public static bool IsAllowedStation(IReadOnlyList<(SupportType Type, SpringData? Spring)> station) => station.Count switch
    {
        <= 1 => true,
        2 => IsPair(station[0].Type, station[0].Spring, station[1].Type, station[1].Spring),
        _ => false
    };

    private static bool IsPair(SupportType firstType, SpringData? first, SupportType secondType, SpringData? second) =>
        (IsTranslationalSpring(firstType, first) && IsRotationalSpring(secondType, second) && second is { RigidTranslation: false })
        || (IsRotationalSpring(firstType, first) && first is { RigidTranslation: false } && IsTranslationalSpring(secondType, second));

    /// <summary>Whether a support of this kind may stand at the position next to the other supports (without <paramref name="exclude"/>).</summary>
    public static bool Allows(IEnumerable<EditorSupport> supports, Length position, SupportType type, SpringData? spring, Guid? exclude = null)
    {
        var station = supports.Where(s => s.Id != exclude && s.Position.Meters == position.Meters)
            .Select(s => (s.Type, s.Spring)).Append((type, spring)).ToList();
        return IsAllowedStation(station);
    }

    /// <summary>
    /// The supports that break the station rule: at a station that does, every support after the first one in document order,
    /// as the one that would not have been accepted.
    /// </summary>
    public static IEnumerable<EditorSupport> Violations(IReadOnlyList<EditorSupport> supports) =>
        supports.GroupBy(s => s.Position.Meters)
            .Where(station => !IsAllowedStation([.. station.Select(s => (s.Type, s.Spring))]))
            .SelectMany(station => station.Skip(1));

    /// <summary>
    /// The Core supports of the editor supports, in one pass. A translational and a rotational spring at the same position
    /// become one Core support that stands where the first of the two stands; every other support stays as it is.
    /// </summary>
    public static IReadOnlyList<CoreSupport> ToCore(IReadOnlyList<EditorSupport> supports)
    {
        var stations = supports.GroupBy(s => s.Position.Meters).ToDictionary(g => g.Key, g => g.ToList());
        var result = new List<CoreSupport>();
        foreach (var support in supports)
        {
            var station = stations[support.Position.Meters];
            if (station.Count == 2 && IsAllowedStation([.. station.Select(s => (s.Type, s.Spring))]))
            {
                if (!ReferenceEquals(station[0], support)) continue;
                var translational = station.Single(s => s.Spring!.Translational is not null);
                var rotational = station.Single(s => s.Spring!.Rotational is not null);
                result.Add(new(new SpringData(translational.Spring!.Translational, rotational.Spring!.Rotational)
                    .ToCore(support.Position), station.Select(s => s.Id).ToArray()));
                continue;
            }
            result.Add(new(support.Type == SupportType.Spring
                ? (support.Spring ?? throw new InvalidOperationException("A spring support needs its springs.")).ToCore(support.Position)
                : new Support(support.Position, support.Type), [support.Id]));
        }
        return result;
    }
}
