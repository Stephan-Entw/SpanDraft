using System.Text.Json;
using SpanDraft.MicrosoftStore;

try
{
    object result = args switch
    {
        ["validate-source", var root, var version, var mode] => PackageRules.ValidateSource(root, version, mode),
        ["validate-source", var root, var version, var mode, var head] => PackageRules.ValidateSource(root, version, mode, head),
        ["stage", var root, var publish, var staging, var version] => PackageRules.Stage(root, publish, staging, version),
        ["verify", var package] => PackageRules.Verify(package),
        ["verify", var package, var version, var staging] => PackageRules.Verify(package, version, staging),
        _ => throw new ArgumentException("Usage: validate-source <repo> <effective-version> <Submission|Development> [expected-head] | stage <repo> <publish> <staging> <version> | verify <msix> [<version> <staging>]")
    };
    Console.WriteLine(JsonSerializer.Serialize(result));
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Microsoft Store packaging: {error.Message}");
    return 1;
}
