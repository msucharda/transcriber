using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace TinyTranscriber.Tests;

public sealed class SettingsFormTests
{
    [Fact]
    public void SavedSelectionsReachThePersistenceCallback()
    {
        StatusFormTests.RunSta(() =>
        {
            DictationPreferences? saved = null;
            using var form = Create(preferences => saved = preferences);
            Find<ComboBox>(form, "RecordingMode").SelectedIndex = 1;
            Find<ComboBox>(form, "Separator").SelectedIndex = 2;
            Assert.True(form.SaveChanges());
            Assert.Equal(new DictationPreferences(DictationSeparator.Paragraph, RecordingMode.PushToTalk), saved);
        });
    }

    [Fact]
    public void CancelDoesNotPersistEdits()
    {
        StatusFormTests.RunSta(() =>
        {
            var saves = 0;
            using var form = Create(_ => saves++);
            Find<ComboBox>(form, "RecordingMode").SelectedIndex = 1;
            form.Close();
            Assert.Equal(0, saves);
        });
    }

    [Fact]
    public void FailedSaveKeepsEditsAvailableAndShowsAnError()
    {
        StatusFormTests.RunSta(() =>
        {
            using var form = Create(_ => throw new IOException("Settings file is locked."));
            Find<ComboBox>(form, "RecordingMode").SelectedIndex = 1;
            Assert.False(form.SaveChanges());
            Assert.False(form.IsDisposed);
            Assert.Equal(RecordingMode.PushToTalk, form.SelectedPreferences.RecordingMode);
            Assert.Contains("Settings file is locked.", Find<Label>(form, "SaveError").Text);
        });
    }

    [Fact]
    public void RecordingPreventsMidGestureModeChangeButSavingWorksAfterStop()
    {
        StatusFormTests.RunSta(() =>
        {
            var saves = 0;
            using var form = Create(_ => saves++);
            form.SetRecordingBusy(true);
            Assert.False(form.SaveChanges());
            Assert.Equal(0, saves);
            form.SetRecordingBusy(false);
            Assert.True(form.SaveChanges());
            Assert.Equal(1, saves);
        });
    }

    [Fact]
    public void SettingsWindowIsNeverAnAutomaticPasteTarget()
    {
        StatusFormTests.RunSta(() =>
        {
            using var form = Create(_ => { });
            Assert.False(NativeInput.IsExternalWindow(form.Handle));
            Assert.False(form.Visible);
        });
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void NativeSettingsRenderOffscreenWithKeyboardAccessibleControls(float scale)
    {
        StatusFormTests.RunSta(() =>
        {
            var systemFont = SystemFonts.MessageBoxFont
                ?? throw new InvalidOperationException("Windows message-box font is unavailable.");
            using var font = new Font(systemFont.FontFamily, systemFont.Size * scale);
            using var form = Create(_ => { });
            form.Scale(new SizeF(scale, scale));
            form.Font = font;
            form.PerformLayout();
            form.Size = form.GetPreferredSize(Size.Empty);
            var mode = Find<ComboBox>(form, "RecordingMode");
            var spacing = Find<ComboBox>(form, "Separator");
            Assert.Equal(ComboBoxStyle.DropDownList, mode.DropDownStyle);
            Assert.Equal("Recording mode", mode.AccessibleName);
            Assert.Equal(3, spacing.Items.Count);
            Assert.NotNull(form.AcceptButton);
            Assert.NotNull(form.CancelButton);
            Assert.True(mode.Width >= 300 * scale);
            Assert.True(spacing.Width >= 300 * scale);
            using var image = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            var border = (form.Width - form.ClientSize.Width) / 2;
            using (var graphics = Graphics.FromImage(image))
            {
                RenderChildren(form, graphics, new Point(border, form.Height - form.ClientSize.Height - border));
            }

            Assert.Equal(0, mode.SelectedIndex);
            Assert.Equal("Press to start / press to stop", mode.Text);
            Assert.Equal(0, spacing.SelectedIndex);
            Assert.Equal("Continue on same line (space)", spacing.Text);
            Assert.True(Enumerable.Range(border + 20, form.ClientSize.Width - 40).Any(x =>
                Enumerable.Range(40, Math.Min(80, image.Height - 40)).Any(y => image.GetPixel(x, y).R < 100)),
                "The preview must include rendered settings content, not just an empty window.");
            Assert.False(form.Visible);
            var directory = Environment.GetEnvironmentVariable("TINY_TRANSCRIBER_TEST_RENDER_DIRECTORY");
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
                image.Save(Path.Combine(directory, $"settings-{scale:0.0}.png"), ImageFormat.Png);
            }
        });
    }

    [Fact]
    public void PushToTalkStatusExplainsReleaseInsteadOfSecondPress()
    {
        var status = new ParagraphQueueStatus(MicrophoneState.Recording, 1, 2, false, false, false, false, false);
        var presentation = DictationPresentation.From(status, "Ctrl+Shift+Space", mode: RecordingMode.PushToTalk);
        Assert.Equal("Release Space to transcribe", presentation.Detail);
    }

    private static SettingsForm Create(Action<DictationPreferences> save) =>
        new(DictationPreferences.Default, HotkeyDefinition.Parse("Ctrl+Shift+Space"), save);

    private static void RenderChildren(Control parent, Graphics graphics, Point origin)
    {
        foreach (Control child in parent.Controls)
        {
            if (child.Name is "BusyNotice" or "SaveError")
            {
                continue;
            }

            using var image = new Bitmap(child.Width, child.Height);
            child.DrawToBitmap(image, child.ClientRectangle);
            var position = new Point(origin.X + child.Left, origin.Y + child.Top);
            graphics.DrawImageUnscaled(image, position);
            if (child is ComboBox combo)
            {
                // Native WM_PRINT omits the selected item for a hidden dropdown.
                TextRenderer.DrawText(graphics, combo.Text, combo.Font,
                    new Rectangle(position.X + 3, position.Y + 1, combo.Width - 24, combo.Height - 2),
                    combo.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            RenderChildren(child, graphics, position);
        }
    }

    private static T Find<T>(Control parent, string name) where T : Control =>
        Assert.IsType<T>(Assert.Single(parent.Controls.Find(name, searchAllChildren: true)));
}
