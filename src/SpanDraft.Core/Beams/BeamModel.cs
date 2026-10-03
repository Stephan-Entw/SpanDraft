using SpanDraft.Core.Loads;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;

namespace SpanDraft.Core.Beams;

/// <summary>
/// A single straight beam with constant material and section. Local x runs rightwards
/// from zero at the left end; transverse positive is upwards; positive rotation and
/// moment are counterclockwise. Cross-object consistency is checked by BeamModelValidator.
/// </summary>
public sealed class BeamModel
{
    public BeamModel(Length length, Material material, Section section,
        IEnumerable<Support> supports, IEnumerable<BeamLoad> loads)
    {
        DomainGuard.Positive(length.Meters, nameof(length));
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(supports);
        ArgumentNullException.ThrowIfNull(loads);

        Support[] supportArray = supports.ToArray();
        BeamLoad[] loadArray = loads.ToArray();
        if (supportArray.Any(support => support is null))
            throw new ArgumentException("Supports must not contain null entries.", nameof(supports));
        if (loadArray.Any(load => load is null))
            throw new ArgumentException("Loads must not contain null entries.", nameof(loads));

        Length = length;
        Material = material;
        Section = section;
        Supports = Array.AsReadOnly(supportArray);
        Loads = Array.AsReadOnly(loadArray);
    }

    public Length Length { get; }
    public Material Material { get; }
    public Section Section { get; }
    public IReadOnlyList<Support> Supports { get; }
    public IReadOnlyList<BeamLoad> Loads { get; }
}
