using SpanDraft.Core.Sections;
using SpanDraft.Core.Units;
using Xunit;

namespace SpanDraft.Tests;

public class ManualSectionDefinitionTests
{
    private static readonly Area A = Area.FromSquareMeters(2);
    private static readonly SecondMomentOfArea I = SecondMomentOfArea.FromMetersToTheFourth(3);
    private static readonly SectionModulus W = SectionModulus.FromCubicMeters(4);
    private static ManualSectionAxis Axis(SectionAxisDesignation designation) => new(designation, I, W);

    [Theory]
    [InlineData(SectionAxisDesignation.Y)]
    [InlineData(SectionAxisDesignation.Z)]
    [InlineData(SectionAxisDesignation.U)]
    [InlineData(SectionAxisDesignation.V)]
    public void OneExplicitAxisHasEqualSignedModuliAndNoInventedSecondAxis(SectionAxisDesignation designation)
    {
        var input = Axis(designation);
        ISectionDefinition section = new ManualSectionDefinition(A, input);
        Assert.Equal(designation, input.AxisDesignation);
        Assert.Equal(I, input.SecondMomentOfArea);
        Assert.Equal(W, input.SectionModulus);
        Assert.Equal(A, section.Area);
        var axis = Assert.Single(section.Axes);
        Assert.Same(axis, section.GetAxis(designation));
        Assert.Equal(designation, axis.AxisDesignation);
        Assert.Equal(I, axis.SecondMomentOfArea);
        Assert.Equal(W, axis.PositiveSectionModulus);
        Assert.Equal(W, axis.NegativeSectionModulus);
        foreach (var absent in Enum.GetValues<SectionAxisDesignation>().Where(value => value != designation))
            Assert.Throws<KeyNotFoundException>(() => section.GetAxis(absent));
        Assert.False(section is SpanDraft.Core.Sections.Parametric.IParametricSectionDefinition);
        var list = Assert.IsAssignableFrom<IList<SectionAxisProperties>>(section.Axes);
        Assert.Throws<NotSupportedException>(() => list.Add(axis));
        Assert.Throws<NotSupportedException>(() => list[0] = axis);
    }

    public static IEnumerable<object[]> Pairs()
    {
        foreach (var first in Enum.GetValues<SectionAxisDesignation>())
        foreach (var second in Enum.GetValues<SectionAxisDesignation>())
            yield return [first, second];
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void OnlyCoordinateOrPrincipalPairsAreAcceptedInEitherOrder(SectionAxisDesignation first, SectionAxisDesignation second)
    {
        var valid = first != second && ((first is SectionAxisDesignation.Y or SectionAxisDesignation.Z) == (second is SectionAxisDesignation.Y or SectionAxisDesignation.Z));
        var secondInput = new ManualSectionAxis(second, SecondMomentOfArea.FromMetersToTheFourth(7), SectionModulus.FromCubicMeters(8));
        if (!valid)
        {
            Assert.Throws<ArgumentException>(() => new ManualSectionDefinition(A, Axis(first), secondInput));
            return;
        }
        var section = new ManualSectionDefinition(A, Axis(first), secondInput);
        var reversed = new ManualSectionDefinition(A, secondInput, Axis(first));
        Assert.Equal(section.Axes, reversed.Axes);
        Assert.Equal(new[] { first, second }.Order(), section.Axes.Select(axis => axis.AxisDesignation));
        Assert.Equal(I, section.GetAxis(first).SecondMomentOfArea);
        Assert.Equal(secondInput.SecondMomentOfArea, section.GetAxis(second).SecondMomentOfArea);
        Assert.Equal(secondInput.SectionModulus, section.GetAxis(second).PositiveSectionModulus);
        Assert.Equal(secondInput.SectionModulus, section.GetAxis(second).NegativeSectionModulus);
    }

    public static IEnumerable<object[]> InvalidNumbers() =>
        new[] { 0, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity }.Select(value => new object[] { value });

    [Theory]
    [MemberData(nameof(InvalidNumbers))]
    public void AreaInertiaAndBothModuliMustBeFiniteAndStrictlyPositive(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManualSectionDefinition(Area.FromSquareMeters(value), Axis(SectionAxisDesignation.Y)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManualSectionAxis(SectionAxisDesignation.Y, SecondMomentOfArea.FromMetersToTheFourth(value), W));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManualSectionAxis(SectionAxisDesignation.Y, I, SectionModulus.FromCubicMeters(value)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SectionAxisProperties(SectionAxisDesignation.Y, SecondMomentOfArea.FromMetersToTheFourth(value), W, W));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SectionAxisProperties(SectionAxisDesignation.Y, I, SectionModulus.FromCubicMeters(value), W));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SectionAxisProperties(SectionAxisDesignation.Y, I, W, SectionModulus.FromCubicMeters(value)));
    }

    [Fact]
    public void DefaultUnitValuesAreRejectedByDomainObjects()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManualSectionDefinition(default, Axis(SectionAxisDesignation.Y)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManualSectionAxis(SectionAxisDesignation.Y, default, W));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManualSectionAxis(SectionAxisDesignation.Y, I, default));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void UndefinedAxisDesignationsAreRejected(int value)
    {
        var designation = (SectionAxisDesignation)value;
        Assert.Throws<ArgumentOutOfRangeException>(() => Axis(designation));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SectionAxisProperties(designation, I, W, W));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManualSectionDefinition(A, Axis(SectionAxisDesignation.Y)).GetAxis(designation));
    }

    [Fact]
    public void FirstAxisIsRequired() =>
        Assert.Throws<ArgumentNullException>(() => new ManualSectionDefinition(A, null!));

    [Fact]
    public void AxisPropertiesAreValueObjectsAndKeepModuliSeparate()
    {
        var negative = SectionModulus.FromCubicMeters(5);
        var first = new SectionAxisProperties(SectionAxisDesignation.Y, I, W, negative);
        Assert.Equal(first, new SectionAxisProperties(SectionAxisDesignation.Y, I, W, negative));
        Assert.NotEqual(first, new SectionAxisProperties(SectionAxisDesignation.Y, I, negative, W));
        Assert.NotEqual(first, new SectionAxisProperties(SectionAxisDesignation.Z, I, W, negative));
        Assert.Equal(W, first.PositiveSectionModulus);
        Assert.Equal(negative, first.NegativeSectionModulus);
    }
}
