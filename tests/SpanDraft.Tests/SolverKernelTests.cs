using System.Reflection;
using SpanDraft.Core.Beams;
using SpanDraft.Solver;
using SpanDraft.Solver.Kernel;
using Xunit;

namespace SpanDraft.Tests;

public sealed class SolverKernelTests
{
    [Fact]
    public void MigrationEntriesCannotAcceptFutureFeaturePrimitivesOrSolverLoads()
    {
        const BindingFlags methods = BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.NonPublic;
        foreach (var (type, name) in new[] { (typeof(BeamModelAdapter), "Adapt"), (typeof(KernelBeamSolver), "Solve") })
        {
            MethodInfo entry = Assert.Single(type.GetMethods(methods), m => m.Name == name);
            Assert.Equal(typeof(BeamModel), Assert.Single(entry.GetParameters()).ParameterType);
            Assert.True(type.IsNotPublic);
        }
        Assert.Equal(new[] { "EI", "Length", "LineLoads", "PointForces", "PointMoments", "Restraints" },
            typeof(MechanicalInput).GetProperties(BindingFlags.Instance | BindingFlags.NonPublic)
                .Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void SolverKernelIsInternal()
    {
        Assert.True(typeof(SolverKernel).IsNotPublic);
    }

    [Fact]
    public void KernelSignaturesUseNoCoreTypes()
    {
        const BindingFlags members = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic;
        Type[] types = typeof(SolverKernel).Assembly.GetTypes();
        foreach (Type type in types.Where(type => type.Namespace == typeof(SolverKernel).Namespace))
        {
            Assert.All(type.GetFields(members), field => AssertCoreIndependent(field.FieldType));
            Assert.All(type.GetMethods(members), method => AssertCoreIndependent(method.ReturnType));
            var methods = type.GetMethods(members).Cast<MethodBase>().Concat(type.GetConstructors(members));
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
