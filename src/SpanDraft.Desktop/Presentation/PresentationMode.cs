namespace SpanDraft.Desktop.Presentation;

public enum PresentationMode
{
    Standard,
    Detailed
}

/// <summary>Semantic quantities keep unit selection separate from result precision.</summary>
public enum QuantityKind
{
    BeamLength,
    SectionDimension,
    TransverseDisplacement,
    AxialDisplacement,
    Area,
    SecondMomentOfArea,
    SectionModulus,
    Rotation,
    TransverseForce,
    AxialForce,
    Moment,
    DistributedLoad,
    Stress,
    SafetyFactor
}

public enum UnitDimension
{
    Length,
    Area,
    SecondMomentOfArea,
    SectionModulus,
    Rotation,
    Force,
    Moment,
    DistributedLoad,
    Pressure,
    Dimensionless
}
