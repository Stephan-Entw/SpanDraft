using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Tests;

internal static class PresentationTestProfiles
{
    internal static UnitProfile Mixed => UnitProfile.UnitedStates
        .WithUnit(QuantityKind.AxialForce, UnitCatalog.Newton)
        .WithUnit(QuantityKind.Moment, UnitCatalog.KilonewtonMeter)
        .WithUnit(QuantityKind.TransverseDisplacement, UnitCatalog.Meter);

    internal static UnitProfile Profile(int index) => index switch
    {
        0 => UnitProfile.Default, 1 => UnitProfile.StructuralEngineering,
        2 => UnitProfile.UnitedStates, _ => Mixed
    };
}
