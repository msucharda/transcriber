using System.Drawing;

namespace TinyTranscriber.Tests;

public sealed class TrayIconsTests
{
    [Theory]
    [InlineData("TinyTranscriber")]
    [InlineData("Recording")]
    [InlineData("Transcribing")]
    public void BundledIconsContainReadableFramesForTrayAndExplorer(string name)
    {
        using var stream = typeof(TrayIcons).Assembly.GetManifestResourceStream(
            $"TinyTranscriber.Assets.{name}.ico");
        Assert.NotNull(stream);
        using var reader = new BinaryReader(stream);
        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        int[] sizes = [16, 20, 24, 32, 48, 64, 256];
        Assert.Equal(sizes.Length, reader.ReadUInt16());

        foreach (var size in sizes)
        {
            var dimension = size == 256 ? 0 : size;
            Assert.Equal(dimension, reader.ReadByte());
            Assert.Equal(dimension, reader.ReadByte());
            reader.ReadUInt16();
            Assert.Equal(1, reader.ReadUInt16());
            Assert.Equal(32, reader.ReadUInt16());
            var length = reader.ReadUInt32();
            var offset = reader.ReadUInt32();
            Assert.InRange(offset + length, 1L, stream.Length);
            var nextEntry = stream.Position;
            stream.Position = offset;
            using var frame = new MemoryStream(reader.ReadBytes(checked((int)length)));
            using var bitmap = new Bitmap(frame);
            Assert.Equal(new Size(size, size), bitmap.Size);
            Assert.Equal(0, bitmap.GetPixel(0, 0).A);
            Assert.Equal(255, bitmap.GetPixel(size / 2, size / 2).A);
            stream.Position = nextEntry;
        }

        // GDI+ Icon selection skips the 256px PNG frame; Explorer reads it directly.
        foreach (var size in sizes.Where(size => size < 256))
        {
            stream.Position = 0;
            using var icon = new Icon(stream, size, size);
            using var bitmap = icon.ToBitmap();
            Assert.Equal(new Size(size, size), bitmap.Size);
            Assert.Equal(0, bitmap.GetPixel(0, 0).A);
            Assert.Equal(255, bitmap.GetPixel(size / 2, size / 2).A);
        }
    }

    [Fact]
    public void CachedIconsRemainUsableAfterTheirResourceStreamsClose()
    {
        using var icons = new TrayIcons();
        using var idle = icons.Idle.ToBitmap();
        using var recording = icons.Recording.ToBitmap();
        using var transcribing = icons.Transcribing.ToBitmap();
        var x = idle.Width / 2;
        var y = idle.Height / 2;
        Assert.NotEqual(idle.GetPixel(x, y), recording.GetPixel(x, y));
        Assert.NotEqual(recording.GetPixel(x, y), transcribing.GetPixel(x, y));
        Assert.NotEqual(nint.Zero, icons.Idle.Handle);
        Assert.NotEqual(nint.Zero, icons.Recording.Handle);
        Assert.NotEqual(nint.Zero, icons.Transcribing.Handle);
    }
}
