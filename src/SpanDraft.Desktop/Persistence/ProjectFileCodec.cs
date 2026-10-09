using System.Text.Json;
using System.Text.Json.Serialization;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Sections.Parametric;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Core.Validation;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Persistence;

public sealed class ProjectFormatException(string message, Exception? inner = null) : Exception(message, inner);

public static class ProjectFileCodec
{
    public const string Format = "SpanDraft.Project";
    public const int Version = 2;
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        RespectNullableAnnotations = true,
        AllowDuplicateProperties = false
    };

    public static byte[] Serialize(ProjectState state) => JsonSerializer.SerializeToUtf8Bytes(ToDto(state), JsonOptions);

    public static ProjectState Deserialize(ReadOnlySpan<byte> json)
    {
        try
        {
            var root = JsonElement.Parse(json, new JsonDocumentOptions { AllowDuplicateProperties = false });
            if (root.ValueKind != JsonValueKind.Object)
                throw new ProjectFormatException("Project root must be an object.");
            if (!root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String
                || format.GetString() != Format)
                throw new ProjectFormatException("Not a SpanDraft project.");
            if (!root.TryGetProperty("formatVersion", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var number))
                throw new ProjectFormatException("Missing or invalid project format version.");
            return number switch
            {
                1 => FromDto(root.Deserialize<ProjectFileV1>(JsonOptions)!),
                2 => FromDto(root.Deserialize<ProjectFileV2>(JsonOptions)!),
                _ => throw new ProjectFormatException("Unsupported project format version.")
            };
        }
        catch (Exception e) when (e is JsonException or ArgumentException or OverflowException)
        {
            throw new ProjectFormatException("Invalid project data: " + e.Message, e);
        }
    }

    public static ProjectState FromDto(ProjectFileV1 file) => FromDto(ProjectFileV1Reader.Migrate(file));

    public static ProjectFileV2 ToDto(ProjectState state)
    {
        var d = state.Document;
        var n = d.NamingState;
        return new()
        {
            Format = Format, FormatVersion = Version,
            Document = new()
            {
                Length = d.Length.Meters, BendingAxis = AxisToWire(d.BendingAxis),
                Material = new() { Name = d.Material.Name, YoungsModulus = d.Material.YoungsModulus.Pascals,
                    YieldStrength = d.Material.YieldStrength.Pascals, Density = d.Material.Density?.KilogramsPerCubicMeter,
                    PoissonRatio = d.Material.PoissonRatio?.Value },
                Section = SectionToDto(d.Section),
                Supports = d.Supports.Select(s => new SupportV2 { Id = s.Id, Name = s.Name,
                    Type = s.Type switch { SupportType.Fixed => "fixed", SupportType.Pinned => "pinned",
                        SupportType.Roller => "roller", _ => throw new ProjectFormatException("Unknown support type.") },
                    Position = s.Position.Meters }).ToArray(),
                PointLoads = d.Loads.Select(l => new PointLoadV2 { Id = l.Id, Name = l.Name,
                    Type = l.Kind switch { PointLoadKind.Force => "force", PointLoadKind.Moment => "moment",
                        _ => throw new ProjectFormatException("Unknown point load type.") },
                    Position = l.Position.Meters, Value = l.Value }).ToArray(),
                DistributedLoads = d.DistributedLoads.Select(l => new DistributedLoadV2 { Id = l.Id, Name = l.Name,
                    StartPosition = l.StartPosition.Meters, EndPosition = l.EndPosition.Meters,
                    Intensity = l.Intensity.NewtonsPerMeter }).ToArray(),
                NamingState = new() { NextSupportOrdinal = n.NextSupportOrdinal, NextForceNumber = n.NextForceNumber,
                    NextMomentNumber = n.NextMomentNumber, NextDistributedLoadNumber = n.NextDistributedLoadNumber }
            },
            Presentation = new() { AnnotationOffsets = state.Presentation.AnnotationOffsets.Select(o =>
                new OffsetV2 { EntityId = o.Key, Dx = o.Value.Dx, Dy = o.Value.Dy }).ToArray() }
        };
    }

    internal static SectionV2 SectionToDto(ISectionDefinition section) =>
        SectionDefinitionCompatibility.Normalize(section) switch
        {
            RectangleSectionGeometry s => new RectangleSectionV2
            { Width = s.Width.Meters, Height = s.Height.Meters },
            RectangularHollowSectionGeometry s => new RectangularHollowSectionV2
            { Width = s.Width.Meters, Height = s.Height.Meters, WallThickness = s.WallThickness.Meters, OuterRadius = s.OuterRadius.Meters },
            CircleSectionGeometry s => new CircleSectionV2
            { Diameter = s.Diameter.Meters },
            CircularHollowSectionGeometry s => new CircularHollowSectionV2
            { OuterDiameter = s.OuterDiameter.Meters, WallThickness = s.WallThickness.Meters },
            ISectionGeometry s => new ISectionV2
            { Height = s.Height.Meters, Width = s.Width.Meters, WebThickness = s.WebThickness.Meters, FlangeThickness = s.FlangeThickness.Meters, Radius = s.Radius.Meters },
            USectionGeometry s => new USectionV2
            { Height = s.Height.Meters, Width = s.Width.Meters, WebThickness = s.WebThickness.Meters, FlangeThickness = s.FlangeThickness.Meters, Radius = s.Radius.Meters },
            TSectionGeometry s => new TSectionV2
            { Height = s.Height.Meters, Width = s.Width.Meters, WebThickness = s.WebThickness.Meters, FlangeThickness = s.FlangeThickness.Meters, Radius = s.Radius.Meters },
            AngleSectionGeometry s => new AngleSectionV2
            { Width = s.Width.Meters, Height = s.Height.Meters, Thickness = s.Thickness.Meters, InnerRadius = s.InnerRadius.Meters },
            ManualSectionDefinition s => new ManualSectionV2
            {
                Area = s.Area.SquareMeters,
                Axes = s.Axes.Select(a => new ManualAxisV2 { Designation = AxisToWire(a.AxisDesignation),
                    SecondMomentOfArea = a.SecondMomentOfArea.MetersToTheFourth,
                    SectionModulus = a.PositiveSectionModulus.CubicMeters }).ToArray()
            },
            _ => throw new ProjectFormatException("Unknown section type.")
        };

    private static ISectionDefinition SectionFromDto(SectionV2 section) => section switch
    {
        RectangleSectionV2 s => new RectangleSectionGeometry(L(s.Width, "width"), L(s.Height, "height")),
        RectangularHollowSectionV2 s => new RectangularHollowSectionGeometry(L(s.Width, "width"), L(s.Height, "height"), L(s.WallThickness, "wallThickness"), L(s.OuterRadius, "outerRadius")),
        CircleSectionV2 s => new CircleSectionGeometry(L(s.Diameter, "diameter")),
        CircularHollowSectionV2 s => new CircularHollowSectionGeometry(L(s.OuterDiameter, "outerDiameter"), L(s.WallThickness, "wallThickness")),
        ISectionV2 s => new ISectionGeometry(L(s.Height, "height"), L(s.Width, "width"), L(s.WebThickness, "webThickness"), L(s.FlangeThickness, "flangeThickness"), L(s.Radius, "radius")),
        USectionV2 s => new USectionGeometry(L(s.Height, "height"), L(s.Width, "width"), L(s.WebThickness, "webThickness"), L(s.FlangeThickness, "flangeThickness"), L(s.Radius, "radius")),
        TSectionV2 s => new TSectionGeometry(L(s.Height, "height"), L(s.Width, "width"), L(s.WebThickness, "webThickness"), L(s.FlangeThickness, "flangeThickness"), L(s.Radius, "radius")),
        AngleSectionV2 s => new AngleSectionGeometry(L(s.Width, "width"), L(s.Height, "height"), L(s.Thickness, "thickness"), L(s.InnerRadius, "innerRadius")),
        ManualSectionV2 s => ManualFromDto(s),
        _ => throw new ProjectFormatException("Unknown section type.")
    };

    private static ManualSectionDefinition ManualFromDto(ManualSectionV2 section)
    {
        var axes = Required(section.Axes, "axes");
        if (axes.Length is < 1 or > 2) throw new ProjectFormatException("Manual sections require one or two axes.");
        ManualSectionAxis Map(ManualAxisV2 entry)
        {
            var a = Required(entry, "axis");
            return new(AxisFromWire(a.Designation),
                SecondMomentOfArea.FromMetersToTheFourth(Number(a.SecondMomentOfArea, "secondMomentOfArea")),
                SectionModulus.FromCubicMeters(Number(a.SectionModulus, "sectionModulus")));
        }
        return new(Area.FromSquareMeters(Number(section.Area, "area")), Map(axes[0]), axes.Length == 2 ? Map(axes[1]) : null);
    }

    internal static string AxisToWire(SectionAxisDesignation axis) => axis switch
    {
        SectionAxisDesignation.Y => "y", SectionAxisDesignation.Z => "z",
        SectionAxisDesignation.U => "u", SectionAxisDesignation.V => "v",
        _ => throw new ProjectFormatException("Unknown section axis.")
    };

    private static SectionAxisDesignation AxisFromWire(string value) => value switch
    {
        "y" => SectionAxisDesignation.Y, "z" => SectionAxisDesignation.Z,
        "u" => SectionAxisDesignation.U, "v" => SectionAxisDesignation.V,
        _ => throw new ProjectFormatException("Unknown section axis.")
    };

    public static ProjectState FromDto(ProjectFileV2 file)
    {
        try { return MapValidated(file); }
        catch (Exception e) when (e is ArgumentException or OverflowException)
        { throw new ProjectFormatException("Invalid project data: " + e.Message, e); }
    }

    private static ProjectState MapValidated(ProjectFileV2 file)
    {
        Required(file, "project");
        if (file.Format != Format) throw new ProjectFormatException("Not a SpanDraft project.");
        if (file.FormatVersion != Version) throw new ProjectFormatException("Unsupported project format version.");
        var d = Required(file.Document, "document");
        var m = Required(d.Material, "material");
        var s = Required(d.Section, "section");
        var n = Required(d.NamingState, "namingState");
        var p = Required(file.Presentation, "presentation");
        var ids = new HashSet<Guid>();
        Guid EntityId(Guid id)
        {
            if (id == Guid.Empty || !ids.Add(id)) throw new ProjectFormatException("Entity IDs must be non-empty and unique.");
            return id;
        }
        var section = SectionFromDto(s);
        var document = new EditorDocument(L(d.Length, "length"),
            new Material(Required(m.Name, "material.name"), Pressure.FromPascals(Number(m.YoungsModulus, "youngsModulus")),
                Pressure.FromPascals(Number(m.YieldStrength, "yieldStrength")),
                m.Density is { } rho ? MassDensity.FromKilogramsPerCubicMeter(Number(rho, "density")) : null,
                m.PoissonRatio is { } nu ? PoissonRatio.FromValue(Number(nu, "poissonRatio")) : null),
            section, AxisFromWire(d.BendingAxis),
            Required(d.Supports, "supports").Select(entry =>
            {
                var x = Required(entry, "support");
                return new EditorSupport(EntityId(x.Id), L(x.Position, "position"), x.Type switch
                { "fixed" => SupportType.Fixed, "pinned" => SupportType.Pinned, "roller" => SupportType.Roller,
                    _ => throw new ProjectFormatException("Unknown support type.") }, Name(x.Name));
            }),
            Required(d.PointLoads, "pointLoads").Select(entry =>
            {
                var x = Required(entry, "pointLoad");
                return EditorPointLoad.Create(EntityId(x.Id), L(x.Position, "position"), x.Type switch
                { "force" => PointLoadKind.Force, "moment" => PointLoadKind.Moment,
                    _ => throw new ProjectFormatException("Unknown point load type.") }, Number(x.Value, "value"), Name(x.Name));
            }),
            new NamingState(n.NextSupportOrdinal, n.NextForceNumber, n.NextMomentNumber, n.NextDistributedLoadNumber),
            Required(d.DistributedLoads, "distributedLoads").Select(entry =>
            {
                var x = Required(entry, "distributedLoad");
                return new EditorUniformDistributedLoad(EntityId(x.Id), L(x.StartPosition, "startPosition"),
                    L(x.EndPosition, "endPosition"), ForcePerLength.FromNewtonsPerMeter(Number(x.Intensity, "intensity")), Name(x.Name));
            }));
        var errors = BeamModelValidator.Validate(document.ToBeamModel()).Where(e => e.Code != ValidationErrorCode.MissingSupports).ToArray();
        if (errors.Length > 0) throw new ProjectFormatException(errors[0].Message);
        if (document.Supports.Select(x => x.Position).Distinct().Count() != document.Supports.Count)
            throw new ProjectFormatException("Support positions must be unique.");
        var offsets = new Dictionary<Guid, AnnotationOffset>();
        foreach (var entry in Required(p.AnnotationOffsets, "annotationOffsets"))
        {
            var x = Required(entry, "annotationOffset");
            if (!ids.Contains(x.EntityId) || !offsets.TryAdd(x.EntityId, new(Number(x.Dx, "dx"), Number(x.Dy, "dy"))))
                throw new ProjectFormatException("Annotation offsets must reference existing, unique entities.");
        }
        return new(document, new(offsets));
    }

    internal static T Required<T>(T? value, string field) where T : class => value
        ?? throw new ProjectFormatException("Missing or null field: " + field);
    internal static double Number(double? value, string field) => value is { } v && double.IsFinite(v) ? v
        : throw new ProjectFormatException("Missing or non-finite number: " + field);
    private static Length L(double? value, string field) => Length.FromMeters(Number(value, field));
    private static string Name(string? value)
    {
        var name = Required(value, "name");
        if (EntityNaming.Normalize(name) != name) throw new ProjectFormatException("Entity names must be trimmed.");
        return name;
    }
}
