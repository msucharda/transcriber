namespace TinyTranscriber.Tests;

public sealed class PreferencesStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"transcriber-preferences-{Guid.NewGuid():N}");
    private string FilePath => Path.Combine(directory, "preferences.json");

    [Fact]
    public void NewUserDefaultsToPolishing()
    {
        Assert.True(new PreferencesStore(FilePath).Load().PolishDictation);
    }

    [Fact]
    public void PreferenceSurvivesStoreRecreationAndReplacement()
    {
        var store = new PreferencesStore(FilePath);
        store.Save(new UserPreferences(false));
        Assert.False(new PreferencesStore(FilePath).Load().PolishDictation);
        store.Save(new UserPreferences(true));
        Assert.True(new PreferencesStore(FilePath).Load().PolishDictation);
        Assert.Single(Directory.GetFiles(directory));
        Assert.DoesNotContain("transcript", File.ReadAllText(FilePath), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"PolishDictation":"false"}""")]
    public void InvalidPreferencesAreNotSilentlyReset(string json)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, json);
        var exception = Assert.Throws<InvalidDataException>(() => new PreferencesStore(FilePath).Load());
        Assert.Contains(FilePath, exception.Message);
    }

    [Fact]
    public void FailedSavePreservesPreviousPreferenceAndRemovesTemporaryFile()
    {
        var store = new PreferencesStore(FilePath);
        store.Save(new UserPreferences(false));
        using (File.Open(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var exception = Record.Exception(() => store.Save(new UserPreferences(true)));
            Assert.True(exception is IOException or UnauthorizedAccessException);
        }

        Assert.False(store.Load().PolishDictation);
        Assert.Single(Directory.GetFiles(directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            File.Delete(FilePath);
            Directory.Delete(directory);
        }
    }
}
