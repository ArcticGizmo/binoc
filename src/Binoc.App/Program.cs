using Avalonia;
using Velopack;

namespace Binoc.App;

internal static class Program
{
    // STA for clipboard / shell interop parity with the rest of the desktop stack.
    [STAThread]
    public static int Main(string[] args)
    {
        // Velopack must run first: on an installed build its hooks handle install/update/uninstall and then
        // hand control back. A no-op for a plain `dotnet run`, but wiring it from M0 means M7 packaging needs
        // no retrofit. (In-app "check for updates" via UpdateManager also lands at M7.)
        VelopackApp.Build().Run();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
