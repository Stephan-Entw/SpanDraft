using SpanDraft.Core.Sections;

namespace SpanDraft.Desktop.Persistence;

/// <summary>The wire names of the section axes, shared by project, recovery and library files.</summary>
internal static class SectionAxisWire
{
    internal static string Write(SectionAxisDesignation axis) => axis switch
    {
        SectionAxisDesignation.Y => "y", SectionAxisDesignation.Z => "z",
        SectionAxisDesignation.U => "u", SectionAxisDesignation.V => "v",
        _ => throw new ArgumentOutOfRangeException(nameof(axis))
    };

    internal static SectionAxisDesignation Read(string value) => value switch
    {
        "y" => SectionAxisDesignation.Y, "z" => SectionAxisDesignation.Z,
        "u" => SectionAxisDesignation.U, "v" => SectionAxisDesignation.V,
        _ => throw new ArgumentException("Unknown section axis.", nameof(value))
    };
}
