using SpanDraft.Validation;
using Xunit;

namespace SpanDraft.Validation.Tests;

public class AcceptanceTests
{
    public static IEnumerable<object[]> Cases => Catalog.CaseIds.Select(id => new object[] { id });
    public static IEnumerable<object[]> References => Catalog.CaseIds.SelectMany(id =>
        Catalog.ExternalSolversFor(id).Concat(Catalog.AnalyticalIds.Contains(id) ? new[] { "analytical" } : [])
            .Select(s => new object[] {id,s}));
    public static IEnumerable<object[]> Transformations => Catalog.CaseIds.SelectMany(id =>
        Physics.MetamorphicOperations.Select(op => new object[] {id,op}));
    public static IEnumerable<object[]> Superpositions => Physics.SuperpositionIds.Select(id => new object[] {id});
    private static void Pass(CheckReport report) => Assert.True(report.Status == "PASS", System.Text.Json.JsonSerializer.Serialize(report.Failures,Catalog.Json));

    [Theory, MemberData(nameof(References))]
    public void GoldenReferenceMatchesSolver(string id, string solver)
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
        var solution = ValidationCase.Solve(input);
        Pass(Physics.Fields(input,solution));
        foreach (var evaluation in input.Evaluations)
        {
            var section = solution.EvaluateAt(ValidationCase.M(evaluation.Position),
                Enum.Parse<SpanDraft.Solver.EvaluationSide>(evaluation.Side));
            Assert.Equal(0UL, BitConverter.DoubleToUInt64Bits(section.AxialDisplacement.Meters));
            Assert.Equal(0UL, BitConverter.DoubleToUInt64Bits(section.AxialForce.Newtons));
        }
        foreach (var node in solution.Nodes)
        {
            bool held = input.Supports.Any(s => s.Position == node.Position.Meters && s.Type is "Fixed" or "Pinned" or "Spring");
            Assert.Equal(held, node.ReactionX.HasValue);
            if (held) Assert.Equal(0UL, BitConverter.DoubleToUInt64Bits(node.ReactionX!.Value.Newtons));
        }
    }

    [Theory, MemberData(nameof(Transformations))]
    public void MetamorphicRelation(string id, string operation) => Pass(Physics.Metamorphic(Catalog.Load(id),operation));

    public static IEnumerable<object[]> RigidLimits => Physics.RigidLimitIds.Select(id => new object[] {id});
    [Theory, MemberData(nameof(RigidLimits))]
    public void VeryStiffSpringReproducesItsRigidLimit(string id) => Pass(Physics.Metamorphic(Catalog.Load(id),"rigidLimit"));

    [Theory, MemberData(nameof(Superpositions))]
    public void IndependentLoadGroupsSuperpose(string id) => Pass(Physics.Superposition(Catalog.Load(id)));

    [Theory, MemberData(nameof(Superpositions))]
    public void SuperpositionGroupsDoNotShareLoads(string id)
    {
        var input = Catalog.Load(id);
        var (a, b) = Physics.SplitLoads(input);
        if (id is "V04" or "V09" or "V13" || Catalog.LinearLoadIds.Contains(id)) return; // groups defined explicitly per case
        // Every other case separates the point loads and moments from the distributed loads, each load exactly once.
        Assert.Equal(input.PointForces.Length + input.PointMoments.Length, a.PointForces.Length + a.PointMoments.Length + b.PointForces.Length + b.PointMoments.Length);
        Assert.Equal(input.Udls.Length + input.LineLoads.Length, a.Udls.Length + a.LineLoads.Length + b.Udls.Length + b.LineLoads.Length);
        Assert.Empty(a.Udls); Assert.Empty(a.LineLoads);
        Assert.Empty(b.PointForces); Assert.Empty(b.PointMoments);
    }

    [Fact]
    public void CatalogAndReferenceInventoryIsComplete()
    {
        Assert.Equal(Catalog.CaseIds.Order(), Directory.GetFiles(Path.Combine(Catalog.Root,"validation","cases"),"*.json")
            .Select(Path.GetFileNameWithoutExtension).Order());
        // Two external solvers and the analytical reference where they exist; V22 and V23 have no IndeterminateBeam
        // reference (no rotational spring) and V24 only the analytical one (digit loss of both solvers at k L³/EI = 10¹²).
        int expected = Catalog.CaseIds.Sum(id => Catalog.ExternalSolversFor(id).Length + (Catalog.AnalyticalIds.Contains(id) ? 1 : 0));
        Assert.Equal(94, expected);
        Assert.Equal(expected, Directory.GetFiles(Path.Combine(Catalog.Root,"validation","references"),"*.json").Length);
        Assert.Equal(new[] { "V22", "V23" }, Catalog.SpringIds.Where(id => Catalog.ExternalSolversFor(id) is ["pycba"]));
        // IndeterminateBeam has one E and one I per beam: the segment cases have PyCBA and the analytical reference.
        Assert.All(Catalog.SegmentIds, id => Assert.Equal(["pycba"], Catalog.ExternalSolversFor(id)));
        Assert.All(Catalog.SegmentIds, id => Assert.True(Catalog.Load(id).Segments.Length >= 2));
        // IndeterminateBeam has no internal moment release either: PyCBA (member types) and the analytical reference.
        Assert.All(Catalog.MomentReleaseIds, id => Assert.Equal(["pycba"], Catalog.ExternalSolversFor(id)));
        Assert.All(Catalog.MomentReleaseIds, id => Assert.NotEmpty(Catalog.Load(id).MomentReleases));
        // The releases of a case must be real hinges: the only one with two is the drop-in span.
        Assert.Equal(["V33"], Catalog.MomentReleaseIds.Where(id => Catalog.Load(id).MomentReleases.Length == 2));
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

    [Theory]
    [InlineData("EulerBernoulliBeamSolver", "current", true)]
    [InlineData("EulerBernoulliBeamSolver", null, false)]
    [InlineData("EulerBernoulliBeamSolver", "wrong", false)]
    [InlineData(null, "current", false)]
    [InlineData("KernelBeamSolver", "current", false)]
    [InlineData("SomeOtherSolver", "current", false)]
    public void ImportedExportMustIdentifyTheCurrentSolver(string? entryPoint, string? sourceHash, bool accepted)
    {
        string solverHash = Acceptance.SolverHash();
        // Round-trip through JSON exactly as the runner reads --actual.
        string json = System.Text.Json.JsonSerializer.Serialize(Catalog.CaseIds.Select(id =>
        {
            var export = Results.Export(Catalog.Load(id), solverHash: solverHash);
            if (entryPoint is null) export.Provenance.Remove("entryPoint");
            else export.Provenance["entryPoint"] = entryPoint;
            if (sourceHash is null) export.Provenance.Remove("solverSourceSha256");
            else if (sourceHash != "current") export.Provenance["solverSourceSha256"] = new string('0', 64);
            return export;
        }).ToArray(), Catalog.Json);
        var imported = System.Text.Json.JsonSerializer.Deserialize<ReferenceData[]>(json, Catalog.Json)!;
        Assert.Equal(accepted, Acceptance.IsCurrentExport(imported));
        if (!accepted)
            Assert.Equal("Stale/incomplete SpanDraft export",
                Assert.Throws<InvalidDataException>(() => Acceptance.Run(exported: imported)).Message);
    }

    [Theory]
    [InlineData("entryPoint"), InlineData("solverSourceSha256")]
    [InlineData("missingCase"), InlineData("duplicateCase"), InlineData("inputHash"), InlineData("solver")]
    public void OneStaleCaseRejectsTheWholeImportedExport(string defect)
    {
        string solverHash = Acceptance.SolverHash();
        var exported = Catalog.CaseIds.Select(id => Results.Export(Catalog.Load(id), solverHash: solverHash)).ToArray();
        Assert.True(Acceptance.IsCurrentExport(exported));
        switch (defect)
        {
            case "missingCase": exported = exported[..^1]; break;
            case "duplicateCase": exported[^1] = exported[0]; break;
            case "inputHash": exported[^1] = exported[^1] with { InputSha256 = new string('0', 64) }; break;
            case "solver": exported[^1] = exported[^1] with { Solver = "OtherSolver" }; break;
            default: exported[^1].Provenance.Remove(defect); break;
        }
        Assert.False(Acceptance.IsCurrentExport(exported));
        Assert.Throws<InvalidDataException>(() => Acceptance.Run(exported: exported));
    }

    [Fact]
    public void ImportedExportIsCheckedForEquilibriumOnItsOwnReactions()
    {
        var input = Catalog.Load("V01");
        var solution = ValidationCase.Solve(input);
        var exported = Results.Export(input, solution);
        Pass(Acceptance.Equilibrium(input, solution, exported));
        exported.Reactions[0].Values["Ry"] += 1;
        var report = Acceptance.Equilibrium(input, solution, exported);
        Assert.Equal("FAIL", report.Status);
        Assert.Contains(report.Failures, f => f.Quantity == "sumFy");
        Pass(Acceptance.Equilibrium(input, solution, null));
    }

    [Theory]
    [InlineData("/work/validation/references/V01.pycba.json", "/work/validation/references", true)]
    [InlineData("/work/validation/references/sub/x.json", "/work/validation/references/", true)]
    [InlineData("/work/validation/References/V01.pycba.json", "/work/validation/references", true)]
    [InlineData("/work/validation/references/../results/run.json", "/work/validation/references", false)]
    [InlineData("/work/validation/references-x/run.json", "/work/validation/references", false)]
    [InlineData("/work/validation/references", "/work/validation/references", false)]
    public void GoldenReferenceDirectoryIsProtectedFromGeneratedFiles(string path, string directory, bool inside) =>
        Assert.Equal(inside, PathGuard.IsInside(path, directory));

    [Fact]
    public void ReferenceEquilibriumToleratesMissingAvailabilityEntries()
    {
        var input = Catalog.Load("V01");
        var reference = Catalog.ReadReference("V01","pycba");
        reference.Availability.Remove("Rx");
        Pass(Physics.ReferenceEquilibrium(input,reference));
    }

    [Fact]
    public void ChangedReactionRestraintFailsInsteadOfThrowing()
    {
        var input = Catalog.Load("V01");
        var clamped = input with { Supports = [new(0,"Fixed"), input.Supports[1]] };
        var report = Physics.CompareSolutions(input, ValidationCase.Solve(clamped), [(ValidationCase.Solve(input),1.0)], _ => 1, "restraint");
        Assert.Equal("FAIL", report.Status);
        Assert.Contains(report.Failures, f => f.Reason == "Reaction restraint changed");
    }

    [Fact]
    public void ExportIncludesCurrentSolverSourceHash()
    {
        var exported = Results.Export(Catalog.Load("V01"));
        Assert.Equal(Acceptance.SolverHash(), exported.Provenance["solverSourceSha256"]);
    }
}
