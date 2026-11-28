using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Xunit;

namespace SpanDraft.Tests;

/// <summary>
/// The generated example projects under help/assets: reproducible from their definitions, and every kind of drift is
/// reported by the check, which never writes.
/// </summary>
public sealed class HelpExampleAssetTests
{
    private static string Assets([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../../help/assets"));

    private static string CopyOfCommittedExamples()
    {
        string root = Path.Combine(Path.GetTempPath(), "spandraft-help-examples-" + Guid.NewGuid().ToString("N"));
        foreach (string language in HelpExamples.Languages)
        {
            string target = Path.Combine(root, language, "examples");
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(Path.Combine(Assets(), language, "examples")))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }
        return root;
    }

    private static Dictionary<string, string> Hashes(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(f => Path.GetRelativePath(root, f), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

    [Fact]
    public void TheCommittedExampleFilesAreCurrent() => Assert.Empty(HelpExamples.Check(Assets()));

    [Fact]
    public void GeneratingTwiceGivesTheCommittedBytes()
    {
        string committed = CopyOfCommittedExamples(), first = committed + "-1", second = committed + "-2";
        try
        {
            HelpExamples.Generate(first);
            HelpExamples.Generate(second);
            var expected = Hashes(committed);
            Assert.Equal(6, expected.Count);
            Assert.Equal(expected, Hashes(first));
            Assert.Equal(expected, Hashes(second));
        }
        finally
        {
            foreach (string root in new[] { committed, first, second })
                if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("missing", "Missing example file")]
    [InlineData("damaged", "Damaged example file")]
    [InlineData("stale", "does not match its definition")]
    [InlineData("unexpected", "Unexpected example file")]
    public void TheCheckReportsDriftWithoutChangingAnyFile(string drift, string message)
    {
        string root = CopyOfCommittedExamples();
        try
        {
            string beam = Path.Combine(root, "de", "examples", "einfeldtraeger.spandraft");
            switch (drift)
            {
                case "missing": File.Delete(beam); break;
                case "damaged": File.WriteAllText(beam, "{ \"format\": \"SpanDraft.Project\""); break;
                // Still a valid project, but no longer what the definition produces.
                case "stale": File.WriteAllText(beam, File.ReadAllText(beam).Replace("-1000", "-1500", StringComparison.Ordinal)); break;
                case "unexpected": File.Copy(beam, Path.Combine(root, "en", "examples", "old-example.spandraft")); break;
            }
            var before = Hashes(root);
            var problem = Assert.Single(HelpExamples.Check(root));
            Assert.Contains(message, problem);
            Assert.Equal(before, Hashes(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
