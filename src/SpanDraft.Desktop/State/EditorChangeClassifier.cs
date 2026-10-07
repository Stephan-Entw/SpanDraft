namespace SpanDraft.Desktop.State;

public enum EditorChangeKind { None, MetadataOnly, Mechanical }

/// <summary>Every desktop commit uses this boundary; individual controls do not choose analysis behavior.</summary>
public static class EditorChangeClassifier
{
    public static EditorChangeKind Classify(EditorDocument previous, EditorDocument next,
        EditorPresentationState previousPresentation, EditorPresentationState nextPresentation)
    {
        if (!BeamModelMechanicalComparer.AreEquivalent(previous.ToBeamModel(), next.ToBeamModel()))
            return EditorChangeKind.Mechanical;
        // Selected Core objects retain their profile/template metadata. Their analysis equivalence
        // is defined only above; desktop records also retain IDs, names and creation order.
        return ProjectState.DocumentContentEquals(previous, next) && previousPresentation.ContentEquals(nextPresentation)
                ? EditorChangeKind.None : EditorChangeKind.MetadataOnly;
    }
}
