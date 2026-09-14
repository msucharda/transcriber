using System.Text.Json;

namespace TinyTranscriber.Tests;

public sealed class DictationPreferencesTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("tiny-transcriber-preferences-tests-");
    private string FilePath => Path.Combine(directory.FullName, "nested", "preferences.json");

    [Fact]
    public void MissingPreferencesUseSameLineAndToggleWithoutWritingFiles()
    {
        Assert.Equal(DictationPreferences.Default, new DictationPreferencesStore(FilePath).Load());
        Assert.Equal(" ", DictationPreferences.Default.SeparatorText);
        Assert.Equal(RecordingMode.Toggle, DictationPreferences.Default.RecordingMode);
        Assert.Empty(directory.GetFileSystemInfos());
    }

    [Theory]
    [InlineData(0, " ")]
    [InlineData(1, "\r\n")]
    [InlineData(2, "\r\n\r\n")]
    public void SavedChoicesRoundTripAcrossStoreInstances(int choice, string separator)
    {
        var preferences = new DictationPreferences((DictationSeparator)choice, RecordingMode.PushToTalk);
        new DictationPreferencesStore(FilePath).Save(preferences);
        Assert.Equal(preferences, new DictationPreferencesStore(FilePath).Load());
        Assert.Equal(separator, preferences.SeparatorText);
        var json = File.ReadAllText(FilePath);
        Assert.DoesNotContain("SeparatorText", json);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(2, document.RootElement.EnumerateObject().Count());
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(FilePath)!));
        new DictationPreferencesStore(FilePath).Save(DictationPreferences.Default);
        Assert.Equal(DictationPreferences.Default, new DictationPreferencesStore(FilePath).Load());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{")]
    [InlineData("{\"Separator\":\"Unknown\"}")]
    [InlineData("{\"Separator\":123}")]
    [InlineData("{\"Separator\":\"Space\",\"RecordingMode\":\"Unknown\"}")]
    [InlineData("{\"Separator\":\"Space\",\"Unknown\":\"value\"}")]
    public void InvalidStoredPreferencesRaiseAnExplicitError(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, json);
        Assert.Throws<JsonException>(() => new DictationPreferencesStore(FilePath).Load());
        Assert.Equal(json, File.ReadAllText(FilePath));
    }

    [Fact]
    public void OlderSeparatorOnlyFileKeepsItsChoiceAndDefaultsToToggle()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, """{"Separator":"Paragraph"}""");
        Assert.Equal(new DictationPreferences(DictationSeparator.Paragraph),
            new DictationPreferencesStore(FilePath).Load());
    }

    [Fact]
    public void InvalidSelectionCannotOverwritePreviousSettings()
    {
        var store = new DictationPreferencesStore(FilePath);
        store.Save(DictationPreferences.Default);
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Save(new((DictationSeparator)123)));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Save(new(DictationSeparator.Space, (RecordingMode)123)));
        Assert.Equal(DictationPreferences.Default, store.Load());
    }

    [Fact]
    public void FailedAtomicReplacementCleansOnlyItsTemporaryFile()
    {
        Directory.CreateDirectory(FilePath);
        var untouched = Path.Combine(FilePath, "keep.txt");
        File.WriteAllText(untouched, "keep");
        var failure = Record.Exception(() => new DictationPreferencesStore(FilePath).Save(DictationPreferences.Default));
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.Equal("keep", File.ReadAllText(untouched));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(FilePath)!));
    }

    public void Dispose() => directory.Delete(recursive: true);
}
