using SpanDraft.Core.Beams;
using SpanDraft.Core.Loads;
using SpanDraft.Core.Supports;

namespace SpanDraft.Core.Validation;

/// <summary>
/// Checks cross-object consistency. Beam endpoints are included; duplicate and coincidence comparisons are exact.
/// A spring support must not share its exact position with any other support. The segments must tile the beam
/// exactly; a segment boundary may share its position with supports and loads.
/// </summary>
public static class BeamModelValidator
{
    public static IReadOnlyList<ValidationError> Validate(BeamModel beam)
    {
        ArgumentNullException.ThrowIfNull(beam);
        var errors = new List<ValidationError>();
        double expectedStart = 0;
        for (int i = 0; i < beam.Segments.Count; i++)
        {
            BeamSegment segment = beam.Segments[i];
            if (segment.Start.Meters != expectedStart)
                errors.Add(new(ValidationErrorCode.SegmentsNotContiguous, i == 0
                    ? "The first segment must start at the beam start."
                    : "A segment must start exactly where the previous one ends.", $"Segments[{i}].Start"));
            expectedStart = segment.End.Meters;
        }
        if (expectedStart != beam.Length.Meters)
            errors.Add(new(ValidationErrorCode.SegmentsNotContiguous,
                "The last segment must end at the beam end.", $"Segments[{beam.Segments.Count - 1}].End"));
        if (beam.Supports.Count == 0)
            errors.Add(new(ValidationErrorCode.MissingSupports, "The beam requires at least one support.", "Supports"));

        var seenSupports = new HashSet<(double Position, SupportType Type)>();
        var positions = new Dictionary<double, bool>();
        for (int i = 0; i < beam.Supports.Count; i++)
        {
            Support support = beam.Supports[i];
            if (support.Position.Meters > beam.Length.Meters)
                errors.Add(new(ValidationErrorCode.SupportOutsideBeam, "Support position is outside the beam.", $"Supports[{i}].Position"));
            bool spring = support.Type == SupportType.Spring;
            if (positions.TryGetValue(support.Position.Meters, out bool springBefore))
            {
                if (spring || springBefore)
                    errors.Add(new(ValidationErrorCode.SpringSupportCoincidence,
                        "A spring support cannot share its position with another support.", $"Supports[{i}]"));
                positions[support.Position.Meters] = springBefore || spring;
            }
            else
                positions.Add(support.Position.Meters, spring);

            if (!spring && !seenSupports.Add((support.Position.Meters, support.Type)))
                errors.Add(new(ValidationErrorCode.DuplicateSupport, "An identical support already exists at this position.", $"Supports[{i}]"));
        }

        for (int i = 0; i < beam.Loads.Count; i++)
        {
            switch (beam.Loads[i])
            {
                case PointForce force when force.Position.Meters > beam.Length.Meters:
                    errors.Add(new(ValidationErrorCode.PointForceOutsideBeam, "Point force position is outside the beam.", $"Loads[{i}].Position"));
                    break;
                case PointMoment moment when moment.Position.Meters > beam.Length.Meters:
                    errors.Add(new(ValidationErrorCode.PointMomentOutsideBeam, "Point moment position is outside the beam.", $"Loads[{i}].Position"));
                    break;
                case DistributedLoad load when load.EndPosition.Meters > beam.Length.Meters:
                    errors.Add(new(ValidationErrorCode.DistributedLoadOutsideBeam, "Distributed load extends outside the beam.", $"Loads[{i}]"));
                    break;
            }
        }

        return errors.AsReadOnly();
    }
}
