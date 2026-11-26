using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Drops.Services;

namespace Drops;

sealed class Program
{
    const string PipeName = "fezcode.drops.show";

    const string Help = """
        Drops: send a file to your other device, from the desktop and the tray.

        Usage:
          Drops                 open the window (or bring the running copy forward)
          Drops --hidden        start in the tray without opening the window

        Flags:
          -h, --help            show this help
          -v, --version         show version

        Settings and the saved session live in the fezcode folder:
          Windows   %APPDATA%\fezcode\Drops
          macOS     ~/Library/Application Support/fezcode/Drops
          Linux     ~/.config/fezcode/Drops
        Set DROPS_HOME to use another folder. Edit settings.json there to change the server.
        """;

    /// <summary>Raised (off the UI thread) when a second copy was started: show the window.</summary>
    public static event Action? ShowRequested;

    public static bool StartHidden { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        foreach (var a in args)
        {
            switch (a)
            {
                case "-v" or "--version":
                    Console.WriteLine($"Drops v{AppInfo.Version}");
                    return 0;
                case "-h" or "--help":
                    Console.WriteLine(Help);
                    return 0;
                case "--hidden":
                    StartHidden = true;
                    break;
            }
        }

        // One copy per user: a tray app started twice would upload and poll twice.
        using var only = new Mutex(true, "fezcode.drops.single", out var first);
        if (!first)
        {
            AskRunningCopyToShow();
            return 0;
        }
        _ = Task.Run(ListenForSecondCopies);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    static void AskRunningCopyToShow()
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(2000);
            pipe.WriteByte(1);
        }
        catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException)
        {
        }
    }

    static async Task ListenForSecondCopies()
    {
        while (true)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync();
                if (pipe.ReadByte() == 1) ShowRequested?.Invoke();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(1000);
            }
        }
    }

    // Avalonia configuration, don't remove; also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
