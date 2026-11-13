using System.Text;
using System.Text.Json.Nodes;
using SpanDraft.Desktop;
using SpanDraft.Desktop.Persistence;
using SpanDraft.Desktop.Presentation;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.ViewModels;
using Xunit;
using static SpanDraft.Tests.ProjectTestSupport;

namespace SpanDraft.Tests;

public sealed class DesktopSettingsTests
{
    [Fact]
    public void LocaleDefaultsAndContentComparisonKeepAllThreePreferencesIndependent()
    {
        var settings = UserSettings.Default;
        Assert.Equal(UiLanguagePreference.System, settings.UiLanguage);
        Assert.Equal(RegionalFormatPreference.System, settings.RegionalFormat);
        Assert.False(settings.ContentEquals(settings with { UiLanguage = UiLanguagePreference.English }));
        Assert.False(settings.ContentEquals(settings with { RegionalFormat = RegionalFormatPreference.GermanGermany }));
        var localized = settings with { UiLanguage = UiLanguagePreference.English, RegionalFormat = RegionalFormatPreference.GermanGermany };
        Assert.Same(settings.Profile, localized.Profile);
        Assert.Equal(localized.UiLanguage, localized.Select(UnitProfileKind.UnitedStates).UiLanguage);
        Assert.Equal(localized.RegionalFormat, localized.WithUnit(QuantityKind.BeamLength, UnitCatalog.Foot).RegionalFormat);
    }

    public static IEnumerable<object[]> LocaleCombinations() =>
        from language in Enum.GetValues<UiLanguagePreference>()
        from region in Enum.GetValues<RegionalFormatPreference>()
        select new object[] { language, region };

    [Theory]
    [MemberData(nameof(LocaleCombinations))]
    public void CurrentVersionRoundtripPreservesLocalePreferencesAndUnits(UiLanguagePreference language, RegionalFormatPreference region)
    {
        var settings = UserSettings.Default.WithUnit(QuantityKind.BeamLength, UnitCatalog.Inch)
            .Select(UnitProfileKind.StructuralEngineering) with { UiLanguage = language, RegionalFormat = region, SignificantDigits = 5 };
        var bytes = SettingsCodec.Serialize(settings);
        var json = JsonNode.Parse(bytes)!;
        Assert.Equal(4, json["version"]!.GetValue<int>());
        Assert.Equal(language.ToString(), json["uiLanguage"]!.GetValue<string>());
        Assert.Equal(region.ToString(), json["regionalFormat"]!.GetValue<string>());
        Assert.True(settings.ContentEquals(SettingsCodec.Deserialize(bytes)));
    }

    [Theory]
    [InlineData(UnitProfileKind.MechanicalEngineering)]
    [InlineData(UnitProfileKind.StructuralEngineering)]
    [InlineData(UnitProfileKind.UnitedStates)]
    [InlineData(UnitProfileKind.Custom)]
    public async Task VersionOnePreservesEveryExistingPreferenceWithoutRewritingTheFile(UnitProfileKind selected)
    {
        var settings = UserSettings.Default.WithUnit(QuantityKind.BeamLength, UnitCatalog.Inch)
            .WithUnit(QuantityKind.Moment, UnitCatalog.KilonewtonMeter).Select(selected) with { SignificantDigits = 5 };
        var json = JsonNode.Parse(SettingsCodec.Serialize(settings))!.AsObject();
        json["version"] = 1; json.Remove("uiLanguage"); json.Remove("regionalFormat");
        var files = new Files(); var store = new LocalSettingsStore(files, "settings");
        var original = Encoding.UTF8.GetBytes(json.ToJsonString()); files.Data["settings"] = original;
        var loaded = await store.LoadAsync();
        Assert.True((settings with { SignificantDigits = 3 }).ContentEquals(loaded));
        Assert.Equal(settings.Profile.Kind, loaded.Profile.Kind);
        Assert.Equal(settings.LastStandardProfile, loaded.LastStandardProfile);
        Assert.True(UserSettings.SameUnits(settings.CustomProfile!, loaded.CustomProfile!));
        Assert.Equal(UiLanguagePreference.System, loaded.UiLanguage);
        Assert.Equal(RegionalFormatPreference.System, loaded.RegionalFormat);
        Assert.Same(original, files.Data["settings"]); Assert.Empty(files.Writes);
    }

