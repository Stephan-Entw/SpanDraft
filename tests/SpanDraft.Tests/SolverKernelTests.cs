using System.Reflection;
using System.Runtime.CompilerServices;
using SpanDraft.Analysis;
using SpanDraft.Core.Beams;
using SpanDraft.Desktop.State;
using SpanDraft.Engineering;
using SpanDraft.Reporting.Notation;
using SpanDraft.Solver;
using SpanDraft.Solver.Kernel;
using SpanDraft.Tests.SolverReference;
using Xunit;

namespace SpanDraft.Tests;

public sealed class SolverKernelTests
{
    private const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Assembly Solver = typeof(EulerBernoulliBeamSolver).Assembly;

    [Fact]
    public void ProductEntryIsTheSingleSolveOfACoreBeamModel()
    {
        Type type = typeof(EulerBernoulliBeamSolver);
        MethodInfo solve = Assert.Single(type.GetMethods(Declared), m => !m.IsPrivate);
        Assert.Equal(nameof(EulerBernoulliBeamSolver.Solve), solve.Name);
        Assert.True(solve.IsPublic);
        Assert.Equal(typeof(BeamSolution), solve.ReturnType);
        Assert.Equal(typeof(BeamModel), Assert.Single(solve.GetParameters()).ParameterType);
        Assert.Empty(Assert.Single(type.GetConstructors(Declared)).GetParameters());
        Assert.Empty(type.GetFields(Declared));

        // No other public type of the solver assembly offers a way to compute a solution.
        Assert.All(Solver.GetExportedTypes().Where(t => t != type), exported =>
            Assert.DoesNotContain(exported.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .OfType<MethodBase>(), member => member.GetParameters().Any(p => p.ParameterType == typeof(BeamModel))));
        ConstructorInfo solution = Assert.Single(typeof(BeamSolution).GetConstructors(Declared));
        Assert.True(solution.IsAssembly);
        Assert.Equal(new[] { typeof(BeamModel), typeof(BendingResult) }, solution.GetParameters().Select(p => p.ParameterType));
    }

    [Fact]
    public void ProductPathCannotAcceptFutureFeaturePrimitives()
    {
        Assert.True(typeof(BeamModelAdapter).IsNotPublic);
        Assert.All(typeof(BeamModelAdapter).GetMethods(Declared).Where(m => !m.IsPrivate), entry =>
            Assert.Equal(typeof(BeamModel), Assert.Single(entry.GetParameters()).ParameterType));
        Assert.Equal(new[] { "EI", "Length", "LineLoads", "PointForces", "PointMoments", "Restraints" },
            typeof(MechanicalInput).GetProperties(BindingFlags.Instance | BindingFlags.NonPublic)
                .Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void SolverAssemblyContainsOnlyTheFacadeAndTheKernel()
    {
        // Every solver type outside the kernel namespace is listed here. A second calculation path
        // would need a new type and therefore a deliberate change of this list.
        string[] facade =
        [
            "BeamExtrema", "BeamExtremum`1", "BeamModelAdapter", "BeamNodeResult", "BeamSectionResult", "BeamSolution",
            "BeamSolverException", "Displacement", "EulerBernoulliBeamSolver", "EvaluationSide", "NumericalGuard",
            "PolynomialRoots", "SolverErrorCode"
        ];
        Type[] types = Solver.GetTypes().Where(t => !t.IsNested &&
            t.GetCustomAttribute<CompilerGeneratedAttribute>() is null).ToArray();
        Assert.Equal(facade, types.Where(t => t.Namespace == typeof(EulerBernoulliBeamSolver).Namespace)
            .Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.All(types.Where(t => t.Namespace != typeof(EulerBernoulliBeamSolver).Namespace), t =>
        {
            Assert.Equal(typeof(SolverKernel).Namespace, t.Namespace);
            Assert.True(t.IsNotPublic);
        });
    }

    [Fact]
    public void FrozenReferenceSolversAndMathNetAreOutsideEveryProductAssembly()
    {
        Assembly tests = typeof(ReferenceBeamSolver).Assembly;
        Assembly[] product =
        [
            typeof(BeamModel).Assembly, Solver, typeof(BeamEngineeringAnalysis).Assembly, typeof(BeamAnalysis).Assembly,
            typeof(TechnicalSymbols).Assembly, typeof(ProjectOverviewState).Assembly
        ];
        Assert.Equal(product.Length, product.Distinct().Count());
        Assert.DoesNotContain(tests, product);
        foreach (Type reference in new[]
        {
            typeof(ReferenceBeamSolver), typeof(ReferenceBeamSolution), typeof(SupportSpanSolver), typeof(SolverLoads),
            typeof(SolverModel), typeof(DofMap), typeof(BeamAssembly), typeof(ReducedSystemSolver),
            typeof(EulerBernoulliElement), typeof(ElementField)
        })
        {
            Assert.Same(tests, reference.Assembly);
            Assert.True(reference.IsNotPublic);
            Assert.All(product, assembly => Assert.DoesNotContain(assembly.GetTypes(), t => t.Name == reference.Name));
        }
        Assert.All(product, assembly => Assert.DoesNotContain(assembly.GetReferencedAssemblies(),
            name => name.Name!.StartsWith("MathNet", StringComparison.OrdinalIgnoreCase) || name.Name == tests.GetName().Name));
        Assert.Contains(tests.GetReferencedAssemblies(), name => name.Name == "MathNet.Numerics");
    }

    [Fact]
    public void SolverKernelIsInternal()
    {
        Assert.True(typeof(SolverKernel).IsNotPublic);
    }

    [Fact]
    public void KernelSignaturesUseNoCoreTypes()
    {
        Type[] types = Solver.GetTypes();
        foreach (Type type in types.Where(type => type.Namespace == typeof(SolverKernel).Namespace))
        {
            Assert.All(type.GetFields(Declared), field => AssertCoreIndependent(field.FieldType));
            Assert.All(type.GetMethods(Declared), method => AssertCoreIndependent(method.ReturnType));
            var methods = type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared));
            Assert.All(methods.SelectMany(method => method.GetParameters()),
                parameter => AssertCoreIndependent(parameter.ParameterType));
        }
    }

    private static void AssertCoreIndependent(Type type)
    {
        Assert.NotEqual(typeof(BeamModel).Assembly, type.Assembly);
        if (type.GetElementType() is { } element)
            AssertCoreIndependent(element);
        Assert.All(type.GetGenericArguments(), AssertCoreIndependent);
    }
}
