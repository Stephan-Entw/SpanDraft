using SpanDraft.Core.Sections;
using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop.Sections;

public sealed record SectionDefinitionResult(ISectionDefinition Section, SectionAxisDesignation BendingAxis, string DisplayName);

/// <summary>Library access only; the definition editor does not own a project or persistence.</summary>
public interface ISectionDefinitionLibrary
{
    bool CanSave { get; }
    string? Error { get; }
    bool ContainsName(string name);
    Task<string?> SaveAsync(SectionLibraryEntry entry);
}

internal sealed class SectionDefinitionLibrary(MainWindowViewModel main) : ISectionDefinitionLibrary
{
    public bool CanSave => main.CanSaveSections && !main.IsBusy;
    public string? Error => main.SectionLibraryError;
    public bool ContainsName(string name) => main.UserSections.Find(name) is not null;
    public Task<string?> SaveAsync(SectionLibraryEntry entry) => main.SaveSectionAsync(entry);
}
