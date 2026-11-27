using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Drops.Controls;
using Drops.Services;

namespace Drops.Views;

/// <summary>
/// The whole interface, and a port of the web page's app.js: sign in, the drop zone,
/// the upload queue, the list of files with their time gauges, and the password form.
/// </summary>
public partial class MainWindow : Window
{
    readonly Settings settings;
    readonly DropsClient client;
    readonly DispatcherTimer timer;
    readonly Queue<Job> pending = new();
    readonly Dictionary<string, (TextBlock info, Gauge gauge)> rows = new();

    string? listKey;
    string ttlText = "an hour";
    bool busy, refreshing, everListed, dragOver;
    DateTime lastRefresh = DateTime.MinValue;

    /// <summary>How many files are in the drop, or null when signed out. The tray shows it.</summary>
    public event Action<int?>? CountChanged;

    /// <summary>Who is signed in, or null. The tray's "Sign out" follows it.</summary>
    public event Action<string?>? UserChanged;

    sealed class Job
    {
        public required Func<Task<Stream>> Open;
        public required long Size;
        public required string Name;
        public required Border Card;
        public required Grid Row;
        public required TextBlock Info;
        public required Gauge Fill;
        public bool Done;
    }

    // Needed by the XAML previewer only.
    public MainWindow() : this(new Settings()) { }

    public MainWindow(Settings settings)
    {
        this.settings = settings;
        InitializeComponent();

        Width = Math.Max(MinWidth, settings.Width);
        Height = Math.Max(MinHeight, settings.Height);

        client = new DropsClient(settings.ServerUri, SessionStore.Load(settings.ServerUri));
        SetHint();
        SetupTitleBar();

        // ---- top bar
        AllApps.Click += (_, _) => OpenUrl("https://apps.fezcode.studio/");


        // ---- sign in
        SignIn.Click += async (_, _) => await SubmitLogin();

        // ---- the drop
        Pick.Click += async (_, _) => await PickFiles();
        TextToggle.IsCheckedChanged += (_, _) => TextPanel.IsVisible = TextToggle.IsChecked == true;
        PwToggle.IsCheckedChanged += (_, _) => PwForm.IsVisible = PwToggle.IsChecked == true;
        SendText.Click += (_, _) =>
        {
            var t = TextNote.Text ?? "";
            if (t.Trim().Length == 0) return;
            EnqueueText(t);
            TextNote.Text = "";
        };
        DownloadAll.Click += async (_, _) => await SaveAll();
        Clear.Click += async (_, _) =>
        {
            if (!await Confirm("Remove everything from the drop?", "Remove all")) return;
            await client.DeleteAll();
            await Refresh();
        };
        PwSubmit.Click += async (_, _) => await SubmitPassword();

        // ---- files dragged onto the window, from anywhere on it
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        KeyDown += OnKeyDown;

        // The colour fields lean gently toward the pointer.
        Zone.PointerMoved += (_, e) => Lean(ZoneFlow, Zone, e);
        Login.PointerMoved += (_, e) => Lean(LoginFlow, Login, e);

        SizeChanged += (_, _) => Fit();
        Fit();

        // Every 4 seconds while the window is open, every 30 while it sits in the tray.
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += async (_, _) =>
        {
            if (client.Token == null) return;
            if (!IsVisible && DateTime.UtcNow - lastRefresh < TimeSpan.FromSeconds(30)) return;
            await Refresh();
        };
        timer.Start();

        Opened += async (_, _) =>
        {
            // A saved session opens straight onto the drop; the first answer from the server corrects it if needed.
            // (Done here, not in the constructor: text measured before the window has its size stays clipped.)
            if (!everListed) Show(client.Token != null);
            await Refresh();
        };
    }

    // ------------------------------------------------------------------ the title bar

