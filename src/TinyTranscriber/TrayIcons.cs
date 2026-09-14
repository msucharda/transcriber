namespace TinyTranscriber;

internal sealed class TrayIcons : IDisposable
{
    public Icon Idle { get; } = Load("TinyTranscriber");
    public Icon Recording { get; } = Load("Recording");
    public Icon Transcribing { get; } = Load("Transcribing");

    public void Dispose()
    {
        Idle.Dispose();
        Recording.Dispose();
        Transcribing.Dispose();
    }

    private static Icon Load(string name)
    {
        using var stream = typeof(TrayIcons).Assembly.GetManifestResourceStream(
            $"TinyTranscriber.Assets.{name}.ico")
            ?? throw new InvalidOperationException($"The bundled {name} icon is missing.");
        using var icon = new Icon(stream, SystemInformation.SmallIconSize);
        return (Icon)icon.Clone();
    }
}
