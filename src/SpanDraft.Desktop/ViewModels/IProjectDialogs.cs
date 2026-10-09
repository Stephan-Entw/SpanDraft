namespace SpanDraft.Desktop.ViewModels;

public enum LeaveDecision { Cancel, Save, Discard }
public enum RecoveryDecision { Cancel, Restore, Discard }

/// <summary>Native UI boundary; session/file workflows can be tested without a window.</summary>
public interface IProjectDialogs
{
    Task<string?> PickOpenPathAsync();
    Task<string?> PickSavePathAsync(string? currentFilePath);
    Task<LeaveDecision> ConfirmLeaveAsync(string projectName);
    Task<RecoveryDecision> ConfirmRecoveryAsync(bool damaged, DateTimeOffset? writtenAtUtc);
    Task ShowErrorAsync(string message);
    Task<bool> ConfirmMaterialDeleteAsync(string name);
    Task<bool> ConfirmSectionDeleteAsync(string name);
}
