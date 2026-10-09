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
        // Core definitions retain their complete persistent inputs. Their analysis equivalence
        // is defined only above; desktop records also retain IDs, names and creation order.
        return ProjectState.DocumentContentEquals(previous, next) && previousPresentation.ContentEquals(nextPresentation)
                ? EditorChangeKind.None : EditorChangeKind.MetadataOnly;
    }
}
