using System;
using System.IO;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Drops.Services;
using Drops.Views;

namespace Drops;

public partial class App : Application
{
    MainWindow? window;
    TrayIcon? tray;

    /// <summary>True once Quit was chosen, so closing the window really closes it.</summary>
    public static bool Quitting { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The window can be closed while Drops lives on in the tray.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            try { if (Directory.Exists(AppPaths.ViewCache)) Directory.Delete(AppPaths.ViewCache, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

            var settings = Settings.Load();
            // Not set as desktop.MainWindow: the lifetime would show it at start, even for --hidden.
            window = new MainWindow(settings);
            desktop.ShutdownRequested += (_, _) => Quitting = true;

            ShowTray(desktop);
            window.CountChanged += count =>
            {
                if (tray != null) tray.ToolTipText = count switch
                {
                    null => "Drops",
                    0 => "Drops: nothing in the drop",
                    1 => "Drops: 1 file in the drop",
                    _ => $"Drops: {count} files in the drop",
                };
            };

            Program.ShowRequested += () => Dispatcher.UIThread.Post(() => window.BringForward());

            if (Program.StartHidden) window.StartInTray();
            else window.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    void ShowTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var menu = new NativeMenu();
        menu.Add(Item("Show Drops", () => window?.BringForward()));
        menu.Add(Item("Send files…", () => window?.PickFromTray()));
        menu.Add(Item("Send clipboard", () => window?.SendClipboard()));
        menu.Add(new NativeMenuItemSeparator());
        var signOut = Item("Sign out", () => window?.SignOut());
        signOut.IsEnabled = window?.HasSession == true; // a saved session can be dropped even while the server is silent
        menu.Add(signOut);
        if (window != null) window.UserChanged += user =>
        {
            signOut.Header = string.IsNullOrEmpty(user) ? "Sign out" : "Sign out " + user;
            signOut.IsEnabled = user != null;
        };
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item("Quit", () =>
        {
            Quitting = true;
            if (tray != null) tray.IsVisible = false;
            desktop.Shutdown();
        }));

        using var icon = AssetLoader.Open(new Uri("avares://Drops/Assets/drops-icon.ico"));
        tray = new TrayIcon { Icon = new WindowIcon(icon), ToolTipText = "Drops", Menu = menu, IsVisible = true };
        tray.Clicked += (_, _) => window?.BringForward();
        TrayIcon.SetIcons(this, [tray]);
    }

    static NativeMenuItem Item(string header, Action run) => new(header) { Command = new MenuCommand(run) };

    sealed class MenuCommand(Action run) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => run();
    }
}