    [Fact]
    public void OriginalVersionOneDefaultFileStillLoads()
    {
        var bytes = Encoding.UTF8.GetBytes("""
            {"version":1,"selectedProfile":"MechanicalEngineering","lastStandardProfile":"MechanicalEngineering",
             "hasCustomProfile":false,"presentationMode":"Standard"}
            """);
        Assert.True(UserSettings.Default.ContentEquals(SettingsCodec.Deserialize(bytes)));
    }

    [Theory]
    [InlineData("uiLanguage", "\"Unknown\"")]
    [InlineData("regionalFormat", "\"Unknown\"")]
    [InlineData("uiLanguage", "123")]
    [InlineData("regionalFormat", "0")]
    [InlineData("uiLanguage", "null")]
    [InlineData("regionalFormat", "null")]
    [InlineData("uiLanguage", null)]
    [InlineData("regionalFormat", null)]
    public async Task InvalidCurrentLocalesFallBackAsAWhole(string field, string? value)
    {
        var json = JsonNode.Parse(SettingsCodec.Serialize(UserSettings.Default.Select(UnitProfileKind.UnitedStates)))!.AsObject();
        if (value is null) json.Remove(field); else json[field] = JsonNode.Parse(value);
        var files = new Files(); files.Data["settings"] = Encoding.UTF8.GetBytes(json.ToJsonString());
        Assert.Same(UserSettings.Default, await new LocalSettingsStore(files, "settings").LoadAsync());
    }

