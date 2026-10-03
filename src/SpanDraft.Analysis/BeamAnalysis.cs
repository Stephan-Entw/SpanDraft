using SpanDraft.Core.Beams;
using SpanDraft.Engineering;
using SpanDraft.Solver;

namespace SpanDraft.Analysis;

/// <summary>Application entry point for solving and assessing one beam.</summary>
public static class BeamAnalysis
{
    /// <summary>Returns expected solver failures; engineering and unexpected exceptions propagate.</summary>
    public static BeamAnalysisOutcome Analyze(BeamModel beam)
    {
        ArgumentNullException.ThrowIfNull(beam);

        BeamSolution solution;
        try
        {
            solution = new EulerBernoulliBeamSolver().Solve(beam);
        }
        catch (BeamSolverException exception)
        {
            return BeamAnalysisOutcome.FromFailure(new BeamAnalysisFailure(exception));
        }

        BeamEngineeringResult engineering = BeamEngineeringAnalysis.Analyze(solution);
        return BeamAnalysisOutcome.Success(new BeamAnalysisResult(solution, engineering));
    }
}
