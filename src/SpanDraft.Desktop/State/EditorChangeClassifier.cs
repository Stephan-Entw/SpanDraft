namespace SpanDraft.Desktop.State;

public enum EditorChangeKind { None, MetadataOnly, Mechanical }

/// <summary>Every desktop commit uses this boundary; individual controls do not choose analysis behavior.</summary>
public static class EditorChangeClassifier
{
    public static EditorChangeKind Classify(ProjectState previous, ProjectState next)
    {
        if (!BeamModelMechanicalComparer.AreEquivalent(previous.Document.ToBeamModel(), next.Document.ToBeamModel()))
            return EditorChangeKind.Mechanical;
        // Core definitions retain their complete persistent inputs. Their analysis equivalence
        // is defined only above; desktop records also retain IDs, names and creation order.
        return previous.ContentEquals(next)
                ? EditorChangeKind.None : EditorChangeKind.MetadataOnly;
    }
}
