namespace SpanDraft.Desktop.Presentation;

/// <summary>An immutable display unit; the scale is the number of canonical SI units per display unit.</summary>
public sealed class UnitDefinition
{
    internal UnitDefinition(string id, string symbol, UnitDimension dimension, PresentationNumber scale)
    {
        Id = id;
        Symbol = symbol;
        Dimension = dimension;
        Scale = scale;
    }

    public string Id { get; }
    public string Symbol { get; }
    public UnitDimension Dimension { get; }
    public double SiUnitsPerUnit => Scale.ToDouble();
    internal PresentationNumber Scale { get; }

    /// <summary>Converts a finite value. Throws when the nonzero result cannot fit in double.</summary>
    public double ToSi(double displayValue) => Convert(PresentationNumber.FromDouble(displayValue) * Scale);

    /// <summary>Converts a finite value. The result formatter uses extended-range arithmetic instead.</summary>
    public double FromSi(double siValue) => Convert(PresentationNumber.FromDouble(siValue) / Scale);

    private static double Convert(PresentationNumber value)
    {
        double result = value.ToDouble();
        if (!double.IsFinite(result) || (result == 0 && !value.IsZero))
            throw new OverflowException("The converted value is outside double's nonzero finite range.");
        return result;
    }
}

/// <summary>Canonical SI units and all units used by the built-in profiles. Symbols are language-independent.</summary>
public static class UnitCatalog
{
    private static readonly PresentationNumber InchScale = PresentationNumber.Parse("0.0254");
    private static readonly PresentationNumber FootScale = PresentationNumber.Parse("0.3048");
    // NIST SP 811: lbf = 0.45359237 kg · 9.80665 m/s², exactly; kip = 1000 lbf.
    private static readonly PresentationNumber KipScale = PresentationNumber.Parse("4448.2216152605");

    private static UnitDefinition Unit(string id, string symbol, UnitDimension dimension, string scale) =>
        new(id, symbol, dimension, PresentationNumber.Parse(scale));

    public static UnitDefinition Meter { get; } = Unit("m", "m", UnitDimension.Length, "1");
    public static UnitDefinition Millimeter { get; } = Unit("mm", "mm", UnitDimension.Length, "0.001");
    public static UnitDefinition Foot { get; } = new("ft", "ft", UnitDimension.Length, FootScale);
    public static UnitDefinition Inch { get; } = new("in", "in", UnitDimension.Length, InchScale);
    public static UnitDefinition SquareMeter { get; } = Unit("m2", "m²", UnitDimension.Area, "1");
    public static UnitDefinition SquareMillimeter { get; } = Unit("mm2", "mm²", UnitDimension.Area, "1e-6");
    public static UnitDefinition SquareCentimeter { get; } = Unit("cm2", "cm²", UnitDimension.Area, "1e-4");
    public static UnitDefinition SquareInch { get; } = new("in2", "in²", UnitDimension.Area, InchScale * InchScale);
    public static UnitDefinition MeterToFourth { get; } = Unit("m4", "m⁴", UnitDimension.SecondMomentOfArea, "1");
    public static UnitDefinition MillimeterToFourth { get; } = Unit("mm4", "mm⁴", UnitDimension.SecondMomentOfArea, "1e-12");
    public static UnitDefinition CentimeterToFourth { get; } = Unit("cm4", "cm⁴", UnitDimension.SecondMomentOfArea, "1e-8");
    public static UnitDefinition InchToFourth { get; } = new("in4", "in⁴", UnitDimension.SecondMomentOfArea,
        InchScale * InchScale * InchScale * InchScale);
    public static UnitDefinition CubicMeter { get; } = Unit("m3", "m³", UnitDimension.SectionModulus, "1");
    public static UnitDefinition CubicMillimeter { get; } = Unit("mm3", "mm³", UnitDimension.SectionModulus, "1e-9");
    public static UnitDefinition CubicCentimeter { get; } = Unit("cm3", "cm³", UnitDimension.SectionModulus, "1e-6");
    public static UnitDefinition CubicInch { get; } = new("in3", "in³", UnitDimension.SectionModulus,
        InchScale * InchScale * InchScale);
    public static UnitDefinition Radian { get; } = Unit("rad", "rad", UnitDimension.Rotation, "1");
    public static UnitDefinition Newton { get; } = Unit("N", "N", UnitDimension.Force, "1");
    public static UnitDefinition Kilonewton { get; } = Unit("kN", "kN", UnitDimension.Force, "1000");
    public static UnitDefinition Kip { get; } = new("kip", "kip", UnitDimension.Force, KipScale);
    public static UnitDefinition NewtonMeter { get; } = Unit("Nm", "N·m", UnitDimension.Moment, "1");
    public static UnitDefinition KilonewtonMeter { get; } = Unit("kNm", "kN·m", UnitDimension.Moment, "1000");
    public static UnitDefinition KipFoot { get; } = new("kipft", "kip·ft", UnitDimension.Moment, KipScale * FootScale);
    public static UnitDefinition NewtonPerMeter { get; } = Unit("N/m", "N/m", UnitDimension.DistributedLoad, "1");
    public static UnitDefinition KilonewtonPerMeter { get; } = Unit("kN/m", "kN/m", UnitDimension.DistributedLoad, "1000");
    public static UnitDefinition KipPerFoot { get; } = new("kip/ft", "kip/ft", UnitDimension.DistributedLoad, KipScale / FootScale);
    public static UnitDefinition Pascal { get; } = Unit("Pa", "Pa", UnitDimension.Pressure, "1");
    public static UnitDefinition Megapascal { get; } = Unit("MPa", "MPa", UnitDimension.Pressure, "1e6");
    public static UnitDefinition Ksi { get; } = new("ksi", "ksi", UnitDimension.Pressure, KipScale / (InchScale * InchScale));
    public static UnitDefinition Dimensionless { get; } = Unit("1", "", UnitDimension.Dimensionless, "1");

    public static IReadOnlyList<UnitDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        Meter, Millimeter, Foot, Inch, SquareMeter, SquareMillimeter, SquareCentimeter, SquareInch,
        MeterToFourth, MillimeterToFourth, CentimeterToFourth, InchToFourth,
        CubicMeter, CubicMillimeter, CubicCentimeter, CubicInch, Radian,
        Newton, Kilonewton, Kip, NewtonMeter, KilonewtonMeter, KipFoot,
        NewtonPerMeter, KilonewtonPerMeter, KipPerFoot, Pascal, Megapascal, Ksi, Dimensionless
    });

    public static UnitDimension DimensionOf(QuantityKind kind) => kind switch
    {
        QuantityKind.BeamLength or QuantityKind.SectionDimension or QuantityKind.TransverseDisplacement
            or QuantityKind.AxialDisplacement => UnitDimension.Length,
        QuantityKind.Area => UnitDimension.Area,
        QuantityKind.SecondMomentOfArea => UnitDimension.SecondMomentOfArea,
        QuantityKind.SectionModulus => UnitDimension.SectionModulus,
        QuantityKind.Rotation => UnitDimension.Rotation,
        QuantityKind.TransverseForce or QuantityKind.AxialForce => UnitDimension.Force,
        QuantityKind.Moment => UnitDimension.Moment,
        QuantityKind.DistributedLoad => UnitDimension.DistributedLoad,
        QuantityKind.Stress => UnitDimension.Pressure,
        QuantityKind.SafetyFactor => UnitDimension.Dimensionless,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    internal static void Validate(QuantityKind kind, UnitDefinition unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (unit.Dimension != DimensionOf(kind))
            throw new ArgumentException("The unit is incompatible with the quantity.", nameof(unit));
    }
}
