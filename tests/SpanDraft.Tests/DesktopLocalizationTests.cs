using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SpanDraft.Desktop.Resources;
using Xunit;

namespace SpanDraft.Tests;

public sealed class DesktopLocalizationTests
{
    private static Dictionary<string, string> ReadResources(string name)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpanDraft.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var entries = XDocument.Load(Path.Combine(directory.FullName, "src", "SpanDraft.Desktop", "Resources", name))
            .Root!.Elements("data").ToArray();
        Assert.DoesNotContain(entries.GroupBy(e => e.Attribute("name")!.Value), g => g.Count() > 1);
        return entries.ToDictionary(e => e.Attribute("name")!.Value, e => e.Element("value")!.Value);
    }

    [Fact]
    public void BothLanguagesHaveExactlyTheSameKeysAndStronglyTypedAccessorsWithoutDuplicates()
    {
        var english = ReadResources("Strings.resx"); var german = ReadResources("Strings.de.resx");
        Assert.NotEmpty(english);
        Assert.Equal(english.Keys.Order(), german.Keys.Order());
        var properties = typeof(Strings).GetProperties(BindingFlags.Public | BindingFlags.Static);
        Assert.Equal(english.Keys.Order(), properties.Select(p => p.Name).Order());
        Assert.All(properties, p => Assert.Equal(typeof(string), p.PropertyType));
        Assert.All(english.Values.Concat(german.Values), value => Assert.False(string.IsNullOrWhiteSpace(value)));
    }

    [Fact]
    public void CompositeFormatPlaceholdersMatchIncludingTheirNumberOfOccurrences()
    {
        var english = ReadResources("Strings.resx"); var german = ReadResources("Strings.de.resx");
        foreach (string key in english.Keys)
        {
            _ = CompositeFormat.Parse(english[key]); _ = CompositeFormat.Parse(german[key]);
            Assert.Equal(Placeholders(english[key]), Placeholders(german[key]));
        }
    }

    private static string[] Placeholders(string text) => Regex.Matches(text, @"(?<!\{)\{(\d+)(?:,[^{}:]*)?(?::[^{}]*)?\}")
        .Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal).ToArray();
}
