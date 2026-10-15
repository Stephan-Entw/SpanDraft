using SpanDraft.Core.Beams;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;

namespace SpanDraft.Desktop.State;

/// <summary>The sole committed UI document. Input buffers are held separately.</summary>
public sealed record EditorDocument
{
    public EditorDocument(Length length, Material material, Section section,
        IEnumerable<EditorSupport>? supports = null, IEnumerable<EditorPointLoad>? loads = null, NamingState? namingState = null,
        IEnumerable<EditorUniformDistributedLoad>? distributedLoads = null, string? sectionDisplayName = null)
        : this(length, material, section, SectionAxisDesignation.Y, supports, loads, namingState, distributedLoads, sectionDisplayName)
    {
    }

    public EditorDocument(Length length, Material material, ISectionDefinition section, SectionAxisDesignation bendingAxis,
        IEnumerable<EditorSupport>? supports = null, IEnumerable<EditorPointLoad>? loads = null, NamingState? namingState = null,
        IEnumerable<EditorUniformDistributedLoad>? distributedLoads = null, string? sectionDisplayName = null)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(section);
        if (!Enum.IsDefined(bendingAxis))
            throw new ArgumentOutOfRangeException(nameof(bendingAxis), bendingAxis, "Unknown bending axis.");
        SectionAxisProperties axis;
        try { axis = section.GetAxis(bendingAxis); }
        catch (KeyNotFoundException e)
        { throw new ArgumentException("The section does not provide the selected bending axis.", nameof(bendingAxis), e); }
        if (axis is null || axis.AxisDesignation != bendingAxis)
            throw new ArgumentException("The section did not resolve the selected bending axis.", nameof(bendingAxis));
        BendingAxis = bendingAxis;
        Length = length;
        Material = material;
        Section = section;
        SectionDisplayName = sectionDisplayName;
        Supports = Array.AsReadOnly((supports ?? []).ToArray());
        Loads = Array.AsReadOnly((loads ?? []).ToArray());
        DistributedLoads = Array.AsReadOnly((distributedLoads ?? []).ToArray());
        NamingState = namingState ?? new();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in NamedEntities)
            if (!names.Add(EntityNaming.Normalize(entity.Name)))
                throw new ArgumentException("Entity names must be unique across the project.", nameof(supports));
    }

    public Length Length { get; init; }
    public Material Material { get; init; }
    public ISectionDefinition Section { get; }
    public SectionAxisDesignation BendingAxis { get; }
    private readonly string? _sectionDisplayName;
    /// <summary>Null selects a generated name; explicit names are desktop metadata.</summary>
    public string? SectionDisplayName
    {
        get => _sectionDisplayName;
        init
        {
            if (value is not null && (string.IsNullOrWhiteSpace(value) || value != value.Trim()))
                throw new ArgumentException("Section display names must be nonempty and trimmed.", nameof(SectionDisplayName));
            _sectionDisplayName = value;
        }
    }
    public IReadOnlyList<EditorSupport> Supports { get; }
    public IReadOnlyList<EditorPointLoad> Loads { get; }
    public IReadOnlyList<EditorUniformDistributedLoad> DistributedLoads { get; }
    public NamingState NamingState { get; }
    public IEnumerable<(Guid Id, string Name)> NamedEntities =>
        Supports.Select(s => (s.Id, s.Name)).Concat(Loads.Select(l => (l.Id, l.Name)))
            .Concat(DistributedLoads.Select(l => (l.Id, l.Name)));

    public EditorDocument WithSupports(IEnumerable<EditorSupport> supports, NamingState? namingState = null) =>
        new(Length, Material, Section, BendingAxis, supports, Loads, NextNamingState(namingState), DistributedLoads, SectionDisplayName);

    public EditorDocument WithLoads(IEnumerable<EditorPointLoad> loads, NamingState? namingState = null) =>
        new(Length, Material, Section, BendingAxis, Supports, loads, NextNamingState(namingState), DistributedLoads, SectionDisplayName);

    public EditorDocument WithDistributedLoads(IEnumerable<EditorUniformDistributedLoad> loads, NamingState? namingState = null) =>
        new(Length, Material, Section, BendingAxis, Supports, Loads, NextNamingState(namingState), loads, SectionDisplayName);

    public EditorDocument WithSection(ISectionDefinition section, SectionAxisDesignation bendingAxis) =>
        WithSection(section, bendingAxis, SectionDisplayName);

    public EditorDocument WithSection(ISectionDefinition section, SectionAxisDesignation bendingAxis, string? sectionDisplayName) =>
        new(Length, Material, section, bendingAxis, Supports, Loads, NamingState, DistributedLoads, sectionDisplayName);

    public EditorDocument WithBendingAxis(SectionAxisDesignation bendingAxis) => WithSection(Section, bendingAxis);

    private NamingState NextNamingState(NamingState? requested)
    {
        var next = requested ?? NamingState;
        if (next.NextSupportOrdinal < NamingState.NextSupportOrdinal || next.NextForceNumber < NamingState.NextForceNumber
            || next.NextMomentNumber < NamingState.NextMomentNumber
            || next.NextDistributedLoadNumber < NamingState.NextDistributedLoadNumber)
            throw new ArgumentException("Project naming counters cannot move backwards.", nameof(requested));
        return next;
    }

    public BeamModel ToBeamModel() => new(Length, Material, Section, BendingAxis,
        Supports.Select(s => new Support(s.Position, s.Type)),
        Loads.Select(l => l.ToCore()).Concat(DistributedLoads.Select(l => l.ToCore())));
}
