using System.Text;
using System.Text.Json.Nodes;
using SpanDraft.Analysis;
using SpanDraft.Core.Supports;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Controls;
using SpanDraft.Desktop.Layout;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;
using Xunit;

namespace SpanDraft.Tests;

/// <summary>Spring supports in the desktop state: units and settings, geometry, hit testing, layout and overview text.</summary>
public sealed class DesktopSpringSupportTests
{
    private static Length Mm(double value) => Length.FromMillimeters(value);
    private static SpringData Translational(double k) => new(TranslationalStiffness.FromNewtonsPerMeter(k), null);
    private static SpringData Rotational(double k) => new(null, RotationalStiffness.FromNewtonMetersPerRadian(k));
    private static SpringData Combined(double k, double kt) =>
        new(TranslationalStiffness.FromNewtonsPerMeter(k), RotationalStiffness.FromNewtonMetersPerRadian(kt));
    private static SpringData Partial(double kt) => new(null, RotationalStiffness.FromNewtonMetersPerRadian(kt), true);

    [Fact]
    public void StiffnessUnitsConvertToSiAndEveryBuiltInProfileProvidesBoth()
    {
        Assert.Equal(5e6, UnitCatalog.NewtonPerMillimeter.ToSi(5e3));
        Assert.Equal(2e6, UnitCatalog.KilonewtonPerMillimeter.ToSi(2));
        Assert.Equal(7e3, UnitCatalog.KilonewtonPerMeterStiffness.ToSi(7));
        Assert.Equal(3e4, UnitCatalog.KilonewtonMeterPerRadian.ToSi(30));
        NumericAssert.Close(4448.2216152605 / 0.0254, UnitCatalog.KipPerInch.ToSi(1));
        Assert.All(new[] { UnitProfile.MechanicalEngineering, UnitProfile.StructuralEngineering, UnitProfile.UnitedStates }, p =>
        {
            Assert.Equal(UnitDimension.TranslationalStiffness, p[QuantityKind.TranslationalStiffness].Dimension);
            Assert.Equal(UnitDimension.RotationalStiffness, p[QuantityKind.RotationalStiffness].Dimension);
        });
        Assert.Throws<ArgumentException>(() => UnitProfile.Default.WithUnit(QuantityKind.TranslationalStiffness, UnitCatalog.NewtonPerMeter));
        Assert.Throws<ArgumentException>(() => UnitProfile.Default.WithUnit(QuantityKind.RotationalStiffness, UnitCatalog.NewtonPerMillimeter));
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void SettingsWrittenBeforeSpringSupportsKeepTheirCustomUnitsAndGetStandardStiffnessUnits(int version)
    {
        var settings = UserSettings.Default.WithUnit(QuantityKind.BeamLength, UnitCatalog.Inch)
            .WithUnit(QuantityKind.Stress, UnitCatalog.Ksi);
        var json = JsonNode.Parse(SettingsCodec.Serialize(settings))!.AsObject();
        json["version"] = version;
        // Settings files before DEV-19 know neither stiffness quantity; those before version 4 lack the material quantities too.
        foreach (var kind in new[] { QuantityKind.TranslationalStiffness, QuantityKind.RotationalStiffness })
            json["customUnits"]!.AsObject().Remove(kind.ToString());
        if (version < 4)
        {
            foreach (var kind in new[] { QuantityKind.YoungsModulus, QuantityKind.MaterialStrength, QuantityKind.Density, QuantityKind.PoissonRatio })
                json["customUnits"]!.AsObject().Remove(kind.ToString());
            json["presentationMode"] = "Standard";
            json.Remove("significantDigits"); json.Remove("notation");
        }
        var loaded = SettingsCodec.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.True(loaded.HasCustomProfile);
        Assert.Same(UnitCatalog.Inch, loaded.Profile[QuantityKind.BeamLength]);
        Assert.Same(UnitCatalog.Ksi, loaded.Profile[QuantityKind.Stress]);
        Assert.Equal(UserSettings.Standard(settings.LastStandardProfile)[QuantityKind.TranslationalStiffness].Id,
            loaded.Profile[QuantityKind.TranslationalStiffness].Id);
        Assert.Equal(UserSettings.Standard(settings.LastStandardProfile)[QuantityKind.RotationalStiffness].Id,
            loaded.Profile[QuantityKind.RotationalStiffness].Id);
    }

    [Fact]
    public async Task AFileWithoutStiffnessEntriesIsNotReplacedByTheDefaultSettings()
    {
        var settings = UserSettings.Default.WithUnit(QuantityKind.BeamLength, UnitCatalog.Foot);
        var json = JsonNode.Parse(SettingsCodec.Serialize(settings))!.AsObject();
        json["customUnits"]!.AsObject().Remove(QuantityKind.TranslationalStiffness.ToString());
        json["customUnits"]!.AsObject().Remove(QuantityKind.RotationalStiffness.ToString());
        var files = new ProjectTestSupport.Files();
        files.Data["settings"] = Encoding.UTF8.GetBytes(json.ToJsonString());
        var loaded = await new LocalSettingsStore(files, "settings").LoadAsync();
        Assert.NotSame(UserSettings.Default, loaded);
        Assert.Same(UnitCatalog.Foot, loaded.Profile[QuantityKind.BeamLength]);
    }

    [Fact]
    public void CurrentSettingsRoundtripACustomStiffnessUnit()
    {
        var settings = UserSettings.Default.WithUnit(QuantityKind.TranslationalStiffness, UnitCatalog.KilonewtonPerMillimeter)
            .WithUnit(QuantityKind.RotationalStiffness, UnitCatalog.KilonewtonMeterPerRadian);
        var loaded = SettingsCodec.Deserialize(SettingsCodec.Serialize(settings));
        Assert.Same(UnitCatalog.KilonewtonPerMillimeter, loaded.Profile[QuantityKind.TranslationalStiffness]);
        Assert.Same(UnitCatalog.KilonewtonMeterPerRadian, loaded.Profile[QuantityKind.RotationalStiffness]);
        Assert.True(settings.ContentEquals(loaded));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    public void GeometryPartsFollowTheSpringsAndShareTheHingedExtentsAndGround(bool translational, bool rotational, bool rigid)
    {
        var geometry = new SpringSupportGeometry(translational, rotational, rigid);
        var hinged = new HingedSupportGeometry(true);
        Assert.Equal(hinged.LeftExtent, geometry.LeftExtent);
        Assert.Equal(hinged.RightExtent, geometry.RightExtent);
        Assert.Equal(hinged.Bottom, geometry.Bottom);
        Assert.Equal(hinged.Ground, geometry.Ground);
        Assert.Equal(rotational, geometry.Spiral.Any());
        Assert.Equal(rotational ? SpringSupportGeometry.SpiralOuterRadius : hinged.Joint.Radius,
            -geometry.Top - SchematicMetrics.SymbolStrokeWidth / 2);
        // The body reaches from the joint to the ground without leaving the symbol's horizontal extent.
        var body = geometry.Body.ToArray();
        Assert.NotEmpty(body);
        Assert.True(body.SelectMany(l => new[] { l.StartY, l.EndY }).Min() <= hinged.Joint.Radius + 1e-9);
        Assert.All(body.Concat(geometry.Spiral), l =>
        {
            Assert.InRange(l.StartX, -geometry.LeftExtent, geometry.RightExtent);
            Assert.InRange(l.EndX, -geometry.LeftExtent, geometry.RightExtent);
            Assert.InRange(l.StartY, geometry.Top, geometry.Bottom);
            Assert.InRange(l.EndY, geometry.Top, geometry.Bottom);
        });
        Assert.True(body.Select(l => Math.Max(l.StartY, l.EndY)).Max() <= SchematicMetrics.SupportGroundY + 1e-9);
        if (translational)
            Assert.Contains(body, l => l.StartX != 0 || l.EndX != 0);
        else
            Assert.Contains(body, l => l.StartY != l.EndY || l.StartX != l.EndX);
        // The zigzag starts and ends on the support axis.
        if (translational)
        {
            Assert.Equal(0, body[0].StartX);
            Assert.Equal(0, body[^1].EndX);
            Assert.Equal(SchematicMetrics.SupportGroundY, body[^1].EndY, 9);
        }
    }

    [Fact]
    public void TheSpiralOfARotationalSpringRaisesTheTopAndTheHitZoneOnly()
    {
        var plain = new SpringSupportGeometry(true, false, false);
        var coiled = new SpringSupportGeometry(true, true, false);
        Assert.True(coiled.Top < plain.Top);
        double dy = plain.Top - 3;
        Assert.False(plain.Contains(0, dy, 0));
        Assert.True(coiled.Contains(0, dy, 0));
        Assert.False(coiled.Contains(coiled.RightExtent + 6, 0, 5));
    }

    [Fact]
    public void HitTestFindsSpringSupportsAtTheirSymbolAndNotElsewhere()
    {
        var v = DesktopLayoutFixture.Linear(0, 1000, 200, 1);
        EditorSupport[] supports =
        [
            new(Guid.NewGuid(), Mm(200), SupportType.Spring, "A", Translational(1e5)),
            new(Guid.NewGuid(), Mm(600), SupportType.Spring, "B", Partial(1e4))
        ];
        double beamY = v.Viewport.BeamY;
        Assert.Equal(supports[0].Id, SupportSymbol.HitTest(supports, v.Layout.Transform, beamY, 200, beamY + 12));
        Assert.Equal(supports[1].Id, SupportSymbol.HitTest(supports, v.Layout.Transform, beamY, 600, beamY + 12));
        Assert.Null(SupportSymbol.HitTest(supports, v.Layout.Transform, beamY, 400, beamY + 12));
        Assert.Null(SupportSymbol.HitTest(supports, v.Layout.Transform, beamY, 200, beamY + 60));
        // A spring without its data cannot be hit: there is no symbol to find.
        Assert.Null(SupportSymbol.HitTest([supports[0] with { Spring = null }], v.Layout.Transform, beamY, 200, beamY + 12));
    }

    [Fact]
    public void StationRequirementsOfSpringSupportsEqualThoseOfTheHingedSupports()
    {
        var spring = new EditorDocument(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section,
            [new(Guid.NewGuid(), Mm(0), SupportType.Pinned, "A"), new(Guid.NewGuid(), Mm(500), SupportType.Spring, "B", Combined(1e5, 1e4))]);
        var rigid = new EditorDocument(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section,
            [new(Guid.NewGuid(), Mm(0), SupportType.Pinned, "A"), new(Guid.NewGuid(), Mm(500), SupportType.Roller, "B")]);
        var a = StationRequirementBuilder.FromDocument(spring, 1.25);
        var b = StationRequirementBuilder.FromDocument(rigid, 1.25);
        Assert.Equal(b, a);
    }

    [Fact]
    public void OverviewListsEverySpringWithItsStiffnessInTheDisplayUnits()
    {
        var document = new EditorDocument(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section,
            [new(Guid.NewGuid(), Mm(0), SupportType.Pinned, "A"),
                new(Guid.NewGuid(), Mm(200), SupportType.Spring, "B", Translational(2.5e6)),
                new(Guid.NewGuid(), Mm(400), SupportType.Spring, "C", Rotational(3e4)),
                new(Guid.NewGuid(), Mm(600), SupportType.Spring, "D", Combined(1.5e6, 8e3)),
                new(Guid.NewGuid(), Mm(800), SupportType.Spring, "E", Partial(4.5e4))]);
        var state = ProjectOverviewState.From(document, AnalysisPresentationState.FromOutcome(BeamAnalysis.Analyze(document.ToBeamModel())));
        // Digit grouping and the language of the type name follow the culture; the content does not.
        string[] types = state.Supports.Select(x => string.Concat(x.Type.Where(c => !char.IsWhiteSpace(c)))).ToArray();
        string name = string.Concat(Strings.SpringSupport.Where(c => !char.IsWhiteSpace(c)));
        string rigid = string.Concat(Strings.SpringRigidTranslation.Where(c => !char.IsWhiteSpace(c)));
        Assert.Equal(name + ",k=2500N/mm", types[1]);
        Assert.Equal(name + ",kθ=30000N·m/rad", types[2]);
        Assert.Equal(name + ",k=1500N/mm,kθ=8000N·m/rad", types[3]);
        Assert.Equal(name + "," + rigid + ",kθ=45000N·m/rad", types[4]);
        Assert.NotEmpty(state.Reactions);
    }

    [Fact]
    public void ComparerTreatsEveryStiffnessKindAndTypeChangeAsMechanical()
    {
        EditorDocument Make(SpringData spring) => new(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section,
            [new(Guid.NewGuid(), Mm(0), SupportType.Pinned, "A"), new(Guid.NewGuid(), Mm(500), SupportType.Spring, "B", spring)]);
        var reference = Make(Combined(1e5, 1e4)).ToBeamModel();
        Assert.True(BeamModelMechanicalComparer.AreEquivalent(reference, Make(Combined(1e5, 1e4)).ToBeamModel()));
        foreach (var other in new[] { Combined(2e5, 1e4), Combined(1e5, 2e4), Translational(1e5), Rotational(1e4), Partial(1e4) })
            Assert.False(BeamModelMechanicalComparer.AreEquivalent(reference, Make(other).ToBeamModel()));
        Assert.True(BeamModelMechanicalComparer.IsKnownSupportType(SupportType.Spring));
    }

    [Fact]
    public void ASpringSupportWithoutItsSpringsCannotBecomeACoreModel()
    {
        var document = new EditorDocument(Mm(1000), ProjectTemplates.Material, ProjectTemplates.Section,
            [new(Guid.NewGuid(), Mm(500), SupportType.Spring, "B")]);
        Assert.Throws<InvalidOperationException>(() => document.ToBeamModel());
    }
}
