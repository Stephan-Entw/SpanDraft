namespace SpanDraft.Core.Validation;

/// <summary>Stable categories of model consistency errors; no solver stability checks are included.</summary>
public enum ValidationErrorCode
{
    MissingSupports,
    SupportOutsideBeam,
    DuplicateSupport,
    PointForceOutsideBeam,
    PointMomentOutsideBeam,
    DistributedLoadOutsideBeam
}

/// <summary>A model error with a machine-readable code, explanatory message and property path (zero-based collection indices).</summary>
public sealed record ValidationError(ValidationErrorCode Code, string Message, string Path);
