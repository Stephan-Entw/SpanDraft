using System.Collections.ObjectModel;

namespace SpanDraft.Desktop.Presentation;

public enum UnitProfileKind
{
    MechanicalEngineering,
    StructuralEngineering,
    UnitedStates,
    Custom
}

/// <summary>Immutable complete unit selection; individual changes create a custom profile.</summary>
public sealed class UnitProfile
{
    public UnitProfile(IReadOnlyDictionary<QuantityKind, UnitDefinition> units) : this(UnitProfileKind.Custom, units) { }

    private UnitProfile(UnitProfileKind kind, IReadOnlyDictionary<QuantityKind, UnitDefinition> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        if (units.Count != Enum.GetValues<QuantityKind>().Length)
            throw new ArgumentException("A profile must specify every quantity exactly once.", nameof(units));
        var copy = new Dictionary<QuantityKind, UnitDefinition>();
        foreach (QuantityKind quantity in Enum.GetValues<QuantityKind>())
        {
            if (!units.TryGetValue(quantity, out var unit))
                throw new ArgumentException("A profile is missing a quantity.", nameof(units));
            UnitCatalog.Validate(quantity, unit);
            copy.Add(quantity, unit);
        }
        Kind = kind;
        Units = new ReadOnlyDictionary<QuantityKind, UnitDefinition>(copy);
    }

    public UnitProfileKind Kind { get; }
    public IReadOnlyDictionary<QuantityKind, UnitDefinition> Units { get; }
    public UnitDefinition this[QuantityKind kind] => Units[kind];

    public UnitProfile WithUnit(QuantityKind kind, UnitDefinition unit)
    {
        UnitCatalog.Validate(kind, unit);
        var copy = new Dictionary<QuantityKind, UnitDefinition>(Units) { [kind] = unit };
        return new UnitProfile(copy);
    }

    public static UnitProfile Default => MechanicalEngineering;
    public static UnitProfile MechanicalEngineering { get; } = Create(UnitProfileKind.MechanicalEngineering);
    public static UnitProfile StructuralEngineering { get; } = Create(UnitProfileKind.StructuralEngineering);
    public static UnitProfile UnitedStates { get; } = Create(UnitProfileKind.UnitedStates);

    private static UnitProfile Create(UnitProfileKind kind)
    {
        bool us = kind == UnitProfileKind.UnitedStates;
        bool civil = kind == UnitProfileKind.StructuralEngineering;
        var units = new Dictionary<QuantityKind, UnitDefinition>
        {
            [QuantityKind.BeamLength] = us ? UnitCatalog.Foot : civil ? UnitCatalog.Meter : UnitCatalog.Millimeter,
            [QuantityKind.SectionDimension] = us ? UnitCatalog.Inch : UnitCatalog.Millimeter,
            [QuantityKind.TransverseDisplacement] = us ? UnitCatalog.Inch : UnitCatalog.Millimeter,
            [QuantityKind.AxialDisplacement] = us ? UnitCatalog.Inch : UnitCatalog.Millimeter,
            [QuantityKind.Area] = us ? UnitCatalog.SquareInch : civil ? UnitCatalog.SquareCentimeter : UnitCatalog.SquareMillimeter,
            [QuantityKind.SecondMomentOfArea] = us ? UnitCatalog.InchToFourth : civil ? UnitCatalog.CentimeterToFourth : UnitCatalog.MillimeterToFourth,
            [QuantityKind.SectionModulus] = us ? UnitCatalog.CubicInch : civil ? UnitCatalog.CubicCentimeter : UnitCatalog.CubicMillimeter,
            [QuantityKind.Rotation] = UnitCatalog.Radian,
            [QuantityKind.TransverseForce] = us ? UnitCatalog.Kip : civil ? UnitCatalog.Kilonewton : UnitCatalog.Newton,
            [QuantityKind.AxialForce] = us ? UnitCatalog.Kip : civil ? UnitCatalog.Kilonewton : UnitCatalog.Newton,
            [QuantityKind.Moment] = us ? UnitCatalog.KipFoot : civil ? UnitCatalog.KilonewtonMeter : UnitCatalog.NewtonMeter,
            [QuantityKind.DistributedLoad] = us ? UnitCatalog.KipPerFoot : civil ? UnitCatalog.KilonewtonPerMeter : UnitCatalog.NewtonPerMeter,
            [QuantityKind.Stress] = us ? UnitCatalog.Ksi : UnitCatalog.Megapascal,
            [QuantityKind.SafetyFactor] = UnitCatalog.Dimensionless
        };
        return new UnitProfile(kind, units);
    }
}
