using SpanDraft.Desktop.Libraries;
using SpanDraft.Desktop.Sections;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Tests;

public abstract class SectionDefinitionRedesignTestSupport
{
    protected sealed class Library : ISectionDefinitionLibrary
    {
        public UserSectionLibrary Source { get; } = new();
        public IReadOnlyList<SectionLibraryEntry> Entries => Source.All;
        public bool CanSave { get; set; } = true;
        public string? Error { get; set; }
        public string? SaveError { get; set; }
        public string? DeleteError { get; set; }
        public bool CancelDelete { get; set; }
        public int DeleteCalls { get; private set; }
        public int SaveCalls { get; private set; }
        public TaskCompletionSource<string?>? SaveCompletion { get; set; }
        public TaskCompletionSource<string?>? DeleteCompletion { get; set; }
        public bool ContainsName(string name) => Source.Find(name) is not null;
        public async Task<string?> SaveAsync(SectionLibraryEntry entry)
        {
            SaveCalls++;
            string? error = SaveCompletion is null ? SaveError : await SaveCompletion.Task;
            if (error is null) Source.Add(entry);
            return error;
        }
        public async Task<string?> DeleteAsync(string name)
        {
            DeleteCalls++;
            string? error = DeleteCompletion is null ? DeleteError : await DeleteCompletion.Task;
            if (error is null && !CancelDelete) Source.Remove(name);
            return error;
        }
    }
    protected static void Fill(SectionDefinitionViewModel vm)
    {
        foreach (var input in vm.Parameters)
        {
            input.PendingText = input.Key switch
            { "h" or "b" or "d" or "D" => "100", "tw" or "tf" or "t" => "10", "r" => "5", "A" => "1000", _ => input.Key.StartsWith('I') ? "1000000" : "20000" };
            input.Commit();
        }
    }
}
