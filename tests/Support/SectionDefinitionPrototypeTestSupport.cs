using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Tests;

public abstract class SectionDefinitionPrototypeTestSupport
{
    protected static SectionDefinitionInput Input(SectionDefinitionViewModel vm,string key) => vm.Parameters.Single(p=>p.Key==key);
    protected static void Commit(SectionDefinitionViewModel vm,string key,string value)
    { var input=Input(vm,key); input.PendingText=value; input.Commit(); }
    protected static void Fill(SectionDefinitionViewModel vm, bool selectAxis = true)
    {
        foreach (var input in vm.Parameters.ToArray())
            Commit(vm,input.Key,input.Key switch
            { "b"=>"120", "h"=>"200", "d" or "D"=>"80", "t" or "tw"=>"5", "tf"=>"12", "r"=>"8", "A"=>"7200", _=>input.Key.StartsWith('I')?"8640000":"144000" });
        if (selectAxis && vm.RequiresAxisSelection) vm.SelectedAxis = vm.AvailableAxes[0].Value;
    }
}
