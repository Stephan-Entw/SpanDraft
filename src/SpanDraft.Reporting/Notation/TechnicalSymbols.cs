namespace SpanDraft.Reporting.Notation;

/// <summary>The notation used by SpanDraft. Keys and engineering values remain outside this catalog.</summary>
public static class TechnicalSymbols
{
    private static TechnicalText Variable(string text) => new(new TechnicalRun(text, Role: TechnicalRole.Variable));
    private static TechnicalRun Index(string text, bool variable = false) =>
        new(text, TechnicalPosition.Subscript, variable ? TechnicalRole.Variable : TechnicalRole.Text);
    public static TechnicalText Area { get; } = Variable("A");
    public static TechnicalText YoungsModulus { get; } = Variable("E");
    public static TechnicalText Density { get; } = Variable("ρ");
    public static TechnicalText PoissonRatio { get; } = Variable("ν");
    public static TechnicalText SafetyFactor { get; } = Variable("S");
    public static TechnicalText Displacement { get; } = Variable("w");
    public static TechnicalText Shear { get; } = Variable("V");
    public static TechnicalText Moment { get; } = Variable("M");
    public static TechnicalText YieldStrength { get; } = new(new("R", Role: TechnicalRole.Variable), Index("e"));
    public static TechnicalText ProofStrength { get; } = new(new("R", Role: TechnicalRole.Variable), Index("p0,2"));
    public static TechnicalText MaximumStress { get; } = new(new("σ", Role: TechnicalRole.Variable), Index("b,max"));
    public static TechnicalText MaximumMoment { get; } = "|" + Moment + new TechnicalText(new TechnicalRun("|"), Index("max"));
    public static TechnicalText MaximumDisplacement { get; } = "|" + Displacement + new TechnicalText(new TechnicalRun("|"), Index("max"));
    public static TechnicalText ReactionX { get; } = new(new("R", Role: TechnicalRole.Variable), Index("x", true));
    public static TechnicalText ReactionY { get; } = new(new("R", Role: TechnicalRole.Variable), Index("y", true));
    public static TechnicalText Diameter { get; } = "Ø";

    public static TechnicalText Axis(string axis) => axis is "x" or "y" or "z" or "u" or "v"
        ? Variable(axis) : throw new ArgumentOutOfRangeException(nameof(axis));
    public static TechnicalText Function(TechnicalText quantity) => quantity + Variable("(x)");
    public static TechnicalText Inertia(string? axis = null) => WithAxis("I", axis);
    public static TechnicalText Modulus(string? axis = null, int sign = 0)
    {
        var symbol = WithAxis("W", axis);
        return sign switch
        {
            0 => symbol,
            1 => symbol + new TechnicalText(new TechnicalRun("+", TechnicalPosition.Superscript)),
            -1 => symbol + new TechnicalText(new TechnicalRun("−", TechnicalPosition.Superscript)),
            _ => throw new ArgumentOutOfRangeException(nameof(sign))
        };
    }
    private static TechnicalText WithAxis(string letter, string? axis) => axis is null or "" ? Variable(letter)
        : Variable(letter) + new TechnicalText(new TechnicalRun(axis is "y" or "z" or "u" or "v" ? axis
            : throw new ArgumentOutOfRangeException(nameof(axis)), TechnicalPosition.Subscript, TechnicalRole.Variable));

    public static TechnicalText SectionInput(string key) => key switch
    {
        "A" => Area,
        "IY" => Inertia("y"), "IZ" => Inertia("z"), "IU" => Inertia("u"), "IV" => Inertia("v"),
        "WY" => Modulus("y"), "WZ" => Modulus("z"), "WU" => Modulus("u"), "WV" => Modulus("v"),
        "tw" => new(new("t", Role: TechnicalRole.Variable), Index("w")),
        "tf" => new(new("t", Role: TechnicalRole.Variable), Index("f")),
        "h" or "b" or "d" or "D" or "t" or "r" => Variable(key),
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };
}
