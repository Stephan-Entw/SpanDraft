using SpanDraft.Core.Materials;
using SpanDraft.Core.Units;
using SpanDraft.Desktop.Libraries;
using Xunit;
using static SpanDraft.Tests.LibraryTestSupport;

namespace SpanDraft.Tests;

public sealed class LibraryModelTests
{
    [Fact]
    public void MaterialCrudUsesLocalCaseInsensitiveNamesAndKeepsOrder()
    {
        var first = MaterialEntry(); var second = MaterialEntry("Fixture B");
        var library = new UserMaterialLibrary();
        Assert.Empty(library.All); Assert.Null(library.Find("absent")); Assert.False(library.Remove("absent"));
        library.Add(first); library.Add(second);
        Assert.Same(first, library.Find("fIXTURE a"));
        Assert.Throws<ArgumentException>(() => library.Add(MaterialEntry("fixture A")));
        Assert.Throws<ArgumentException>(() => library.Replace("Fixture A", MaterialEntry("fixture b")));
        Assert.Throws<KeyNotFoundException>(() => library.Replace("absent", first));
        Assert.Equal(new[] { first, second }, library.All);
        var replacement = MaterialEntry("Fixture A", 345678901);
        library.Replace("fixture a", replacement);
        Assert.Same(replacement, library.All[0]);
        var renamed = MaterialEntry("Renamed", 345678901);
        library.Replace("Fixture A", renamed);
        Assert.Null(library.Find("Fixture A")); Assert.Same(renamed, library.All[0]);
        var casing = MaterialEntry("RENAMED");
        library.Replace("renamed", casing);
        Assert.Equal(new[] { casing, second }, library.All);
        Assert.True(library.Remove("rEnAmEd")); Assert.False(library.Remove("RENAMED"));
        Assert.Same(second, Assert.Single(library.All));
        var view = Assert.IsAssignableFrom<IList<MaterialLibraryEntry>>(library.All);
        Assert.Throws<NotSupportedException>(() => view.Clear());
        Assert.Throws<NotSupportedException>(() => view[0] = first);
    }

    [Fact]
    public void SectionCrudUsesLocalCaseInsensitiveNamesAndKeepsOrder()
    {
        var first = new SectionLibraryEntry("Fixture A", ParametricSectionTestSupport.Shape(0));
        var second = new SectionLibraryEntry("Fixture B", ParametricSectionTestSupport.Shape(1));
        var library = new UserSectionLibrary();
        Assert.Empty(library.All); Assert.Null(library.Find("absent")); Assert.False(library.Remove("absent"));
        library.Add(first); library.Add(second);
        Assert.Same(first, library.Find("fIXTURE a"));
        Assert.Throws<ArgumentException>(() => library.Add(new("fixture A", first.Section)));
        Assert.Throws<ArgumentException>(() => library.Replace("Fixture A", new("fixture b", first.Section)));
        Assert.Throws<KeyNotFoundException>(() => library.Replace("absent", first));
        Assert.Equal(new[] { first, second }, library.All);
        var replacement = new SectionLibraryEntry("Fixture A", ParametricSectionTestSupport.Shape(2));
        library.Replace("fixture a", replacement);
        Assert.Same(replacement, library.All[0]);
        var renamed = new SectionLibraryEntry("Renamed", replacement.Section);
        library.Replace("Fixture A", renamed);
        Assert.Null(library.Find("Fixture A")); Assert.Same(renamed, library.All[0]);
        var casing = new SectionLibraryEntry("RENAMED", replacement.Section);
        library.Replace("renamed", casing);
        Assert.Equal(new[] { casing, second }, library.All);
        Assert.True(library.Remove("rEnAmEd")); Assert.False(library.Remove("RENAMED"));
        Assert.Same(second, Assert.Single(library.All));
        var view = Assert.IsAssignableFrom<IList<SectionLibraryEntry>>(library.All);
        Assert.Throws<NotSupportedException>(() => view.Clear());
        Assert.Throws<NotSupportedException>(() => view[0] = first);
    }

