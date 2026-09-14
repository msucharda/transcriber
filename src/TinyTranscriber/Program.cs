namespace TinyTranscriber;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        try
        {
            using var context = new DictationApplicationContext();
            using var consoleLifetime = ConsoleLifetime.AttachToParent(context.RequestExit);
            Application.Run(context);
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