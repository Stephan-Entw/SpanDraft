using System.Reflection;
using System.Xml.Linq;
using SpanDraft.Analysis;
using SpanDraft.Core.Beams;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Core.Validation;
using SpanDraft.Solver;
using Xunit;
using static SpanDraft.Tests.SolverTestSupport;

namespace SpanDraft.Tests;

public class AnalysisTests
{
    private static BeamModel CenterLoad(double sectionModulus = 0.001) => Beam(loads: [Point(1, -1000)],
        material: new Material("Analysis test steel", Pressure.FromPascals(200e9), Pressure.FromPascals(250e6)),
        section: new CustomSection(Area.FromSquareMeters(0.01), SecondMomentOfArea.FromMetersToTheFourth(1e-6),
            SectionModulus.FromCubicMeters(sectionModulus)));

    [Fact]
    public void CenterLoadReturnsConsistentAnalyticalAndEngineeringResults()
    {
        BeamModel beam = CenterLoad();
        BeamAnalysisOutcome outcome = BeamAnalysis.Analyze(beam);
        Assert.True(outcome.IsSuccess);
        Assert.Null(outcome.Failure);
        BeamAnalysisResult result = Assert.IsType<BeamAnalysisResult>(outcome.Result);
        Assert.Same(beam, result.Beam);
        Assert.Same(result.Beam, result.Solution.Beam);
        ForceClose(500, At(result.Solution, 0).ReactionY);
        ForceClose(500, At(result.Solution, 2).ReactionY);
        NumericAssert.Close(500, result.Solution.Extrema.MaximumBendingMoment.Value.NewtonMeters);
        NumericAssert.Close(-1.0 / 1200, result.Solution.Extrema.MinimumTransverseDisplacement.Value.Meters);
        NumericAssert.Close(500000, result.Engineering.MaximumBendingStress.Pascals);
        NumericAssert.Close(500, result.Engineering.SafetyFactor);
        Assert.Same(result.Solution.Extrema.MaximumBendingMoment, result.Engineering.CriticalBendingMoment);
        Assert.Same(result.Solution.Extrema.MinimumTransverseDisplacement, result.Engineering.CriticalTransverseDisplacement);
    }

    [Fact]
    public void MissingSupportsPreservesStructuredValidationErrorWithoutResult()
    {
        BeamAnalysisFailure failure = Failure(BeamAnalysis.Analyze(Beam(supports: [])), BeamAnalysisFailureCode.InvalidModel);
        ValidationError error = Assert.Single(failure.ValidationErrors);
        Assert.Equal(ValidationErrorCode.MissingSupports, error.Code);
        Assert.Equal("Supports", error.Path);
        Assert.Equal("The beam requires at least one support.", error.Message);
        Assert.Throws<NotSupportedException>(() => ((IList<ValidationError>)failure.ValidationErrors).Clear());
    }

    [Fact]
    public void InvalidModelPreservesAllOriginalDiagnostics()
    {
        BeamModel beam = Beam([SupportAt(3, SupportType.Fixed), SupportAt(3, SupportType.Fixed)],
            [Point(3, -1), Couple(3, 1), Uniform(0, 3, -1)]);
        BeamSolverException original = Assert.Throws<BeamSolverException>(() => Solve(beam));
        BeamAnalysisFailure failure = Failure(BeamAnalysis.Analyze(beam), BeamAnalysisFailureCode.InvalidModel);
        Assert.Equal(original.ValidationErrors, failure.ValidationErrors);
        Assert.Equal(original.Message, failure.TechnicalMessage);
        Assert.Equal(6, failure.ValidationErrors.Count);
    }

    [Fact]
    public void CoreValidUnstableModelReturnsFailureWithoutEngineeringResult()
    {
        BeamModel beam = Beam([SupportAt(0, SupportType.Roller), SupportAt(L, SupportType.Roller)], [Point(1, -1000)]);
        Assert.Empty(BeamModelValidator.Validate(beam));
        Assert.Empty(Failure(BeamAnalysis.Analyze(beam), BeamAnalysisFailureCode.UnstableModel).ValidationErrors);
    }

    [Fact]
    public void IllConditionedModelReturnsFailureWithoutResult()
    {
        Assert.Empty(Failure(BeamAnalysis.Analyze(Cantilever(Point(1, -1000), Point(1 + 1e-6, 0))),
            BeamAnalysisFailureCode.IllConditionedSystem).ValidationErrors);
    }

    [Fact]
    public void NumericalFailureReturnsFailureWithoutResult()
    {
        Assert.Empty(Failure(BeamAnalysis.Analyze(Cantilever(Point(L, double.MaxValue), Point(L, double.MaxValue))),
            BeamAnalysisFailureCode.NumericalFailure).ValidationErrors);
    }

    [Theory]
    [InlineData(SolverErrorCode.InvalidModel, BeamAnalysisFailureCode.InvalidModel)]
    [InlineData(SolverErrorCode.UnstableModel, BeamAnalysisFailureCode.UnstableModel)]
    [InlineData(SolverErrorCode.IllConditionedSystem, BeamAnalysisFailureCode.IllConditionedSystem)]
    [InlineData(SolverErrorCode.NumericalFailure, BeamAnalysisFailureCode.NumericalFailure)]
    public void SolverErrorCodeMappingIsExplicit(SolverErrorCode source, BeamAnalysisFailureCode expected)
    {
        Assert.Equal(expected, BeamAnalysisFailure.MapSolverErrorCode(source));
    }