    /// <summary>
    /// Replaces the system title bar with Drops' own. Windows: the client area covers the whole
    /// window and the buttons are ours. macOS: the same, but the system's traffic lights stay.
    /// Linux: the window manager's title bar is switched off and its resize border kept.
    /// </summary>
    void SetupTitleBar()
    {
        if (OperatingSystem.IsMacOS())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = 40;
            CaptionButtons.IsVisible = false;
            TitleLabel.Margin = new Thickness(78, 0, 0, 0); // clear of the traffic lights
        }
        else if (OperatingSystem.IsWindows())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
            ExtendClientAreaTitleBarHeightHint = -1;
        }
        else
        {
            SystemDecorations = SystemDecorations.BorderOnly;
        }

        TitleBar.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(TitleBar).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) ToggleMaximize();
            else BeginMoveDrag(e);
        };
        MinButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaxButton.Click += (_, _) => ToggleMaximize();
        CloseButton.Click += (_, _) => Close();
        FitChrome();
    }

    void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>Keeps the page inside the screen when maximized, and shows the right maximize glyph.</summary>
    void FitChrome()
    {
        var max = WindowState == WindowState.Maximized;
        Root.Margin = max ? OffScreenMargin : default;
        MaxGlyph.Data = Geometry.Parse(max
            ? "M2.5,2.5 V0.5 H9.5 V7.5 H7.5 M0.5,2.5 H7.5 V9.5 H0.5 Z"
            : "M0.5,0.5 H9.5 V9.5 H0.5 Z");
        ToolTip.SetTip(MaxButton, max ? "Restore" : "Maximize");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (Root != null && (change.Property == WindowStateProperty || change.Property == OffScreenMarginProperty))
            FitChrome();
    }

    // ------------------------------------------------------------------ window and tray

    /// <summary>Start without showing the window; the tray icon is the way in.</summary>
    public void StartInTray() => _ = Refresh();

    public void BringForward()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        _ = Refresh();
    }

    public bool HasSession => client.Token != null;

    /// <summary>Signing out lives in the tray menu.</summary>
    public async void SignOut()
    {
        if (client.Token == null) return;
        await client.Logout();
        Show(false);
    }

    public async void PickFromTray()
    {
        BringForward();
        if (AppSection.IsVisible) await PickFiles();
    }

    /// <summary>Sends what is on the clipboard: files if there are any, otherwise its text.</summary>
    public async void SendClipboard()
    {
        if (!AppSection.IsVisible)
        {
            BringForward();
            return;
        }
        if (await ClipboardFiles() is { Count: > 0 } files)
        {
            await AddFiles(files);
            return;
        }
        var text = Clipboard == null ? null : await Clipboard.GetTextAsync();
        if (!string.IsNullOrWhiteSpace(text)) EnqueueText(text);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (WindowState == WindowState.Normal)
        {
            settings.Width = Width;
            settings.Height = Height;
            settings.Save();
        }
        if (!App.Quitting)
        {
            e.Cancel = true;
            if (settings.CloseToTray) Hide();
            else Dispatcher.UIThread.Post(() => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown());
        }
        base.OnClosing(e);
    }

    // ------------------------------------------------------------------ layout that follows the width

    static double Clamp(double min, double value, double max) => Math.Max(min, Math.Min(value, max));

    /// <summary>The page's clamp(…vw…) sizes, worked out from the window width.</summary>
    void Fit()
    {
        var w = ClientSize.Width;
        if (w <= 0) w = Width;
        var gutter = Clamp(16, w * 0.04, 48);
        Page.Padding = new Thickness(gutter, 0, gutter, 40);
        var content = Math.Min(720, w) - 2 * gutter;

        var loginPad = Clamp(26, w * 0.06, 48);
        LoginBody.Margin = new Thickness(loginPad);
        Display(LoginTitle, Clamp(36, w * 0.08, 60));
        LoginTitle.MaxWidth = Math.Ceiling(9 * ZeroWidth(LoginTitle)); // max-width: 9ch
        LoginForm.Width = Math.Max(120, Math.Min(380, content - 2 * loginPad));

        ZoneBody.Margin = new Thickness(22, Clamp(40, w * 0.09, 72));
        Display(ZoneTitle, Clamp(34, w * 0.08, 56));

        PwForm.Width = Math.Max(120, Math.Min(380, content - 36));
    }

    /// <summary>.fz-display: tight leading and tracking that scale with the size.</summary>
    static void Display(TextBlock t, double size)
    {
        t.FontSize = size;
        t.LineHeight = size * 0.96;
        t.LetterSpacing = size * -0.042;
    }

    static double ZeroWidth(TextBlock t) =>
        new FormattedText("0", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(t.FontFamily, FontStyle.Normal, FontWeight.Bold), t.FontSize, Brushes.Black).Width;

    static void Lean(FlowSurface flow, Control host, PointerEventArgs e)
    {
        var p = e.GetPosition(host);
        if (host.Bounds.Width > 0 && host.Bounds.Height > 0)
            flow.LeanToward(p.X / host.Bounds.Width, 1 - p.Y / host.Bounds.Height);
    }

    void SetHint() =>
        ZoneHint.Text = $"Drag them onto this window, paste them, or choose them. They stay for {ttlText}, then they are gone.";

    // ------------------------------------------------------------------ formatting, as in app.js

    static string Fixed(double v, int digits) => v.ToString("F" + digits, CultureInfo.InvariantCulture);

    static string FmtSize(long n) =>
        n < 1024 ? n + " B"
        : n < 1048576 ? Fixed(n / 1024.0, 0) + " KB"
        : n < 1073741824 ? Fixed(n / 1048576.0, 1) + " MB"
        : Fixed(n / 1073741824.0, 2) + " GB";

    static string FmtLeft(double s) =>
        s <= 0 ? "about to go"
        : s < 60 ? "under a minute left"
        : s < 3600 ? Math.Ceiling(s / 60) + " min left"
        : Fixed(s / 3600, 1) + " h left";

    static string FmtTtl(long s) =>
        s == 3600 ? "an hour"
        : s < 3600 ? Math.Round(s / 60.0) + " minutes"
        : Fixed(s / 3600.0, s % 3600 != 0 ? 1 : 0) + " hours";

    // ------------------------------------------------------------------ signed in or not

    void Show(bool authed)
    {
        Login.IsVisible = !authed;
        AppSection.IsVisible = authed;
        if (authed) return;
        listKey = null;
        CountChanged?.Invoke(null);
        UserChanged?.Invoke(null);
        if (IsVisible) User.Focus();
    }

    async Task Refresh()
    {
        if (client.Token == null)
        {
            Show(false);
            return;
        }
        if (refreshing) return;
        refreshing = true;
        try
        {
            var (reply, data) = await client.List();
            lastRefresh = DateTime.UtcNow;
            if (reply.Status == 401)
            {
                client.Forget();
                Show(false);
                return;
            }
            if (data == null)
            {
                // Nothing to show yet and no answer: say so instead of leaving the list blank.
                if (!everListed) Usage.Text = "The server did not answer. Check the connection; Drops keeps trying.";
                return;
            }
            everListed = true;
            LoginErr.Text = "";
            Show(true);
            UserChanged?.Invoke(data.User);
            ttlText = FmtTtl(data.Ttl);
            SetHint();
            Usage.Text = FmtSize(data.Used) + " of " + FmtSize(data.Max) + " in use";
            CountChanged?.Invoke(data.Files.Length);

            // Rebuild the list only when its contents change; otherwise just move the gauges.
            var key = string.Join(",", data.Files.Select(f => f.Id));
            if (key != listKey)
            {
                listKey = key;
                Render(data.Files);
            }
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            foreach (var f in data.Files)
            {
                if (!rows.TryGetValue(f.Id, out var row)) continue;
                var left = f.Expires - now;
                row.info.Text = FmtSize(f.Size) + ", " + FmtLeft(left);
                row.gauge.Value = data.Ttl > 0 ? Math.Clamp(left / data.Ttl, 0, 1) : 0;
            }
        }
        finally
        {
            refreshing = false;
        }
    }

    // ------------------------------------------------------------------ the list

    IBrush Brush(string key) => (IBrush)this.FindResource(key)!;

    static Button Btn(string text, string classes)
    {
        var b = new Button { Content = text };
        b.Classes.AddRange(classes.Split(' '));
        return b;
    }

    /// <summary>.remove: an underlined text link.</summary>
    static Button Link(string text)
    {
        var b = new Button { Content = new TextBlock { Text = text, TextDecorations = TextDecorations.Underline } };
        b.Classes.Add("remove");
        return b;
    }

    /// <summary>A .file card: name and details on the left, actions on the right, a gauge underneath.</summary>
    (Border card, Grid row, StackPanel acts, StackPanel body, TextBlock info) Card(string name, string infoText)
    {
        var info = new TextBlock
        {
            Text = infoText, FontSize = 13.5, LineHeight = 21, Margin = new Thickness(0, 2, 0, 0),
            Foreground = Brush("SoftBrush"), TextWrapping = TextWrapping.Wrap,
            FontFeatures = [FontFeature.Parse("tnum")], // tabular-nums: the countdown does not wobble
        };
        var meta = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = name, FontWeight = FontWeight.SemiBold, LineHeight = 26.35, TextWrapping = TextWrapping.Wrap },
                info,
            },
        };
        var acts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(acts, 1);
        row.Children.Add(meta);
        row.Children.Add(acts);

        var body = new StackPanel { Children = { row } };
        var card = new Border { Child = body };
        card.Classes.Add("file");

        // file-in: it arrives from just above, slightly small.
        card.Opacity = 0;
        card.RenderTransform = TransformOperations.Parse("translateY(-14px) scale(0.97)");
        card.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            card.Opacity = 1;
            card.RenderTransform = TransformOperations.Parse("translateY(0px) scale(1)");
        }, DispatcherPriority.Background);
        return (card, row, acts, body, info);
    }

    void Render(DropFile[] files)
    {
        List.Children.Clear();
        rows.Clear();
        Clear.IsVisible = files.Length > 0;
        DownloadAll.IsVisible = files.Length > 0;
        ToolTip.SetTip(DownloadAll, files.Length == 1 ? "One file, as a zip" : files.Length + " files in one zip");
        if (files.Length == 0)
        {
            List.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(20), Padding = new Thickness(18, 26), Background = Brush("WashBrush"),
                Child = new TextBlock
                {
                    Text = "Nothing here yet. Put something in from either device.", LineHeight = 26.35,
                    Foreground = Brush("SoftBrush"), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                },
            });
            return;
        }

        foreach (var f in files)
        {
            var (card, _, acts, body, info) = Card(f.Name, "");

            var save = Btn("Save", "btn small");
            save.Click += async (_, _) => await Save(f, save);
            acts.Children.Add(save);

            if (!string.IsNullOrEmpty(f.Text))
            {
                var copy = Btn("Copy", "btn small quiet");
                copy.Click += async (_, _) =>
                {
                    try
                    {
                        await Clipboard!.SetTextAsync(f.Text);
                        copy.Content = "Copied";
                    }
                    catch (Exception)
                    {
                        copy.Content = "Select it below";
                    }
                    await Task.Delay(1600);
                    copy.Content = "Copy";
                };
                acts.Children.Add(copy);
            }
            else if (f.Inline)
            {
                var view = Btn("View", "btn small quiet");
                view.Click += async (_, _) => await View(f, view);
                acts.Children.Add(view);
            }

            var remove = Link("Remove");
            remove.Click += async (_, _) =>
            {
                remove.IsEnabled = false;
                await client.Delete(f.Id);
                await Refresh();
            };
            acts.Children.Add(remove);

            if (!string.IsNullOrEmpty(f.Text))
            {
                body.Children.Add(new Border
                {
                    Margin = new Thickness(0, 12, 0, 0), CornerRadius = new CornerRadius(12), ClipToBounds = true,
                    Background = Brush("WashBrush"),
                    Child = new ScrollViewer
                    {
                        MaxHeight = 200,
                        Content = new SelectableTextBlock
                        {
                            Text = f.Text, Padding = new Thickness(14, 12), FontSize = 14, LineHeight = 21,
                            FontFamily = (FontFamily)this.FindResource("MonoFont")!, TextWrapping = TextWrapping.Wrap,
                            SelectionBrush = Brush("InkBrush"), SelectionForegroundBrush = Brush("PaperBrush"),
                        },
                    },
                });
            }

            var gauge = new Gauge { Fill = Brush("SageBrush"), Margin = new Thickness(0, 12, 0, 0) };
            body.Children.Add(gauge);
            rows[f.Id] = (info, gauge);
            List.Children.Add(card);
        }
    }

    async Task Save(DropFile f, Button button)
    {
        var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save " + f.Name, SuggestedFileName = f.Name, ShowOverwritePrompt = true,
        });
        if (target == null) return;
        button.IsEnabled = false;
        button.Content = "Saving";
        Reply reply;
        try
        {
            await using var stream = await target.OpenWriteAsync();
            reply = await client.Download(f.Id, stream);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            reply = new Reply(0, "");
        }
        button.Content = reply.Ok ? "Saved" : reply.Status == 404 ? "Gone" : "Not saved";
        await Task.Delay(1600);
        button.Content = "Save";
        button.IsEnabled = true;
        if (reply.Status is 401 or 404) await Refresh();
    }

    /// <summary>
    /// "Download all", as on the page: everything in the drop as one zip. The server is asked first,
    /// so nothing is created on disk when the drop is empty or the server is older than the feature.
    /// </summary>
    async Task SaveAll()
    {
        DownloadAll.IsEnabled = false;
        DownloadAll.Content = "Preparing";
        var (reply, response, fileName) = await client.OpenAll();
        using (response)
        {
            if (response != null)
            {
                var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save everything in the drop", SuggestedFileName = fileName, DefaultExtension = "zip",
                    ShowOverwritePrompt = true,
                    FileTypeChoices = [new FilePickerFileType("Zip archive") { Patterns = ["*.zip"] }],
                });
                if (target == null)
                {
                    DownloadAll.Content = "Download all";
                    DownloadAll.IsEnabled = true;
                    return;
                }
                DownloadAll.Content = "Saving";
                try
                {
                    await using (var stream = await target.OpenWriteAsync())
                        reply = await DropsClient.Finish(response, stream);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    reply = new Reply(0, "");
                }
                // A zip cut off half-way does not open; do not leave one behind.
                if (!reply.Ok)
                {
                    try { await target.DeleteAsync(); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                }
            }
        }

        DownloadAll.Content = reply.Status switch
        {
            200 => "Saved",
            404 when reply.Text == "the drop is empty" => "Nothing to save",
            404 => "Not on this server yet",
            401 => "Signed out",
            _ => "Not saved",
        };
        await Task.Delay(reply.Status == 404 && reply.Text != "the drop is empty" ? 2600 : 1600);
        DownloadAll.Content = "Download all";
        DownloadAll.IsEnabled = true;
        if (reply.Status == 401)
        {
            client.Forget();
            Show(false);
        }
        else if (reply.Status == 404) await Refresh();
    }

    /// <summary>The page opens pictures, videos and text in a new tab; here they open in the system's own viewer.</summary>
    async Task View(DropFile f, Button button)
    {
        button.IsEnabled = false;
        button.Content = "Opening";
        var ok = false;
        try
        {
            var dir = Path.Combine(AppPaths.ViewCache, f.Id);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, Path.GetFileName(f.Name));
            Reply reply;
            await using (var stream = File.Create(path))
                reply = await client.Download(f.Id, stream);
            if (reply.Ok)
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                ok = true;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
        }
        button.Content = ok ? "View" : "Not opened";
        if (!ok) await Task.Delay(1600);
        button.Content = "View";
        button.IsEnabled = true;
    }

    // ------------------------------------------------------------------ sending: one file at a time,
    // so progress stays honest on a slow link

    void Enqueue(Func<Task<Stream>> open, long size, string name)
    {
        var (card, row, _, body, info) = Card(name, FmtSize(size) + ", waiting");
        var fill = new Gauge(TimeSpan.FromSeconds(0.2)) { Fill = Brush("TerraBrush"), Margin = new Thickness(0, 12, 0, 0) };
        body.Children.Add(fill);
        Queue.Children.Add(card);
        QueueWrap.IsVisible = true;
        pending.Enqueue(new Job { Open = open, Size = size, Name = name, Card = card, Row = row, Info = info, Fill = fill });
        Pump();
    }

    void EnqueueText(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        Enqueue(() => Task.FromResult<Stream>(new MemoryStream(bytes)), bytes.Length,
            "text-" + DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture) + ".txt");
    }

    async void Pump()
    {
        if (busy) return;
        if (pending.Count == 0)
        {
            if (Queue.Children.Count == 0) QueueWrap.IsVisible = false;
            return;
        }
        var job = pending.Dequeue();
        busy = true;

        Reply reply;
        var lastShown = 0L;
        try
        {
            await using var source = await job.Open();
            reply = await client.Upload(source, job.Size, job.Name, (sent, total) =>
            {
                // Called from the network thread, for every block: show a few updates a second.
                var tick = Environment.TickCount64;
                if (sent < total && tick - lastShown < 60) return;
                lastShown = tick;
                Dispatcher.UIThread.Post(() =>
                {
                    if (job.Done) return;
                    job.Fill.Value = total > 0 ? (double)sent / total : 1;
                    job.Info.Text = FmtSize(sent) + " of " + FmtSize(total);
                });
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            reply = new Reply(-1, "");
        }
        job.Done = true;
        busy = false;

        if (reply.Status == 200)
        {
            Queue.Children.Remove(job.Card);
        }
        else
        {
            Fail(job, reply.Status switch
            {
                401 => "Not sent: you were signed out. Sign in and send it again.",
                507 => "Not sent: the drop is full. Remove something and send it again.",
                0 => "Not sent: the connection dropped. Send it again.",
                -1 => "Not sent: the file could not be read.",
                _ => "Not sent: " + (reply.Text.Length > 0 ? reply.Text : "error " + reply.Status) + ".",
            });
            if (reply.Status == 401)
            {
                client.Forget();
                Show(false);
            }
        }
        await Refresh();
        Pump();
    }

    void Fail(Job job, string message)
    {
        job.Card.Classes.Add("failed");
        job.Info.Text = message;
        job.Info.Foreground = Brush("DangerBrush");
        job.Fill.IsVisible = false;
        var dismiss = Link("Dismiss");
        dismiss.VerticalAlignment = VerticalAlignment.Center;
        dismiss.Click += (_, _) =>
        {
            Queue.Children.Remove(job.Card);
            Pump();
        };
        Grid.SetColumn(dismiss, 1);
        job.Row.Children.Add(dismiss);
    }

    async Task AddFiles(IEnumerable<IStorageItem> items)
    {
        foreach (var item in items)
        {
            if (item is not IStorageFile file) continue; // folders are not sent
            var name = string.IsNullOrEmpty(file.Name) ? "file" : file.Name;
            if (file.TryGetLocalPath() is { } path)
            {
                long size;
                try { size = new FileInfo(path).Length; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
                Enqueue(() => Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, true)), size, name);
            }
            else
            {
                var props = await file.GetBasicPropertiesAsync();
                Enqueue(() => file.OpenReadAsync(), (long)(props.Size ?? 0), name);
            }
        }
    }

    async Task PickFiles()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose files to send", AllowMultiple = true });
        await AddFiles(files);
    }

    // ------------------------------------------------------------------ drag, drop and paste

    void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        e.DragEffects = AppSection.IsVisible ? DragDropEffects.Copy : DragDropEffects.None;
        SetDragOver(true);
    }

    void OnDragLeave(object? sender, DragEventArgs e)
    {
        // Moving between controls inside the window leaves one and enters the next: wait and see.
        dragOver = false;
        Dispatcher.UIThread.Post(() =>
        {
            if (!dragOver) SetDragOver(false);
        }, DispatcherPriority.Background);
    }

    async void OnDrop(object? sender, DragEventArgs e)
    {
        SetDragOver(false);
        if (!AppSection.IsVisible || e.Data.GetFiles() is not { } files) return;
        await AddFiles(files);
    }

    void SetDragOver(bool over)
    {
        dragOver = over;
        Zone.Classes.Set("over", over);
        ZoneFlow.IsSurging = over;
    }

    async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var paste = e.Key == Key.V && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta));
        if (!paste || !AppSection.IsVisible || FocusManager?.GetFocusedElement() is TextBox) return;
        if (await ClipboardFiles() is { Count: > 0 } files) await AddFiles(files);
    }

    async Task<List<IStorageItem>?> ClipboardFiles()
    {
        try
        {
            if (Clipboard == null) return null;
            return (await Clipboard.GetDataAsync(DataFormats.Files) as IEnumerable<IStorageItem>)?.ToList();
        }
        catch (Exception)
        {
            return null; // clipboard contents the platform cannot hand over
        }
    }

    // ------------------------------------------------------------------ forms

    async Task SubmitLogin()
    {
        LoginErr.Text = "";
        if (string.IsNullOrWhiteSpace(User.Text) || string.IsNullOrEmpty(Pass.Text)) return;
        SignIn.IsEnabled = false;
        var reply = await client.Login(User.Text.Trim(), Pass.Text);
        SignIn.IsEnabled = true;
        if (reply.NoAnswer)
        {
            LoginErr.Text = "The server did not answer. Check the connection and try again.";
        }
        else if (reply.Ok && client.Token != null)
        {
            Pass.Text = "";
            await Refresh();
        }
        else if (reply.Status == 429)
        {
            LoginErr.Text = "Too many attempts. Wait a few minutes, then try again.";
        }
        else
        {
            LoginErr.Text = "That user name and password do not match.";
        }
    }

    async Task SubmitPassword()
    {
        PwMsg.Classes.Remove("ok");
        PwMsg.Text = "";
        var current = PwCurrent.Text ?? "";
        var fresh = PwNew.Text ?? "";
        if (current.Length == 0 || fresh.Length == 0) return;
        if (fresh.Length < 12)
        {
            PwMsg.Text = "Passwords are 12 characters or longer.";
            return;
        }
        if (fresh != (PwRepeat.Text ?? ""))
        {
            PwMsg.Text = "The two new passwords do not match.";
            return;
        }
        PwSubmit.IsEnabled = false;
        var reply = await client.ChangePassword(current, fresh);
        PwSubmit.IsEnabled = true;
        if (reply.NoAnswer)
        {
            PwMsg.Text = "The server did not answer. Check the connection and try again.";
        }
        else if (reply.Ok)
        {
            PwCurrent.Text = PwNew.Text = PwRepeat.Text = "";
            PwMsg.Classes.Add("ok");
            PwMsg.Text = "Password changed. Your other devices are signed out.";
        }
        else
        {
            PwMsg.Text = reply.Text.Length > 0 ? reply.Text : "That did not work. Try again.";
        }
    }

    // ------------------------------------------------------------------ small things

    static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
        }
    }

    /// <summary>The page's confirm(): a small question in the kit's own look.</summary>
    async Task<bool> Confirm(string question, string yes)
    {
        var ok = Btn(yes, "btn small");
        var cancel = Btn("Cancel", "btn small quiet");
        var dialog = new Window
        {
            Title = "Drops", Icon = Icon, Width = 380, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Background = Brush("PaperBrush"), Foreground = Brush("InkBrush"), FontFamily = FontFamily, FontSize = 17,
            Content = new StackPanel
            {
                Margin = new Thickness(26), Spacing = 22,
                Children =
                {
                    new TextBlock { Text = question, FontWeight = FontWeight.SemiBold, LineHeight = 26.35, TextWrapping = TextWrapping.Wrap },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } },
                },
            },
        };
        ok.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        cancel.IsCancel = true;
        return await dialog.ShowDialog<bool>(this);
    }
}
