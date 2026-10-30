using SpanDraft.Analysis;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Materials;
using SpanDraft.Core.Sections;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class DesktopSelfWeightTests
{
    internal static Material Steel(double? density = 7850) => new("Steel", Pressure.FromPascals(210e9),
        Pressure.FromPascals(235e6), MaterialStrengthType.Re,
        density is { } rho ? MassDensity.FromKilogramsPerCubicMeter(rho) : null, null);
    internal static EditorDocument Document(bool enabled = false) => new(M(3), Steel(),
        new SpanDraft.Core.Sections.Parametric.RectangleSectionGeometry(M(.1), M(.2)), SectionAxisDesignation.Y, [new(Guid.NewGuid(), M(0), SupportType.Fixed, "A")],
        selfWeight: enabled ? new(Guid.NewGuid(), true) : null);

    [Fact]
    public void FormulaMapsToExistingUdlAndMatchesCantileverReference()
    {
        var document = Document(true);
        var load = Assert.IsType<UniformDistributedLoad>(Assert.Single(document.ToBeamModel().Loads));
        Assert.Equal(-1539.64405, load.Intensity.NewtonsPerMeter, 8);
        Assert.Equal(M(0), load.StartPosition); Assert.Equal(M(3), load.EndPosition);
        Assert.Empty(document.DistributedLoads);
        Assert.DoesNotContain(document.NamedEntities, e => e.Id == document.SelfWeight!.Id);
        Assert.Contains(document.SelfWeight!.Id, document.EntityIds);
        var explicitLoad = document with { SelfWeight = null };
        explicitLoad = explicitLoad.WithDistributedLoads([new(Guid.NewGuid(), M(0), M(3), load.Intensity, "q1")]);
        var solution = SolverTestSupport.Solve(document.ToBeamModel());
        SolverTestSupport.SameResults(SolverTestSupport.Solve(explicitLoad.ToBeamModel()), solution);
        SolverTestSupport.ForceClose(4618.93215, SolverTestSupport.At(solution, 0).ReactionY);
        double ei = 210e9 * document.Section.GetAxis(document.BendingAxis).SecondMomentOfArea.MetersToTheFourth;
        SolverTestSupport.DisplacementClose(-1539.64405 * 81 / (8 * ei), SolverTestSupport.At(solution, 3).TransverseDisplacement);
    }

}
