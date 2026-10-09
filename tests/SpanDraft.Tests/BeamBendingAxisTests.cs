using SpanDraft.Core.Beams;
using SpanDraft.Core.Sections;
using SpanDraft.Desktop.State;
using SpanDraft.Engineering;
using Xunit;
using static SpanDraft.Tests.SectionAxisTestSupport;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

public sealed class BeamBendingAxisTests
{
    [Theory]
    [InlineData("rectangle", SectionAxisDesignation.Y)]
    [InlineData("rectangle", SectionAxisDesignation.Z)]
    [InlineData("I", SectionAxisDesignation.Y)]
    [InlineData("I", SectionAxisDesignation.Z)]
    [InlineData("angle", SectionAxisDesignation.U)]
    [InlineData("angle", SectionAxisDesignation.V)]
    public void ExplicitAxisRetainsDefinitionAndResolvesExactlyItsProperties(string shape, SectionAxisDesignation axis)
    {
        var section = Profile(shape);
        var beam = AxisBeam(section, axis);
        Assert.Same(section, beam.Section);
        Assert.Equal(axis, beam.BendingAxis);
        Assert.Same(section.GetAxis(axis), beam.BendingAxisProperties);
    }

    [Theory]
    [InlineData(SectionAxisDesignation.Y, null)]
    [InlineData(SectionAxisDesignation.Z, null)]
    [InlineData(SectionAxisDesignation.U, null)]
    [InlineData(SectionAxisDesignation.V, null)]
    [InlineData(SectionAxisDesignation.Y, SectionAxisDesignation.Z)]
    [InlineData(SectionAxisDesignation.U, SectionAxisDesignation.V)]
    public void EveryProvidedManualAxisIsIndividuallyUsable(SectionAxisDesignation first, SectionAxisDesignation? second)
    {
        var section = Manual(first, second);
        foreach (var axis in section.Axes)
        {
            var beam = AxisBeam(section, axis.AxisDesignation, [Point(L, -1000)]);
            Assert.Same(axis, beam.BendingAxisProperties);
            Assert.Same(beam, Solve(beam).Beam);
        }
    }

    [Theory]
    [InlineData("I", SectionAxisDesignation.U)]
    [InlineData("I", SectionAxisDesignation.V)]
    [InlineData("angle", SectionAxisDesignation.Y)]
    [InlineData("angle", SectionAxisDesignation.Z)]
    public void MissingProfileAxisIsRejectedAsBendingAxisArgument(string shape, SectionAxisDesignation axis)
    {
        var exception = Assert.Throws<ArgumentException>(() => AxisBeam(Profile(shape), axis));
        Assert.Equal("bendingAxis", exception.ParamName);
        Assert.IsType<KeyNotFoundException>(exception.InnerException);
    }

    [Fact]
    public void ManualSingleAxisDoesNotSupplyAnyReplacement()
    {
        var section = Manual(SectionAxisDesignation.Y);
        foreach (var axis in new[] { SectionAxisDesignation.Z, SectionAxisDesignation.U, SectionAxisDesignation.V })
            Assert.Equal("bendingAxis", Assert.Throws<ArgumentException>(() => AxisBeam(section, axis)).ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(999)]
    public void UndefinedEnumIsRejectedBeforeSectionResolution(int value)
    {
        var section = new TabulatedSection();
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => AxisBeam(section, (SectionAxisDesignation)value));
        Assert.Equal("bendingAxis", exception.ParamName);
        Assert.Equal(0, section.ResolutionCount);
    }

    [Fact]
    public void AxisIsResolvedOnceAndConsumersUseTheValidatedProperties()
    {
        var section = new TabulatedSection();
        var beam = AxisBeam(section, SectionAxisDesignation.Y, [Point(L, -1000)]);
        var solution = Solve(beam);
        BeamEngineeringAnalysis.Analyze(solution);
        Assert.True(BeamModelMechanicalComparer.AreEquivalent(beam, beam));
        Assert.Equal(1, section.ResolutionCount);
        Assert.Same(beam, solution.Beam);
    }

    [Fact]
    public void DefinitionReturningAnotherAxisCannotCreateAnInconsistentBeam()
    {
        var exception = Assert.Throws<ArgumentException>(() => AxisBeam(new IncorrectAxisDefinition(), SectionAxisDesignation.Z));
        Assert.Equal("bendingAxis", exception.ParamName);
    }