    [Theory]
    [InlineData(5, 5)] [InlineData(6, 5)] [InlineData(7, 5)] [InlineData(15, 5)] [InlineData(16, 5)]
    [InlineData(1, 1)] [InlineData(3, 3)]
    public async Task StoredDigitsAboveTheRangeLoadAsTheMaximumWithoutLosingOtherPreferences(int stored, int loaded)
    {
        // Earlier versions offered up to 15 digits; the key and format version stay unchanged.
        var saved = UserSettings.Default.Select(UnitProfileKind.UnitedStates) with { Notation = EngineeringNotation.Off, LastAuthor = "A" };
        var json = JsonNode.Parse(SettingsCodec.Serialize(saved))!;
        json["significantDigits"] = stored;
        var files = new Files(); files.Data["settings"] = Encoding.UTF8.GetBytes(json.ToJsonString());
        var settings = await new LocalSettingsStore(files, "settings").LoadAsync();
        Assert.Equal(loaded, settings.SignificantDigits);
        Assert.True((saved with { SignificantDigits = loaded }).ContentEquals(settings));
        Assert.Equal(json["version"]!.GetValue<int>(), JsonNode.Parse(SettingsCodec.Serialize(settings))!["version"]!.GetValue<int>());
        Assert.Equal(loaded, JsonNode.Parse(SettingsCodec.Serialize(settings))!["significantDigits"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("de-DE", UnitProfileKind.MechanicalEngineering, 3, true,
        "1234,5678 mm", "≈ 1\u202f235 mm", "2,4583 mm", "2,46 mm", "0,00042 mm", "420\u00a0·\u00a010⁻⁶ mm")]
    [InlineData("de-DE", UnitProfileKind.MechanicalEngineering, 5, false,
        "1234,5678 mm", "≈ 1\u202f234,6 mm", "2,4583 mm", "2,4583 mm", "0,00042 mm", "0,00042 mm")]
    [InlineData("en-US", UnitProfileKind.UnitedStates, 1, true,
        "1234.5678 ft", "≈ 1\u202f235 ft", "2.4583 in", "2 in", "0.00042 in", "400\u00a0·\u00a010⁻⁶ in")]
    [InlineData("en-US", UnitProfileKind.StructuralEngineering, 3, true,
        "1234.5678 m", "≈ 1\u202f235 m", "2.4583 mm", "2.46 mm", "0.00042 mm", "420\u00a0·\u00a010⁻⁶ mm")]
    public void PreviewShowsAGivenAndTwoCalculatedValuesWithThePendingSettings(string culture, UnitProfileKind profile, int digits,
        bool powersOfTen, string inputExact, string input, string resultExact, string result, string smallExact, string small)
    {
        using var scope = new ResultCultureScope(culture, culture);
        var model = new SettingsViewModel(UserSettings.Default, _ => Task.FromResult<string?>(null));
        var notified = new List<string?>();
        model.PropertyChanged += (_, e) => notified.Add(e.PropertyName);
        model.Select(profile); model.SignificantDigits = digits; model.UsesPowersOfTen = powersOfTen;
        Assert.Equal(powersOfTen ? EngineeringNotation.Automatic : EngineeringNotation.Off, model.Pending.Notation);
        Assert.Equal(new[] { (Strings.NumberPreviewInput, inputExact, input), (Strings.NumberPreviewResult, resultExact, result),
            (Strings.NumberPreviewSmall, smallExact, small) }, model.PreviewRows.Select(r => (r.Label, r.Exact, r.Displayed)));
        // The given example has more digits than any setting shows, so it is always marked; calculated values never are.
        Assert.StartsWith("≈ ", model.PreviewRows[0].Displayed);
        Assert.DoesNotContain(model.PreviewRows.Skip(1), r => r.Displayed.Contains('≈'));
        model.DiscardPending();
        Assert.True(model.UsesPowersOfTen);
        // Any change of units, digits or notation refreshes the preview and the check box.
        notified.Clear(); model.UsesPowersOfTen = false;
        Assert.Contains(nameof(SettingsViewModel.PreviewRows), notified);
        Assert.Contains(nameof(SettingsViewModel.UsesPowersOfTen), notified);
        notified.Clear(); model.ChangeUnit(QuantityKind.TransverseDisplacement, UnitCatalog.Inch);
        Assert.Contains(nameof(SettingsViewModel.PreviewRows), notified);
        Assert.EndsWith(" in", model.PreviewRows[1].Displayed);
    }

    [Theory]
    [InlineData("de-DE", "Unter 0,001 und ab 1\u202f000\u202f000, z. B. 1,23\u00a0·\u00a010⁶ N", "3 (Standard)")]
    [InlineData("en-US", "Below 0.001 and from 1\u202f000\u202f000, e.g. 1.23\u00a0·\u00a010⁶ N", "3 (default)")]
    public void DigitChoicesAreCountsAndThePowersOfTenHintStatesTheThresholds(string culture, string hint, string defaultChoice)
    {
        using var scope = new ResultCultureScope(culture, culture);
        var model = new SettingsViewModel(UserSettings.Default, _ => Task.FromResult<string?>(null));
        Assert.Equal(hint, model.PowersOfTenHint);
        Assert.Equal(new[] { "1", "2", defaultChoice, "4", "5" }, model.SignificantDigitChoices.Select(c => c.Label));
        // Running text such as the report keeps the full phrase.
        Assert.Equal(culture == "de-DE" ? "3 signifikante Stellen" : "3 significant digits", SettingsViewModel.SignificantDigitLabel(3));
        Assert.Equal(culture == "de-DE" ? "1 signifikante Stelle" : "1 significant digit", SettingsViewModel.SignificantDigitLabel(1));
    }

    [Fact]
    public void TheDigitSettingOffersOneToFive()
    {
        Assert.Equal(1, ResultPresentationOptions.MinimumSignificantDigits);
        Assert.Equal(5, ResultPresentationOptions.MaximumSignificantDigits);
        Assert.Equal(3, UserSettings.Default.SignificantDigits);
        Assert.Throws<ArgumentOutOfRangeException>(() => UserSettings.Default with { SignificantDigits = 6 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResultPresentationOptions(significantDigits: 0));
        var model = new SettingsViewModel(UserSettings.Default, _ => Task.FromResult<string?>(null));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, model.SignificantDigitChoices.Select(c => c.Value));
    }

    [Theory]
    [InlineData(UnitProfileKind.MechanicalEngineering)]
    [InlineData(UnitProfileKind.StructuralEngineering)]
    [InlineData(UnitProfileKind.UnitedStates)]
    public void StandardSelectionUsesTheExistingDefinitions(UnitProfileKind kind)
    {
        var model = new SettingsViewModel(UserSettings.Default, _ => Task.FromResult<string?>(null));
        model.Select(kind);
        Assert.Same(UserSettings.Standard(kind), model.Pending.Profile);
        Assert.False(model.HasCustomProfile);
        Assert.Equal(7, model.LeftUnits.Count);
        Assert.Equal(9, model.RightUnits.Count);
        foreach (var row in model.LeftUnits.Concat(model.RightUnits))
            Assert.All(row.Units, unit => Assert.Equal(UnitCatalog.DimensionOf(row.Quantity), unit.Dimension));
        Assert.False(model.RightUnits.Single(r => r.Quantity == QuantityKind.Rotation).IsEditable);
    }

    [Fact]
    public void IndividualEditsRecognizeStandardsAndKeepTheLastCustomCombination()
    {
        var model = new SettingsViewModel(UserSettings.Default, _ => Task.FromResult<string?>(null));
        model.ChangeUnit(QuantityKind.BeamLength, UnitCatalog.Meter);
        Assert.True(model.IsCustom); Assert.True(model.HasCustomProfile);
        var custom = model.Pending.CustomProfile;
        Assert.Same(UnitCatalog.Newton, model.Pending.Profile[QuantityKind.TransverseForce]);
        model.Select(UnitProfileKind.UnitedStates);
        Assert.Same(custom, model.Pending.CustomProfile);
        model.Select(UnitProfileKind.Custom);
        Assert.Same(custom, model.Pending.Profile);
        model.ChangeUnit(QuantityKind.BeamLength, UnitCatalog.Millimeter);
        Assert.True(model.IsMechanical);
        Assert.Same(custom, model.Pending.CustomProfile);
        model.Select(UnitProfileKind.Custom);
        Assert.Same(custom, model.Pending.Profile);
    }

    [Fact]
    public async Task ApplyEstablishesANewBaselineAndCancelDiscardsOnlyLaterChanges()
    {
        UserSettings? published = null;
        var model = new SettingsViewModel(UserSettings.Default, settings =>
        { published = settings; return Task.FromResult<string?>(null); });
        Assert.False(model.CanApply);
        model.SignificantDigits = 5;
        model.SelectedUiLanguage = UiLanguagePreference.English;
        model.SelectedRegionalFormat = RegionalFormatPreference.GermanGermany;
        Assert.True(model.CanApply);
        Assert.True(await model.ApplyAsync());
        Assert.Equal(5, published!.SignificantDigits);
        Assert.Equal(UiLanguagePreference.English, published.UiLanguage);
        Assert.Equal(RegionalFormatPreference.GermanGermany, published.RegionalFormat);
        Assert.False(model.CanApply);
        model.Select(UnitProfileKind.StructuralEngineering);
        model.SelectedUiLanguage = UiLanguagePreference.System;
        model.SelectedRegionalFormat = RegionalFormatPreference.System;
        model.DiscardPending();
        Assert.True(model.IsMechanical); Assert.Equal(5, model.SignificantDigits);
        Assert.Equal(UiLanguagePreference.English, model.SelectedUiLanguage);
        Assert.Equal(RegionalFormatPreference.GermanGermany, model.SelectedRegionalFormat);
        Assert.False(model.HasChanges);
        model.ChangeUnit(QuantityKind.Moment, UnitCatalog.KilonewtonMeter);
        model.ChangeUnit(QuantityKind.Moment, UnitCatalog.NewtonMeter);
        // Creation of the remembered individual profile remains a real preference change.
        Assert.True(model.HasChanges);
    }

    [Fact]
    public async Task SavingLocksFurtherEditsAndPreservesPendingStateOnFailure()
    {
        var gate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new SettingsViewModel(UserSettings.Default, _ => gate.Task);
        model.Select(UnitProfileKind.UnitedStates);
        model.SelectedUiLanguage = UiLanguagePreference.English;
        model.SelectedRegionalFormat = RegionalFormatPreference.GermanGermany;
        var pending = model.Pending;
        var saving = model.ApplyAsync();
        Assert.True(model.IsSaving); Assert.False(model.CanApply);
        model.Select(UnitProfileKind.MechanicalEngineering); model.DiscardPending();
        model.SelectedUiLanguage = UiLanguagePreference.German;
        model.SelectedRegionalFormat = RegionalFormatPreference.EnglishUnitedStates;
        Assert.Same(pending, model.Pending);
        Assert.False(await model.ApplyAsync());
        gate.SetResult("Write failed");
        Assert.False(await saving);
        Assert.True(model.CanApply); Assert.True(model.HasError);
        Assert.Same(pending, model.Pending);
        model.DiscardPending();
        Assert.Same(UserSettings.Default, model.Pending);
    }

    [Theory]
    [InlineData(UnitProfileKind.MechanicalEngineering)]
    [InlineData(UnitProfileKind.StructuralEngineering)]
    [InlineData(UnitProfileKind.UnitedStates)]
    [InlineData(UnitProfileKind.Custom)]
    public async Task StoreRoundtripIncludesTheRememberedCustomProfile(UnitProfileKind selected)
    {
        var files = new Files(); var store = new LocalSettingsStore(files, "settings");
        Assert.Same(UserSettings.Default, await store.LoadAsync());
        var settings = UserSettings.Default.WithUnit(QuantityKind.BeamLength, UnitCatalog.Inch)
            .WithUnit(QuantityKind.Moment, UnitCatalog.KilonewtonMeter).Select(selected) with { SignificantDigits = 5 };
        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();
        Assert.True(settings.ContentEquals(loaded));
        Assert.True(UserSettings.SameUnits(settings.CustomProfile!, loaded.Select(UnitProfileKind.Custom).Profile));
        Assert.DoesNotContain("scale", Encoding.UTF8.GetString(files.Data["settings"]), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("broken")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("version")]
    [InlineData("unit")]
    [InlineData("dimension")]
    [InlineData("missingQuantity")]
    [InlineData("missingCustom")]
    [InlineData("profile")]
    [InlineData("lastProfile")]
    [InlineData("mode")]
    public async Task InvalidSettingsFallBackAsAWhole(string corruption)
    {
        var files = new Files(); var store = new LocalSettingsStore(files, "settings");
        var settings = UserSettings.Default.WithUnit(QuantityKind.BeamLength, UnitCatalog.Inch);
        var json = JsonNode.Parse(SettingsCodec.Serialize(settings))!;
        switch (corruption)
        {
            case "version": json["version"] = 99; break;
            case "unit": json["customUnits"]!["BeamLength"] = "unknown"; break;
            case "dimension": json["customUnits"]!["BeamLength"] = "N"; break;
            case "missingQuantity": json["customUnits"]!.AsObject().Remove("BeamLength"); break;
            case "missingCustom": json["customUnits"] = null; break;
            case "profile": json["selectedProfile"] = "Unknown"; break;
            case "lastProfile": json["lastStandardProfile"] = "Custom"; break;
            case "mode": json["notation"] = "Unknown"; break;
        }
        files.Data["settings"] = Encoding.UTF8.GetBytes(corruption switch
        { "broken" => "{", "null" => "null", "{}" => "{}", _ => json.ToJsonString() });
        Assert.Same(UserSettings.Default, await store.LoadAsync());
    }

    [Fact]
    public async Task StartupLoadsPreferencesBeforeTheFirstProjectAnalysis()
    {
        var files = new Files(); var store = new LocalSettingsStore(files, "settings");
        var expected = UserSettings.Default.Select(UnitProfileKind.UnitedStates) with { SignificantDigits = 5 };
        await store.SaveAsync(expected);
        var startup = await DesktopStartupContext.LoadAsync(files, store);
        var main = new MainWindowViewModel(files: startup.Files, settingsStore: startup.SettingsStore, settings: startup.Settings);
        main.NewCommand.Execute(null);
        Assert.True(expected.ContentEquals(main.Settings));
        ProjectTestSupport.CompleteSetup(main); main.Setup.ApplyCommand.Execute(null);
        Assert.True(UserSettings.SameUnits(expected.Profile, main.Editor!.ResultPresentation.Profile));
        Assert.Equal(5, main.Editor.ResultPresentation.SignificantDigits);
    }

    [Fact]
    public async Task FailedPublicationKeepsActivePreferencesAndThePreviousSettingsFile()
    {
        var files = new Files(); var store = new LocalSettingsStore(files, "settings");
        await store.SaveAsync(UserSettings.Default);
        var bytes = files.Data["settings"].ToArray();
        files.FailWritePath = "settings";
        var main = new MainWindowViewModel(files: files, settingsStore: store); main.NewCommand.Execute(null);
        var dialog = new SettingsViewModel(main.Settings, main.ApplySettingsAsync);
        dialog.Select(UnitProfileKind.UnitedStates);
        Assert.False(await dialog.ApplyAsync());
        Assert.Contains(Strings.SettingsSaveError, dialog.ErrorText!);
        Assert.Equal(bytes, files.Data["settings"]);
        Assert.Same(UnitProfile.Default, main.ResultPresentation.Profile);
        Assert.True(dialog.IsUnitedStates); Assert.True(dialog.CanApply);
    }

    [Fact]
    public async Task PreferencePublicationLeavesProjectHistoryAnalysisAndRecoveryUntouched()
    {
        var files = new Files(); var delay = new Delay();
        string recoveryPath = Path.Combine(Path.GetTempPath(), "phase3-recovery.json");
        var recovery = new ProjectRecovery(files, recoveryPath, delay.Wait, () => Now);
        var store = new LocalSettingsStore(files, "settings");
        int analyses = 0;
        var main = new MainWindowViewModel(b => { analyses++; return SpanDraft.Analysis.BeamAnalysis.Analyze(b); },
            files: files, recovery: recovery, settingsStore: store); main.NewCommand.Execute(null);
        ProjectTestSupport.CompleteSetup(main); main.Setup.ApplyCommand.Execute(null);
        var editor = main.Editor!; var session = editor.Session;
        session.MarkSaved(session.CurrentRevision, Path.Combine(Path.GetTempPath(), "phase3.spandraft"));
        editor.SetAnnotationOffset(Guid.NewGuid(), new(2, 3));
        session.Commit(session.CurrentRevision.State with { Document = editor.Document with { Length = M(1.2) } });
        delay.ReleaseAll(); await recovery.DrainAsync();
        var revision = session.CurrentRevision; var saved = session.SavedRevisionId;
        var undo = session.UndoHistory.ToArray(); var redo = session.RedoHistory.ToArray();
        var bytes = ProjectFileCodec.Serialize(revision.State); var recoveryBytes = files.Data[recoveryPath].ToArray();
        var result = editor.Presentation.Result; int count = analyses, changes = 0;
        session.Changed += () => changes++;
        var settings = UserSettings.Default.WithUnit(QuantityKind.BeamLength, UnitCatalog.Foot) with { SignificantDigits = 5 };
        Assert.Null(await main.ApplySettingsAsync(settings));
        Assert.Same(revision, session.CurrentRevision); Assert.Equal(saved, session.SavedRevisionId);
        Assert.Equal(undo, session.UndoHistory); Assert.Equal(redo, session.RedoHistory);
        Assert.True(session.IsDirty); Assert.Equal(bytes, ProjectFileCodec.Serialize(session.CurrentRevision.State));
        Assert.Equal(recoveryBytes, files.Data[recoveryPath]); Assert.Equal(count, analyses); Assert.Equal(0, changes);
        Assert.Same(result, editor.Presentation.Result);
        Assert.True(editor.Undo()); Assert.True(editor.Redo());
        Assert.True(UserSettings.SameUnits(settings.Profile, editor.ResultPresentation.Profile));
        delay.ReleaseAll(); await recovery.DrainAsync();
    }
    [Theory]
    [InlineData(null, null)] [InlineData("", null)] [InlineData("  ", null)]
    [InlineData(" Ada Lovelace ", "Ada Lovelace")]
    public void VersionThreeRoundtripNormalizesLastAuthor(string? value, string? expected)
    {
        var settings = UserSettings.Default with { LastAuthor = value };
        Assert.Equal(expected, settings.LastAuthor);
        var bytes = SettingsCodec.Serialize(settings);
        Assert.Equal(4, JsonNode.Parse(bytes)!["version"]!.GetValue<int>());
        Assert.Equal(expected, SettingsCodec.Deserialize(bytes).LastAuthor);
        Assert.True(settings.ContentEquals(SettingsCodec.Deserialize(bytes)));
        Assert.Equal(expected is not null, !settings.ContentEquals(UserSettings.Default));
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void HistoricalSettingsNeverSupplyLastAuthor(int version)
    {
        var json = JsonNode.Parse(SettingsCodec.Serialize(UserSettings.Default))!;
        json["version"] = version;
        // Unknown fields in historical versions must not acquire new semantics.
        json["lastAuthor"] = "Ignored"; json["defaultAuthor"] = "Also ignored";
        var loaded = SettingsCodec.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.Null(loaded.LastAuthor); Assert.True(UserSettings.Default.ContentEquals(loaded));
    }

    [Theory]
    [InlineData(false, null, "Legacy author")]
    [InlineData(true, "New author", "New author")]
    [InlineData(true, null, null)]
    [InlineData(true, "  ", null)]
    public void DevelopmentVersionThreeMigratesDefaultAuthorOnlyWhenLastAuthorIsAbsent(bool hasLastAuthor, string? value, string? expected)
    {
        var json = JsonNode.Parse(SettingsCodec.Serialize(UserSettings.Default))!.AsObject();
        json["defaultAuthor"] = " Legacy author ";
        if (hasLastAuthor) json["lastAuthor"] = value; else json.Remove("lastAuthor");
        var loaded = SettingsCodec.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.Equal(expected, loaded.LastAuthor);
        var rewritten = JsonNode.Parse(SettingsCodec.Serialize(loaded))!.AsObject();
        Assert.Equal(expected, rewritten["lastAuthor"]?.GetValue<string>());
        Assert.False(rewritten.ContainsKey("defaultAuthor"));
        Assert.Equal(4, rewritten["version"]!.GetValue<int>());
    }

    [Fact]
    public async Task VisibleSettingsTransactionPreservesTheInternalLastAuthor()
    {
        var files = new Files(); var store = new LocalSettingsStore(files, "settings");
        var main = new MainWindowViewModel(files: files, settingsStore: store,
            settings: UserSettings.Default with { LastAuthor = "Local author" });
        var model = new SettingsViewModel(main.Settings, main.ApplySettingsAsync);
        model.SignificantDigits = 5;
        Assert.True(await model.ApplyAsync());
        Assert.Equal("Local author", main.Settings.LastAuthor);
        Assert.Equal("Local author", (await store.LoadAsync()).LastAuthor);
        Assert.Null(await main.ApplySettingsAsync(main.Settings with { LastAuthor = "Stale snapshot" }));
        Assert.Equal("Local author", main.Settings.LastAuthor);
    }

    [Theory]
    [InlineData(1, "Standard")] [InlineData(1, "Detailed")]
    [InlineData(2, "Standard")] [InlineData(2, "Detailed")]
    [InlineData(3, "Standard")] [InlineData(3, "Detailed")]
    public void LegacyPrecisionMigratesWithoutLosingCustomUnitsOrChangingFiles(int version, string oldMode)
    {
        var settings = UserSettings.Default.WithUnit(QuantityKind.BeamLength, UnitCatalog.Inch)
            .WithUnit(QuantityKind.Stress, UnitCatalog.Ksi) with { SignificantDigits = 5, Notation = EngineeringNotation.Off };
        var json = JsonNode.Parse(SettingsCodec.Serialize(settings))!.AsObject();
        json["version"] = version;
        json["presentationMode"] = oldMode;
        json.Remove("significantDigits"); json.Remove("notation");
        foreach (var kind in new[] { QuantityKind.YoungsModulus, QuantityKind.MaterialStrength, QuantityKind.Density, QuantityKind.PoissonRatio })
            json["customUnits"]!.AsObject().Remove(kind.ToString());
        var loaded = SettingsCodec.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.Equal(3, loaded.SignificantDigits);
        Assert.Equal(EngineeringNotation.Automatic, loaded.Notation);
        Assert.True(UserSettings.SameUnits(settings.Profile, loaded.Profile));
        var current = JsonNode.Parse(SettingsCodec.Serialize(loaded))!.AsObject();
        Assert.False(current.ContainsKey("presentationMode"));
        Assert.Equal(4, current["version"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("significantDigits", "0")] [InlineData("significantDigits", "-1")]
    [InlineData("significantDigits", "3.5")] [InlineData("significantDigits", "null")]
    [InlineData("notation", "null")] [InlineData("notation", "42")]
    public async Task InvalidNumericPreferencesFallBackToDefaults(string field, string value)
    {
        var json = JsonNode.Parse(SettingsCodec.Serialize(UserSettings.Default.Select(UnitProfileKind.UnitedStates)))!;
        json[field] = JsonNode.Parse(value);
        var files = new Files(); files.Data["settings"] = Encoding.UTF8.GetBytes(json.ToJsonString());
        Assert.Same(UserSettings.Default, await new LocalSettingsStore(files, "settings").LoadAsync());
    }


}