    [Fact]
    public void BuiltInCatalogIsReadOnlyAndDoesNotReserveUserNames()
    {
        var builtIn = MaterialEntry();
        var source = new[] { builtIn };
        var catalog = new BuiltInMaterialCatalog(source);
        source[0] = MaterialEntry("changed source");
        Assert.Same(builtIn, catalog.Find("FIXTURE A")); Assert.Null(catalog.Find("absent"));
        var user = MaterialEntry("fixture a", 987654321);
        var library = new UserMaterialLibrary([user]);
        Assert.Same(user, library.Find(builtIn.Material.Name));
        Assert.NotEqual(builtIn.Material.YoungsModulus, user.Material.YoungsModulus);
        var view = Assert.IsAssignableFrom<IList<MaterialLibraryEntry>>(catalog.All);
        Assert.Throws<NotSupportedException>(() => view.Add(user));
        Assert.Throws<NotSupportedException>(() => view[0] = user);
        Assert.DoesNotContain(typeof(BuiltInMaterialCatalog).GetMethods(), method =>
            method.Name is "Add" or "Replace" or "Remove");
    }

    [Fact]
    public void MetadataIsOptionalDefensivelyCopiedAndSeparateFromCore()
    {
        string[] standards = [" Standard A ", "", " ", "Standard B"];
        var entry = new MaterialLibraryEntry(MaterialEntry().Material, MaterialCategory.StainlessSteel, " 1.2345 ", standards);
        standards[0] = "Changed";
        Assert.Equal("1.2345", entry.MaterialNumber);
        Assert.Equal(new[] { "Standard A", "Standard B" }, entry.OtherStandards);
        var view = Assert.IsAssignableFrom<IList<string>>(entry.OtherStandards);
        Assert.Throws<NotSupportedException>(() => view.Add("Changed"));
        var empty = new MaterialLibraryEntry(entry.Material, MaterialCategory.Other, " ", []);
        Assert.Null(empty.MaterialNumber); Assert.Empty(empty.OtherStandards);
        Assert.Same(entry.Material, empty.Material);
        Assert.DoesNotContain(typeof(Material).GetProperties(), p => p.Name is "Category" or "MaterialNumber" or "OtherStandards");
        Assert.DoesNotContain(typeof(MaterialLibraryEntry).GetProperties(), p => p.Name is "Id" or "Name" or "Designation");
        Assert.DoesNotContain(typeof(SectionLibraryEntry).GetProperties(), p => p.Name is "Id" or "BendingAxis");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" A ")]
    public void LibraryNamesMustBeNonEmptyAndAlreadyTrimmed(string name)
    {
        Assert.Throws<ArgumentException>(() => new MaterialLibraryEntry(
            new Material(name.Length == 0 || string.IsNullOrWhiteSpace(name) ? " " : name,
                Pressure.FromPascals(1), Pressure.FromPascals(1)), MaterialCategory.Other));
        Assert.Throws<ArgumentException>(() => new SectionLibraryEntry(name, ParametricSectionTestSupport.Shape(0)));
    }

    [Fact]
    public void ConstructorsRejectDuplicateNamesNullEntriesAndUnknownCategories()
    {
        var entry = MaterialEntry();
        Assert.Throws<ArgumentException>(() => new UserMaterialLibrary([entry, MaterialEntry("fixture a")]));
        Assert.Throws<ArgumentException>(() => new BuiltInMaterialCatalog([entry, MaterialEntry("fixture a")]));
        var section = new SectionLibraryEntry("A", ParametricSectionTestSupport.Shape(0));
        Assert.Throws<ArgumentException>(() => new UserSectionLibrary([section, new("a", section.Section)]));
        Assert.Throws<ArgumentNullException>(() => new UserMaterialLibrary([null!]));
        Assert.Throws<ArgumentNullException>(() => new UserSectionLibrary([null!]));
        Assert.Throws<ArgumentNullException>(() => new MaterialLibraryEntry(null!, MaterialCategory.Other));
        Assert.Throws<ArgumentNullException>(() => new SectionLibraryEntry("A", null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MaterialLibraryEntry(entry.Material, (MaterialCategory)123));
    }
}
