using SpanDraft.Validation;
using Xunit;

namespace SpanDraft.Validation.Tests;

public class AcceptanceTests
{
    public static IEnumerable<object[]> Cases => Catalog.CaseIds.Select(id => new object[] { id });
    public static IEnumerable<object[]> References => Catalog.CaseIds.SelectMany(id =>
        Catalog.ExternalSolvers.Concat(Catalog.AnalyticalIds.Contains(id) ? new[] { "analytical" } : []).Select(s => new object[] {id,s}));
    public static IEnumerable<object[]> Transformations => Catalog.CaseIds.SelectMany(id =>
        new[] { "load2", "load-1", "E2", "I2", "zeroNode" }.Select(op => new object[] {id,op}));
    public static IEnumerable<object[]> Superpositions => Physics.SuperpositionIds.Select(id => new object[] {id});
    private static void Pass(CheckReport report) => Assert.True(report.Status == "PASS", System.Text.Json.JsonSerializer.Serialize(report.Failures,Catalog.Json));

    [Theory, MemberData(nameof(References))]
    public void GoldenReferenceMatchesPublicSolver(string id, string solver)
    {
        var input = Catalog.Load(id);
        var reference = Catalog.ReadReference(id,solver);
        Pass(Comparison.Compare(input,Results.Export(input),reference,solver));
        Pass(Physics.ReferenceEquilibrium(input,reference));
    }

    [Theory, MemberData(nameof(Cases))]
    public void GlobalEquilibrium(string id)
    {
        var input = Catalog.Load(id);
        Pass(Physics.Equilibrium(input,ValidationCase.Solve(input)));
    }

    [Theory, MemberData(nameof(Cases))]
    public void AxialConsistencyJumpsAndIndependentDifferentialChecks(string id)
    {
        var input = Catalog.Load(id);
        Pass(Physics.Fields(input,ValidationCase.Solve(input)));
    }

    [Theory, MemberData(nameof(Transformations))]
    public void MetamorphicRelation(string id, string operation) => Pass(Physics.Metamorphic(Catalog.Load(id),operation));

    [Theory, MemberData(nameof(Superpositions))]
    public void IndependentLoadGroupsSuperpose(string id) => Pass(Physics.Superposition(Catalog.Load(id)));

    [Fact]
    public void CatalogAndReferenceInventoryIsComplete()
    {
        Assert.Equal(Catalog.CaseIds.Order(), Directory.GetFiles(Path.Combine(Catalog.Root,"validation","cases"),"*.json")
            .Select(Path.GetFileNameWithoutExtension).Order());
        Assert.Equal(45, Directory.GetFiles(Path.Combine(Catalog.Root,"validation","references"),"*.json").Length);
    }

    [Fact]
    public void StaleInputHashCannotPass()
    {
        var input = Catalog.Load("V01");
        var reference = Catalog.ReadReference("V01","indeterminatebeam") with { InputSha256 = new string('0',64) };
        Assert.Equal("FAIL",Comparison.Compare(input,Results.Export(input),reference,"indeterminatebeam").Status);
    }

    [Fact]
    public void MissingOneSidedValueCannotPass()
    {
        var input = Catalog.Load("V05");
        var reference = Catalog.ReadReference("V05","indeterminatebeam");
        reference.Samples = reference.Samples.Where(s => !(s.Position==1.5 && s.Side=="Right")).ToArray();
        Assert.Equal("FAIL",Comparison.Compare(input,Results.Export(input),reference,"indeterminatebeam").Status);
    }

    [Fact]
    public void UnsupportedQuantitiesCannotHideRequiredCoverage()
    {
        var input = Catalog.Load("V04");
        var reference = Catalog.ReadReference("V04","pycba");
        reference.Availability["w"]="notSupported";
        Assert.Equal("FAIL",Comparison.Compare(input,Results.Export(input),reference,"pycba").Status);
    }

    [Fact]
    public void OutOfToleranceZeroComparisonIsReported()
    {
        var report = new CheckReport("guard","reference");
        report.Number("w",2e-10,0,Comparison.Displacement,1,"Left");
        Assert.Equal("FAIL",report.Status);
        var failure = Assert.Single(report.Failures);
        Assert.Null(failure.RelativeDeviation);
        Assert.Equal(2e-10,failure.AbsoluteDeviation);
        Assert.Equal("Left",failure.Side);
    }

