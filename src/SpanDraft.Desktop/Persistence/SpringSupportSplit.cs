using System.Security.Cryptography;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.Persistence;

/// <summary>
/// Earlier V5 files stored a translational and a rotational spring as one <c>spring</c> entry. The editor knows two
/// supports instead, so such an entry is read as two: the translational spring keeps the entry's identity, name,
/// position and reference; the rotational spring gets the same position and reference, the next free support name
/// and an identity derived from the entry's, so that reading the same file again gives the same project.
/// </summary>
internal static class SpringSupportSplit
{
    private const string Salt = "SpanDraft.RotationalSpring";

    public static DocumentV5 Apply(DocumentV5 document)
    {
        var supports = ProjectFileCodec.Required(document.Supports, "supports");
        if (!supports.Any(s => s is { Type: "spring", TranslationalStiffness: not null, RotationalStiffness: not null }))
            return document;

        var pointLoads = ProjectFileCodec.Required(document.PointLoads, "pointLoads");
        var distributedLoads = ProjectFileCodec.Required(document.DistributedLoads, "distributedLoads");
        var releases = document.MomentReleases ?? [];
        var naming = ProjectFileCodec.Required(document.NamingState, "namingState");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { EditorSelfWeight.Symbol };
        var ids = new HashSet<Guid>();
        foreach (var s in supports) { names.Add(ProjectFileCodec.Required(s, "support").Name); ids.Add(s.Id); }
        foreach (var l in pointLoads) { names.Add(ProjectFileCodec.Required(l, "pointLoad").Name); ids.Add(l.Id); }
        foreach (var l in distributedLoads) { names.Add(ProjectFileCodec.Required(l, "distributedLoad").Name); ids.Add(l.Id); }
        foreach (var r in releases) { names.Add(ProjectFileCodec.Required(r, "momentRelease").Name); ids.Add(r.Id); }
        if (document.SelfWeight is { } weight) ids.Add(weight.Id);

        long ordinal = naming.NextSupportOrdinal;
        var result = new List<SupportV5>();
        foreach (var support in supports)
        {
            if (support is not { Type: "spring", TranslationalStiffness: not null, RotationalStiffness: not null })
            {
                result.Add(support);
                continue;
            }
            while (names.Contains(EntityNaming.Format(AutoNameKind.Support, ordinal))) ordinal = checked(ordinal + 1);
            string name = EntityNaming.Format(AutoNameKind.Support, ordinal);
            names.Add(name);
            ordinal = checked(ordinal + 1);
            var id = Derive(support.Id, ids);
            ids.Add(id);
            result.Add(support with { RotationalStiffness = null, RigidTranslation = null });
            result.Add(support with { Id = id, Name = name, TranslationalStiffness = null });
        }
        return document with { Supports = [.. result], NamingState = naming with { NextSupportOrdinal = ordinal } };
    }

    /// <summary>A stable identity for the second spring: never empty and not used by any other entity of the project.</summary>
    internal static Guid Derive(Guid original, ISet<Guid> taken)
    {
        Span<byte> input = stackalloc byte[16 + 4];
        original.TryWriteBytes(input);
        for (int attempt = 0; ; attempt++)
        {
            BitConverter.TryWriteBytes(input[16..], attempt);
            var hash = SHA256.HashData([.. input, .. System.Text.Encoding.UTF8.GetBytes(Salt)]);
            var candidate = new Guid(hash.AsSpan(0, 16));
            if (candidate != Guid.Empty && !taken.Contains(candidate)) return candidate;
        }
    }
}
