namespace TinyTranscriber;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        try
        {
            Application.Run(new DictationApplicationContext());
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Tiny Transcriber",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}