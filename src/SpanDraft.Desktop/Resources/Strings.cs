using System.Globalization;
using System.Resources;

namespace SpanDraft.Desktop.Resources;

public static class Strings
{
    public static string DistributedStartLabel => Get(nameof(DistributedStartLabel));
    public static string DistributedEndLabel => Get(nameof(DistributedEndLabel));
    public static string DistributedIntensityLabel => Get(nameof(DistributedIntensityLabel));
    public static string InvalidDistributedRange => Get(nameof(InvalidDistributedRange));
    public static string EntityNameLabel => Get(nameof(EntityNameLabel));
    public static string SchematicDrawing => Get(nameof(SchematicDrawing));
    public static string EmptyEntityName => Get(nameof(EmptyEntityName));
    public static string DuplicateEntityName => Get(nameof(DuplicateEntityName));
    private static readonly ResourceManager ResourceManager =
        new("SpanDraft.Desktop.Resources.Strings", typeof(Strings).Assembly);

    public static string ForceValueLabel => Get(nameof(ForceValueLabel));
    public static string MomentValueLabel => Get(nameof(MomentValueLabel));
    public static string InvalidLoadValue => Get(nameof(InvalidLoadValue));
    public static string LengthExcludesEntities => Get(nameof(LengthExcludesEntities));
    public static string Support => Get(nameof(Support));
    public static string SupportTypeLabel => Get(nameof(SupportTypeLabel));
    public static string Position => Get(nameof(Position));
    public static string OK => Get(nameof(OK));
    public static string Delete => Get(nameof(Delete));
    public static string SupportAlreadyExists => Get(nameof(SupportAlreadyExists));
    public static string PositionInsideBeam => Get(nameof(PositionInsideBeam));
    public static string LengthExcludesSupports => Get(nameof(LengthExcludesSupports));
    public static string InvalidSupportType => Get(nameof(InvalidSupportType));
    public static string SupportCoordinate => Get(nameof(SupportCoordinate));

    private static string Get(string name) =>
        ResourceManager.GetString(name, CultureInfo.CurrentUICulture)
        ?? throw new MissingManifestResourceException(name);

    public static string ApplicationTitle => Get(nameof(ApplicationTitle));
    public static string File => Get(nameof(File));
    public static string Edit => Get(nameof(Edit));
    public static string View => Get(nameof(View));
    public static string Help => Get(nameof(Help));
    public static string Settings => Get(nameof(Settings));
    public static string NewProject => Get(nameof(NewProject));
    public static string EditProject => Get(nameof(EditProject));
    public static string Section => Get(nameof(Section));
    public static string Material => Get(nameof(Material));
    public static string CreateProject => Get(nameof(CreateProject));
    public static string Apply => Get(nameof(Apply));
    public static string Cancel => Get(nameof(Cancel));
    public static string Change => Get(nameof(Change));
    public static string BeamLength => Get(nameof(BeamLength));
    public static string PointForce => Get(nameof(PointForce));
    public static string Moment => Get(nameof(Moment));
    public static string DistributedLoad => Get(nameof(DistributedLoad));
    public static string FixedSupport => Get(nameof(FixedSupport));
    public static string PinnedSupport => Get(nameof(PinnedSupport));
    public static string RollerSupport => Get(nameof(RollerSupport));
    public static string Results => Get(nameof(Results));
    public static string CalculationUnavailable => Get(nameof(CalculationUnavailable));
    public static string SupportsMissing => Get(nameof(SupportsMissing));
    public static string InsufficientSupport => Get(nameof(InsufficientSupport));
    public static string IncompleteModel => Get(nameof(IncompleteModel));
    public static string CalculationNotPossible => Get(nameof(CalculationNotPossible));
    public static string CalculationComplete => Get(nameof(CalculationComplete));
    public static string InvalidLength => Get(nameof(InvalidLength));
    public static string SectionTemplateName => Get(nameof(SectionTemplateName));
    public static string SectionTemplateNote => Get(nameof(SectionTemplateNote));
    public static string MaterialTemplateNote => Get(nameof(MaterialTemplateNote));
    public static string SetupDescription => Get(nameof(SetupDescription));
    public static string EditLength => Get(nameof(EditLength));
    public static string Open => Get(nameof(Open));
    public static string Save => Get(nameof(Save));
    public static string Undo => Get(nameof(Undo));
    public static string Redo => Get(nameof(Redo));
    public static string Zoom => Get(nameof(Zoom));
    public static string About => Get(nameof(About));
    public static string ResultsPending => Get(nameof(ResultsPending));
    public static string DisplacementSymbol => Get(nameof(DisplacementSymbol));
    public static string MomentSymbol => Get(nameof(MomentSymbol));
    public static string StressSymbol => Get(nameof(StressSymbol));
    public static string SafetySymbol => Get(nameof(SafetySymbol));
}
