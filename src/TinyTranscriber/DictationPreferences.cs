using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyTranscriber;

internal enum DictationSeparator
{
    Space,
    NewLine,
    Paragraph
}

internal enum RecordingMode
{
    Toggle,
    PushToTalk
}

internal sealed record DictationPreferences(
    DictationSeparator Separator,
    RecordingMode RecordingMode = RecordingMode.Toggle)
{
    public static DictationPreferences Default { get; } = new(DictationSeparator.Space);

    [JsonIgnore]
    public string SeparatorText => Separator switch
    {
        DictationSeparator.Space => " ",
        DictationSeparator.NewLine => "\r\n",
        DictationSeparator.Paragraph => "\r\n\r\n",
        _ => throw new ArgumentOutOfRangeException(nameof(Separator))
    };
}

internal sealed class DictationPreferencesStore(string path)
{
    private readonly string path = Path.GetFullPath(path);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter<DictationSeparator>(allowIntegerValues: false),
            new JsonStringEnumConverter<RecordingMode>(allowIntegerValues: false)
        }
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TinyTranscriber", "preferences.json");

    public DictationPreferences Load()
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<DictationPreferences>(stream, JsonOptions)
                ?? throw new JsonException("Dictation preferences must be a JSON object.");
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return DictationPreferences.Default;
        }
    }

    public void Save(DictationPreferences preferences)
    {
        _ = preferences.SeparatorText;
        if (!Enum.IsDefined(preferences.RecordingMode))
        {
            throw new ArgumentOutOfRangeException(nameof(preferences));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write))
            {
                JsonSerializer.Serialize(stream, preferences, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