    [Fact]
    public void ReferenceVersionDriftCannotPass()
    {
        var input = Catalog.Load("V01");
        var reference = Catalog.ReadReference("V01","pycba");
        reference.Provenance["version"]="changed";
        Assert.Equal("FAIL",Comparison.Compare(input,Results.Export(input),reference,"pycba").Status);
    }

    [Fact]
    public void OppositeSideOfPointForceJumpCannotClaimShearPlateau()
    {
        var input = Catalog.Load("V01");
        var actual = Results.Export(input);
        actual.Extrema["VMin"] = actual.Extrema["VMin"] with { Locations = [new(2,"Left")] };
        var reference = Catalog.ReadReference("V01","pycba");
        var report = Comparison.Compare(input,actual,reference,"pycba");
        Assert.Contains(report.Failures,f => f.Quantity == "VMin.position");
    }

    [Fact]
    public void ClaimedPassWithoutGridConvergenceCannotPass()
    {
        var input = Catalog.Load("V04");
        var reference = Catalog.ReadReference("V04","pycba");
        reference.Convergence!["converged"] = false;
        Assert.Equal("FAIL",Comparison.Compare(input,Results.Export(input),reference,"pycba").Status);
    }

    [Fact]
    public void DocumentedV14PositionLimitationsAreNotApplicableAndAllValuesPass()
    {
        var input = Catalog.Load("V14");
        var reference = Catalog.ReadReference("V14","indeterminatebeam");
        var report = Comparison.Compare(input,Results.Export(input),reference,"indeterminatebeam");
        Pass(report);
        Assert.Empty(report.Failures);
        Assert.Equal(70,report.Comparisons); // 60 fields + 4 reactions + 6 extremum values.
        Assert.Equal(new[] { "MMax.position", "MMin.position", "VMax.position", "VMin.position", "wMax.position", "wMin.position" },
            report.NotApplicable.Select(c => c.Quantity).Order(StringComparer.Ordinal));
        Assert.All(report.NotApplicable,c => {
            Assert.Equal("NOT APPLICABLE",c.Status);
            Assert.Contains("value remains a mandatory comparison",c.Reason);
        });
    }

    [Theory]
    [InlineData("wMin"), InlineData("wMax"), InlineData("VMin"), InlineData("VMax"), InlineData("MMin"), InlineData("MMax")]
    public void V14NotApplicablePositionCannotHideFailedExtremumValue(string key)
    {
        var input = Catalog.Load("V14");
        var actual = Results.Export(input);
        var reference = Catalog.ReadReference("V14","indeterminatebeam");
        actual.Extrema[key] = actual.Extrema[key] with {
            Value = reference.Extrema[key].Value + 2*Comparison.For(key[..1]).Limit(reference.Extrema[key].Value)
        };
        var report = Comparison.Compare(input,actual,reference,"indeterminatebeam");
        Assert.Equal("FAIL",report.Status);
        Assert.Contains(report.Failures,f => f.Quantity == key);
        Assert.Equal(6,report.NotApplicable.Count);
    }

    [Fact]
    public void UndocumentedV14PositionLimitationCannotPass()
    {
        var input = Catalog.Load("V14");
        var reference = Catalog.ReadReference("V14","indeterminatebeam");
        reference.Extrema["wMin"] = reference.Extrema["wMin"] with { LocationNote = null };
        Assert.Equal("FAIL",Comparison.Compare(input,Results.Export(input),reference,"indeterminatebeam").Status);
    }

    [Fact]
    public void UninvestigatedV14PositionLimitationRemainsPartial()
    {
        var input = Catalog.Load("V14");
        var reference = Catalog.ReadReference("V14","indeterminatebeam");
        reference.Extrema["wMin"] = reference.Extrema["wMin"] with { LocationNote = "Another unresolved extraction issue" };
        Assert.Equal("PARTIAL",Comparison.Compare(input,Results.Export(input),reference,"indeterminatebeam").Status);
    }

    [Fact]
    public void PartialExtractionOfOtherCasesRemainsPartial()
    {
        var input = Catalog.Load("V01");
        var reference = Catalog.ReadReference("V01","indeterminatebeam") with { Status = "PARTIAL" };
        var report = Comparison.Compare(input,Results.Export(input),reference,"indeterminatebeam");
        Assert.Equal("PARTIAL",report.Status);
        Assert.Empty(report.NotApplicable);
    }
}
