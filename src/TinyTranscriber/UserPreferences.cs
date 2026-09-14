using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyTranscriber;

internal sealed record UserPreferences([property: JsonRequired] bool PolishDictation = true);

internal sealed class PreferencesStore(string path)
{
    public static PreferencesStore ForCurrentUser() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TinyTranscriber", "preferences.json"));

    public UserPreferences Load()
    {
        try
        {
            return JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(path))
                ?? throw new InvalidDataException($"Preferences are empty: {path}");
        }
        catch (FileNotFoundException)
        {
            return new UserPreferences();
        }
        catch (DirectoryNotFoundException)
        {
            return new UserPreferences();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Invalid preferences in {path}. Fix or rename this file.", exception);
        }
    }

    public void Save(UserPreferences preferences)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences));
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
