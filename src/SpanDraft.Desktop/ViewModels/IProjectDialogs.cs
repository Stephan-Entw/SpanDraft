namespace SpanDraft.Desktop.ViewModels;

public enum MaterialNameConflictDecision { Cancel, Replace, Rename }
public enum LeaveDecision { Cancel, Save, Discard }

/// <summary>Native UI boundary; session/file workflows can be tested without a window.</summary>
public interface IProjectDialogs
{
    Task<string?> PickOpenPathAsync();
    Task<string?> PickSavePathAsync(string? currentFilePath, string? calculationTitle);
    Task<LeaveDecision> ConfirmLeaveAsync(string projectFileName);
    Task<bool> ConfirmRemoveMissingRecentAsync(string path);
    /// <summary>The recovery area could not be listed at startup: true tries again, false ends the startup.</summary>
    Task<bool> ConfirmRecoveryRetryAsync(string reason);
    Task ShowErrorAsync(string message);
    Task ShowMaterialEditorAsync(MaterialEditorViewModel editor);
    Task<MaterialNameConflictDecision> ResolveMaterialNameConflictAsync(string displayName);
    Task<bool> ConfirmMaterialReplaceAsync(string displayName);
    Task<bool> ConfirmMaterialDeleteAsync(string displayName);
    Task<bool> ConfirmSectionDeleteAsync(string name);
}