    [Fact]
    public void MappingCoversExactlyTheCurrentSolverContract()
    {
        SolverErrorCode[] covered = [SolverErrorCode.InvalidModel, SolverErrorCode.UnstableModel,
            SolverErrorCode.IllConditionedSystem, SolverErrorCode.NumericalFailure];
        Assert.Equal(covered, Enum.GetValues<SolverErrorCode>());
        Assert.All(covered, code => Assert.True(Enum.IsDefined(BeamAnalysisFailure.MapSolverErrorCode(code))));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public void UnknownSolverErrorCodeRemainsVisible(int value)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            BeamAnalysisFailure.MapSolverErrorCode((SolverErrorCode)value));
        Assert.Equal("code", exception.ParamName);
    }

    [Fact]
    public void NullBeamThrowsWithCorrectParameterName()
    {
        Assert.Equal("beam", Assert.Throws<ArgumentNullException>(() => BeamAnalysis.Analyze(null!)).ParamName);
    }

    [Fact]
    public void OutcomeFactoriesPreserveExactlyOneBranch()
    {
        BeamAnalysisResult result = Assert.IsType<BeamAnalysisResult>(BeamAnalysis.Analyze(CenterLoad()).Result);
        BeamAnalysisOutcome success = BeamAnalysisOutcome.Success(result);
        Assert.True(success.IsSuccess);
        Assert.Same(result, success.Result);
        Assert.Null(success.Failure);

        BeamAnalysisFailure failure = new(new BeamSolverException(SolverErrorCode.UnstableModel, "Diagnostic"));
        BeamAnalysisOutcome failed = BeamAnalysisOutcome.FromFailure(failure);
        Assert.False(failed.IsSuccess);
        Assert.Null(failed.Result);
        Assert.Same(failure, failed.Failure);
        Assert.Equal("result", Assert.Throws<ArgumentNullException>(() => BeamAnalysisOutcome.Success(null!)).ParamName);
        Assert.Equal("failure", Assert.Throws<ArgumentNullException>(() => BeamAnalysisOutcome.FromFailure(null!)).ParamName);
    }

    [Fact]
    public void PublicOutcomeApiCannotConstructOrMutateInvalidStates()
    {
        Type type = typeof(BeamAnalysisOutcome);
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetConstructors());
        Assert.Empty(type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
        Assert.All(type.GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.Empty(type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
    }

    [Fact]
    public void FailureRetainsTheOriginalReadOnlyValidationList()
    {
        var error = new ValidationError(ValidationErrorCode.MissingSupports, "Original message", "Supports");
        var exception = new BeamSolverException(SolverErrorCode.InvalidModel, "Technical diagnostic", [error]);
        var failure = new BeamAnalysisFailure(exception);
        Assert.Same(exception.ValidationErrors, failure.ValidationErrors);
        Assert.Same(error, Assert.Single(failure.ValidationErrors));
        Assert.Equal(exception.Message, failure.TechnicalMessage);
    }

    [Fact]
    public void EngineeringExceptionPropagatesInsteadOfBecomingNumericalFailure()
    {
        // A finite, positive W is valid; stress overflows only during engineering assessment.
        BeamModel beam = CenterLoad(1e-310);
        Assert.Empty(BeamModelValidator.Validate(beam));
        Assert.IsType<BeamSolution>(Solve(beam));
        Assert.Throws<ArgumentOutOfRangeException>(() => BeamAnalysis.Analyze(beam));
    }

    [Fact]
    public void DesktopDirectlyReferencesOnlyCoreAndAnalysis()
    {
        Assert.Equal(new[] { "SpanDraft.Analysis", "SpanDraft.Core" }, ProjectReferences("SpanDraft.Desktop"));
    }

    [Fact]
    public void AnalysisHasOnlyCalculationProjectDependenciesAndNoPackages()
    {
        Assert.Equal(new[] { "SpanDraft.Core", "SpanDraft.Engineering", "SpanDraft.Solver" }, ProjectReferences("SpanDraft.Analysis"));
        Assert.Empty(Project("SpanDraft.Analysis").Descendants("PackageReference"));
        Assert.All(typeof(BeamAnalysis).Assembly.GetReferencedAssemblies(), reference =>
            Assert.True(reference.Name is "SpanDraft.Core" or "SpanDraft.Solver" or "SpanDraft.Engineering" ||
                reference.Name?.StartsWith("System", StringComparison.Ordinal) == true,
                $"Unexpected Analysis dependency: {reference.Name}"));
    }

    private static BeamAnalysisFailure Failure(BeamAnalysisOutcome outcome, BeamAnalysisFailureCode code)
    {
        Assert.False(outcome.IsSuccess);
        Assert.Null(outcome.Result);
        BeamAnalysisFailure failure = Assert.IsType<BeamAnalysisFailure>(outcome.Failure);
        Assert.Equal(code, failure.Code);
        Assert.False(string.IsNullOrWhiteSpace(failure.TechnicalMessage));
        return failure;
    }

    private static XDocument Project(string name)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpanDraft.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return XDocument.Load(Path.Combine(directory.FullName, "src", name, name + ".csproj"));
    }

    private static string[] ProjectReferences(string name) => Project(name).Descendants("ProjectReference")
        .Select(reference => Path.GetFileNameWithoutExtension((string)reference.Attribute("Include")!))
        .Order(StringComparer.Ordinal).ToArray();
}