    private sealed class IncorrectAxisDefinition : ISectionDefinition
    {
        private readonly TabulatedSection data = new();
        public SpanDraft.Core.Units.Area Area => data.Area;
        public IReadOnlyList<SectionAxisProperties> Axes => data.Axes;
        public SectionAxisProperties GetAxis(SectionAxisDesignation axisDesignation) => data.Axes[0];
    }

    [Fact]
    public void GeneralDefinitionConstructorAlwaysRequiresAnAxis()
    {
        var general = Assert.Single(typeof(BeamModel).GetConstructors(), constructor =>
            constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(ISectionDefinition)));
        var axis = Assert.Single(general.GetParameters(), parameter => parameter.ParameterType == typeof(SectionAxisDesignation));
        Assert.False(axis.IsOptional);
        Assert.Null(typeof(BeamModel).GetProperty(nameof(BeamModel.BendingAxis))!.SetMethod);
        Assert.Null(typeof(BeamModel).GetProperty(nameof(BeamModel.BendingAxisProperties))!.SetMethod);
    }

    [Fact]
    public void NullArgumentsRetainTheirArgumentNames()
    {
        var valid = AxisBeam(Manual(SectionAxisDesignation.Y), SectionAxisDesignation.Y);
        Assert.Equal("section", Assert.Throws<ArgumentNullException>(() => new BeamModel(valid.Length,
            valid.Material, null!, SectionAxisDesignation.Y, [], [])).ParamName);
        Assert.Equal("supports", Assert.Throws<ArgumentNullException>(() => new BeamModel(valid.Length,
            valid.Material, valid.Section, valid.BendingAxis, null!, [])).ParamName);
        Assert.Equal("loads", Assert.Throws<ArgumentNullException>(() => new BeamModel(valid.Length,
            valid.Material, valid.Section, valid.BendingAxis, [], null!)).ParamName);
    }

    [Fact]
    public void EqualPropertiesAcrossLegacyManualAndParametricDefinitionsAreEquivalent()
    {
        var section = Profile("rectangle");
        var y = section.GetAxis(SectionAxisDesignation.Y);
        var manual = new ManualSectionDefinition(section.Area,
            new ManualSectionAxis(SectionAxisDesignation.Y, y.SecondMomentOfArea, y.PositiveSectionModulus));
        var legacy = new CustomSection(section.Area, y.SecondMomentOfArea, y.PositiveSectionModulus);
        Assert.Equal(y.PositiveSectionModulus, y.NegativeSectionModulus);
        Assert.True(BeamModelMechanicalComparer.AreEquivalent(AxisBeam(section, SectionAxisDesignation.Y),
            AxisBeam(manual, SectionAxisDesignation.Y)));
        Assert.True(BeamModelMechanicalComparer.AreEquivalent(AxisBeam(legacy, SectionAxisDesignation.Y),
            AxisBeam(manual, SectionAxisDesignation.Y)));
    }

    [Theory]
    [InlineData("A")]
    [InlineData("I")]
    [InlineData("W+")]
    [InlineData("W-")]
    public void EachSelectedPropertyIndependentlyChangesMechanicalEquivalence(string change)
    {
        var left = AxisBeam(new TabulatedSection(), SectionAxisDesignation.Y);
        var right = AxisBeam(new TabulatedSection(a: change == "A" ? .004 : .003,
            i: change == "I" ? 9e-6 : 8e-6, positiveW: change == "W+" ? .0003 : .0002,
            negativeW: change == "W-" ? .00015 : .0001), SectionAxisDesignation.Y);
        Assert.False(BeamModelMechanicalComparer.AreEquivalent(left, right));
        Assert.False(BeamModelMechanicalComparer.AreEquivalent(right, left));
    }

    [Fact]
    public void DifferentAxesRemainDifferentForAnIsotropicCircle()
    {
        var circle = Profile("circle");
        var y = AxisBeam(circle, SectionAxisDesignation.Y);
        var z = AxisBeam(circle, SectionAxisDesignation.Z);
        Assert.Equal(y.BendingAxisProperties.SecondMomentOfArea, z.BendingAxisProperties.SecondMomentOfArea);
        Assert.False(BeamModelMechanicalComparer.AreEquivalent(y, z));
    }

    [Fact]
    public void UnselectedAxisPropertiesDoNotChangeMechanicalEquivalence() =>
        Assert.True(BeamModelMechanicalComparer.AreEquivalent(
            AxisBeam(new TabulatedSection(), SectionAxisDesignation.Y),
            AxisBeam(new TabulatedSection(otherI: 3e-6), SectionAxisDesignation.Y)));
}
