using System.Reflection;
using System.Runtime.ExceptionServices;
using SpanDraft.Core.Sections.Geometry;
using SpanDraft.Core.Units;
using Xunit;
using static SpanDraft.Tests.ParametricSectionTestSupport;
using static SpanDraft.Tests.SectionGeometryTestSupport;

namespace SpanDraft.Tests;

public class ParametricSectionInputTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void ParametersAreImmutableRetainedAndDeterministicallyReconstructGeometry(int kind)
    {
        var shape = Shape(kind, r: kind is 1 or >= 4 ? 0.5 : 0);
        var type = shape.GetType();
        Assert.True(type.IsSealed);
        Assert.Equal(typeof(object), type.BaseType);
        Assert.All(type.GetProperties(), property => Assert.False(property.CanWrite));
        var constructor = Assert.Single(type.GetConstructors());
        var values = constructor.GetParameters().Select(parameter =>
        {
            Assert.Equal(typeof(Length), parameter.ParameterType);
            Assert.False(parameter.IsOptional);
            return Property(shape, parameter.Name!);
        }).ToArray();
        var copy = constructor.Invoke(values);
        foreach (var property in type.GetProperties().Where(p => p.PropertyType == typeof(Length)))
            Assert.Equal(property.GetValue(shape), property.GetValue(copy));
        var first = Geometry(shape);
        Assert.Same(first, Geometry(shape));
        var second = Geometry(copy);
        Assert.NotSame(first, second);
        Assert.Equal(first.Holes.Count, second.Holes.Count);
        foreach (var (a, b) in new[] { first.OuterContour }.Concat(first.Holes)
            .Zip(new[] { second.OuterContour }.Concat(second.Holes)))
        {
            Assert.Equal(a.Segments.Count, b.Segments.Count);
            foreach (var (segment, other) in a.Segments.Zip(b.Segments))
            {
                Assert.Equal(segment.GetType(), other.GetType());
                Assert.Equal(segment.Start, other.Start);
                Assert.Equal(segment.End, other.End);
                if (segment is SectionArc arc)
                {
                    var otherArc = Assert.IsType<SectionArc>(other);
                    Assert.Equal(arc.Center, otherArc.Center);
                    Assert.Equal(arc.Radius, otherArc.Radius);
                    Assert.Equal(arc.Direction, otherArc.Direction);
                    Assert.Equal(arc.SweepRadians, otherArc.SweepRadians);
                }
            }
        }
        SameProperties(first.CalculateProperties(), second.CalculateProperties());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void EveryConstructorParameterRejectsInvalidSingleValues(int kind)
    {
        var shape = Shape(kind);
        foreach (var parameter in Assert.Single(shape.GetType().GetConstructors()).GetParameters())
        {
            var isRadius = parameter.Name!.Contains("radius", StringComparison.OrdinalIgnoreCase);
            foreach (var invalid in new[] { -1.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                Assert.Throws<ArgumentOutOfRangeException>(() => Change(shape, parameter.Name!, invalid));
            if (!isRadius)
            {
                var error = Assert.Throws<ArgumentOutOfRangeException>(() => Change(shape, parameter.Name!, 0));
                Assert.Equal(parameter.Name, error.ParamName);
            }
            else
                Assert.NotNull(Geometry(Change(shape, parameter.Name!, 0)));
        }
    }

    public static IEnumerable<object[]> InvalidCombinations()
    {
        foreach (var t in new[] { 3.0, 3.5, 10.0 })
        {
            yield return [1, "wallThickness", t];
            yield return [3, "wallThickness", t];
        }
        yield return [1, "outerRadius", Math.BitIncrement(3)];
        foreach (var kind in new[] { 4, 5, 6 })
        {
            yield return [kind, "webThickness", 6.0];
            yield return [kind, "webThickness", 7.0];
            yield return [kind, "flangeThickness", kind == 6 ? 10.0 : 5.0];
            yield return [kind, "flangeThickness", 11.0];
            yield return [kind, "radius", Math.BitIncrement(kind == 5 ? 4.0 : 2.5)];
        }
        yield return [7, "thickness", 6.0];
        yield return [7, "thickness", 7.0];
        yield return [7, "innerRadius", Math.BitIncrement(5)];
    }

    [Theory]
    [MemberData(nameof(InvalidCombinations))]
    public void InvalidParameterCombinationsAreRejectedBeforeTopology(int kind, string parameter, double value)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Change(Shape(kind), parameter, value));
        Assert.Equal(parameter, error.ParamName);
    }

    [Theory]
    [InlineData(4, 10, 6, 2, 1, 2.01)]
    [InlineData(5, 3, 10, 1, 1, 2.01)]
    [InlineData(6, 10, 3, 2, 1, 2.01)]
    [InlineData(7, 10, 3, 1, 1, 2.01)]
    [InlineData(1, 10, 6, 1, 1, 3.01)]
    public void TheOtherRadiusLimitIsAlsoChecked(int kind, double b, double h, double t, double tf, double r)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Shape(kind, b, h, t, tf, r));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void StandardBoundsHoleCountsAndSharpContoursAreCorrect(int kind)
    {
        var geometry = Geometry(Shape(kind));
        Assert.Equal(kind is 1 or 3 ? 1 : 0, geometry.Holes.Count);
        var points = geometry.OuterContour.Segments.SelectMany(s => new[] { s.Start, s.End }).ToArray();
        Assert.Equal(0, points.Min(p => p.Y.Meters));
        Assert.Equal(0, points.Min(p => p.Z.Meters));
        Assert.Equal(6, points.Max(p => p.Y.Meters));
        Assert.Equal(kind is 2 or 3 ? 6 : 10, points.Max(p => p.Z.Meters));
        foreach (var contour in new[] { geometry.OuterContour }.Concat(geometry.Holes))
        {
            AssertClosed(contour);
            if (kind is 2 or 3)
            {
                Assert.Equal(4, contour.Segments.Count);
                Assert.All(contour.Segments, segment =>
                {
                    var arc = Assert.IsType<SectionArc>(segment);
                    Close(Math.PI / 2, arc.SweepRadians);
                    Assert.Equal(P(3, 3), arc.Center);
                });
            }
            else
                Assert.All(contour.Segments, s => Assert.IsType<SectionLine>(s));
        }
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(4, 4)]
    [InlineData(5, 2)]
    [InlineData(6, 2)]
    [InlineData(7, 1)]
    public void RoundedTransitionsUseExactTangentQuarterArcs(int kind, int arcCount)
    {
        var geometry = Geometry(Shape(kind, r: 1.5));
        var contour = geometry.OuterContour;
        Assert.Equal(arcCount, contour.Segments.OfType<SectionArc>().Count());
        AssertClosed(contour);
        for (var i = 0; i < contour.Segments.Count; i++)
        {
            if (contour.Segments[i] is not SectionArc arc) continue;
            Close(1.5, arc.Radius.Meters);
            Close(Math.PI / 2, Math.Abs(arc.SweepRadians));
            Assert.Equal(kind == 1 ? ArcDirection.Counterclockwise : ArcDirection.Clockwise, arc.Direction);
            var before = Assert.IsType<SectionLine>(contour.Segments[(i + contour.Segments.Count - 1) % contour.Segments.Count]);
            var after = Assert.IsType<SectionLine>(contour.Segments[(i + 1) % contour.Segments.Count]);
            AssertTangent(before, arc, arc.Start);
            AssertTangent(after, arc, arc.End);
        }
        if (kind == 1)
        {
            Assert.Equal(4, Assert.Single(geometry.Holes).Segments.OfType<SectionArc>().Count());
            Assert.All(geometry.Holes[0].Segments.OfType<SectionArc>(), arc => Close(0.5, arc.Radius.Meters));
        }
    }

    [Theory]
    [InlineData(1, 3, 2, 4)]
    [InlineData(4, 2.5, 8, 4)]
    [InlineData(5, 4, 7, 2)]
    [InlineData(6, 2.5, 6, 2)]
    [InlineData(7, 5, 5, 1)]
    public void ExactLimitsOmitOnlyTheConsumedLines(int kind, double r, int lines, int arcs)
    {
        var geometry = Geometry(Shape(kind, r: r));
        Assert.Equal(lines, geometry.OuterContour.Segments.OfType<SectionLine>().Count());
        Assert.Equal(arcs, geometry.OuterContour.Segments.OfType<SectionArc>().Count());
        AssertClosed(geometry.OuterContour);
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(4, 2.5)]
    [InlineData(5, 4)]
    [InlineData(6, 2.5)]
    [InlineData(7, 5)]
    public void PositiveRestEdgesNearRadiusLimitsRemainValid(int kind, double limit)
    {
        var r = limit - Math.ScaleB(1, -20);
        var geometry = Geometry(Shape(kind, r: r));
        AssertClosed(geometry.OuterContour);
        AssertTensor(Reference(kind, r: r), geometry.CalculateProperties());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void NearExhaustedButRepresentableMaterialRemainsValid(int kind)
    {
        var near = 1 - Math.ScaleB(1, -20);
        var t = kind is 1 or 3 ? near * 3 : near * 6;
        var tf = kind == 6 ? near * 10 : near * 5;
        var geometry = Geometry(Shape(kind, t: t, tf: tf));
        var properties = geometry.CalculateProperties();
        AssertTensor(Reference(kind, t: t, tf: tf), properties);
        Assert.True(properties.I2.MetersToTheFourth > 0);
    }

    private static object Property(object shape, string name) => shape.GetType().GetProperties()
        .Single(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).GetValue(shape)!;

    private static object Change(object shape, string parameter, double value)
    {
        var constructor = Assert.Single(shape.GetType().GetConstructors());
        var values = constructor.GetParameters().Select(p => p.Name == parameter ? (object)M(value) : Property(shape, p.Name!)).ToArray();
        try { return constructor.Invoke(values); }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static void AssertClosed(SectionContour contour)
    {
        for (var i = 0; i < contour.Segments.Count; i++)
        {
            var segment = contour.Segments[i];
            Assert.NotEqual(segment.Start, segment.End);
            Assert.Equal(segment.End, contour.Segments[(i + 1) % contour.Segments.Count].Start);
        }
    }

    private static void AssertTangent(SectionLine line, SectionArc arc, SectionPoint point)
    {
        var dy = line.End.Y.Meters - line.Start.Y.Meters;
        var dz = line.End.Z.Meters - line.Start.Z.Meters;
        var ry = point.Y.Meters - arc.Center.Y.Meters;
        var rz = point.Z.Meters - arc.Center.Z.Meters;
        Close(0, dy * ry + dz * rz, arc.Radius.Meters * Math.Max(Math.Abs(dy), Math.Abs(dz)));
    }
}
