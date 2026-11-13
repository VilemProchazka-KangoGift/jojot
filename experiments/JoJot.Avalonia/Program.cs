using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using JoJot.Services;
using JoJot.AvaloniaExperiment.Platform;
using Avalonia.Threading;
using Avalonia.Input;
using JoJot.Models;

namespace JoJot.AvaloniaExperiment;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var entry = System.Diagnostics.Stopwatch.GetTimestamp();
        var loggingInitialized = false;
        try
        {
            if (args.Length == 2 && args[0] == "--ipc-owner-probe")
            {
                var probeOptions = ExperimentOptions.Parse(["--data-dir", args[1]]);
                probeOptions.ValidateDataPath();
                AppEnvironment.SetAppDataDirectory(probeOptions.DataDirectory);
                using var probeOwner = new InstanceChannel(probeOptions.DataDirectory);
                if (!probeOwner.IsPrimary) return 1;
                Console.WriteLine("IPC owner ready");
                Thread.Sleep(TimeSpan.FromSeconds(30));
                return 0;
            }
            if (args.Length == 2 && args[0] == "--ipc-client-probe")
            {
                var probeOptions = ExperimentOptions.Parse(["--data-dir", args[1]]);
                probeOptions.ValidateDataPath();
                AppEnvironment.SetAppDataDirectory(probeOptions.DataDirectory);
                using var probeClient = new InstanceChannel(probeOptions.DataDirectory);
                if (probeClient.IsPrimary) return 2;
                var probeResult = probeClient.Send(new ActivateCommand());
                Console.WriteLine($"IPC result: {probeResult}");
                return probeResult == InstanceChannel.SendResult.Takeover ? 0 : 1;
            }
            if (args.Length == 1 && args[0] == "--desktop-probe")
            {
                var current = VirtualDesktop.GetCurrentDesktopOrDefault();
                var desktops = VirtualDesktop.GetDesktops();
                Console.WriteLine($"Current: {current}");
                foreach (var desktop in desktops)
                    Console.WriteLine($"Desktop: {desktop.Id} {desktop.Index} {desktop.Name}");
                using var notifications = DesktopNotifications.TryRegister(() => { });
                Console.WriteLine(notifications is not null ? "Notifications: registered" :
                    Environment.OSVersion.Version.Build < 26100 ? "Notifications: unsupported" : "Notifications: failed");
                return current != "default" && desktops.Any(d => string.Equals(d.Id, current, StringComparison.OrdinalIgnoreCase)) &&
                    (Environment.OSVersion.Version.Build < 26100 || notifications is not null) ? 0 : 1;
            }
            if (args.Length == 1 && args[0] == "--hotkey-probe")
            {
                using var received = new ManualResetEventSlim();
                using var hotkey = new GlobalHotkey(0x0007, 0x86, received.Set); // Ctrl+Alt+Shift+F23
                return hotkey.Registered && hotkey.PostTestMessage() && received.Wait(TimeSpan.FromSeconds(5)) ? 0 : 1;
            }
            if (args.Length == 4 && args[0] == "--data-dir" &&
                args[2] is "--desktop-move-probe" or "--desktop-move-probe-merge")
            {
                if (!Guid.TryParse(args[3], out var target) || target == Guid.Empty)
                    throw new ArgumentException("The desktop move probe requires a valid target GUID.");
                ExperimentApp.DesktopMoveProbeMerge = args[2] == "--desktop-move-probe-merge";
                args = args[..2];
                ExperimentApp.DesktopMoveProbe = target.ToString();
            }
            var options = ExperimentOptions.Parse(args);
            options.ValidateDataPath();
            AppEnvironment.SetAppDataDirectory(options.DataDirectory);
            LogService.Initialize(options.DataDirectory);
            loggingInitialized = true;
            int result;
            using (var instance = new InstanceChannel(options.DataDirectory))
            {
                if (instance is { IsPrimary: false })
                {
                    var command = new NewTabCommand(DesktopGuid: VirtualDesktop.GetCurrentDesktopOrDefault());
                    var sendResult = instance.Send(command);
                    if (sendResult != InstanceChannel.SendResult.Takeover)
                        return sendResult == InstanceChannel.SendResult.Delivered ? 0 : 1;
                    ExperimentApp.QueueCommand(command);
                }
                instance.Start(ExperimentApp.QueueCommand);
                ExperimentApp.Options = options;
                ExperimentApp.Benchmark = options.BenchmarkOutput is null ? null : new BenchmarkProbe(entry, options);
                result = AppBuilder.Configure<ExperimentApp>().UsePlatformDetect().StartWithClassicDesktopLifetime([]);
            }
            if (ExperimentApp.RestartRequested)
            {
                var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                    start.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
                foreach (var arg in args) start.ArgumentList.Add(arg);
                System.Diagnostics.Process.Start(start);
            }
            return result != 0 ? result : ExperimentApp.FailExitCode;
        }
        catch (Exception ex)
        {
            if (loggingInitialized) LogService.Error("Application failed", ex);
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            if (loggingInitialized) LogService.Shutdown();
        }
    }
}

internal sealed class ExperimentApp : Application
{
    internal static string? DesktopMoveProbe { get; set; }
    internal static bool DesktopMoveProbeMerge { get; set; }
    internal static ExperimentOptions Options { get; set; } = null!;
    internal static BenchmarkProbe? Benchmark { get; set; }
    internal static bool RestartRequested { get; private set; }
    internal static int FailExitCode { get; set; }
    internal static Task StartupRecovery { get; private set; } = Task.CompletedTask;
    private static readonly Lock StorageLock = new();
    private static Task? _storageTask;
    private static FileStream? _dataLock;
    private static readonly Lock CommandLock = new();
    private static readonly Queue<IpcMessage> PendingCommands = new();
    private static ExperimentApp? _current;
    private static IReadOnlyList<VirtualDesktop.Desktop> _liveDesktops = [];
    private static string _initialDesktop = "default";
    private static bool _matchedEarly;
    private readonly Dictionary<string, MainWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _orphans = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PendingMove> _pendingMoveReview = [];
    private readonly HashSet<string> _reservedDesktops = new(StringComparer.OrdinalIgnoreCase);
    private TaskCompletionSource? _reservationsDrained;
    private readonly HashSet<string> _pendingActivations = new(StringComparer.OrdinalIgnoreCase);
    private GlobalHotkey? _hotkey;
    private DesktopNotifications? _desktopNotifications;
    private DispatcherTimer? _desktopPoll;
    private bool _desktopRefreshRunning;
    private bool _desktopMatching;
    private bool _matchPending;
    private bool _shuttingDown;
    private bool _probePendingObserved;
    private string? _probeSource;
    private uint _hotkeyModifiers = 0x000C;
    private uint _hotkeyKey = 0x4E;
    internal static bool ManagesStorageLifetime { get; private set; }

    internal static void QueueCommand(IpcMessage message)
    {
        lock (CommandLock)
            PendingCommands.Enqueue(message);
        if (_current is not null)
            Dispatcher.UIThread.Post(_current.DrainCommands);
    }

    private void DrainCommands()
    {
        if (_shuttingDown) return;
        while (true)
        {
            IpcMessage message;
            lock (CommandLock)
            {
                if (!PendingCommands.TryDequeue(out message!))
                    return;
            }
            var desktop = message switch
            {
                NewTabCommand { DesktopGuid: { Length: > 0 } guid } => guid,
                ShowDesktopCommand show => show.DesktopGuid,
                _ => VirtualDesktop.GetCurrentDesktopOrDefault()
            };
            lock (CommandLock)
            {
                if (_reservedDesktops.Contains(desktop))
                {
                    PendingCommands.Enqueue(message);
                    return;
                }
            }
            var window = GetOrCreateWindow(desktop);
            if (message is NewTabCommand or ShowDesktopCommand && desktop != "default")
                VirtualDesktop.TrySwitchToDesktop(desktop);
            WindowActivation.Activate(window);
            if (message is NewTabCommand newTab) window.RequestNewTab(newTab.InitialContent);
        }
    }

    private MainWindow GetOrCreateWindow(string desktop)
    {
        if (_windows.TryGetValue(desktop, out var existing))
            return existing;
        var window = new MainWindow(desktop);
        var info = _liveDesktops.FirstOrDefault(item => item.Id.Equals(desktop, StringComparison.OrdinalIgnoreCase));
        if (info is not null) window.UpdateDesktopTitle(info.Name, info.Index);
        _windows.Add(desktop, window);
        window.Closed += (_, _) => _windows.Remove(window.DesktopGuid);
        window.InitializeDesktopMovement();
        AttachWindowPlacement(window);
        window.Show();
        return window;
    }

    internal static Task EnsureStorageAsync()
    {
        lock (StorageLock)
            return _storageTask ??= OpenStorageAsync();
    }

    private static async Task OpenStorageAsync()
    {
        Options.ValidateDataPath();
        AppEnvironment.SetAppDataDirectory(Options.DataDirectory);
        _dataLock = new FileStream(Path.Combine(Options.DataDirectory, ".experiment-lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            await DatabaseCore.OpenAsync(Path.Combine(Options.DataDirectory, "jojot.db"));
            await DatabaseCore.EnsureSchemaAsync();
            if (!await DatabaseCore.VerifyIntegrityAsync())
                throw new IOException("The experiment database failed its integrity check.");
            await DatabaseCore.RunPendingMigrationsAsync();
            var preferences = await PreferenceStore.InitializeCacheAsync();
            if (preferences.TryGetValue("log_level", out var level) &&
                Enum.TryParse<Serilog.Events.LogEventLevel>(level, true, out var parsedLevel))
                LogService.SetMinimumLevel(parsedLevel);
            preferences.TryGetValue("language", out var language);
            PanelLanguage.Apply(language);
            StartupSnapshot.ReplacePreferences(preferences);
            if (Options.CompatibilityExpected is null && Options.VisualOutputDirectory is null && CanMatchDesktops() &&
                !await SessionStore.SessionExistsAsync(_initialDesktop) && (await PendingMoveStore.GetPendingMovesAsync()).Count == 0)
            {
                if (_current is { } host)
                    _matchedEarly = await host.TryMatchDesktopsAsync(_liveDesktops);
            }
            await MainWindow.RunStartupAutoDeleteAsync();
            if (Options.CompatibilityExpected is null && Options.VisualOutputDirectory is null &&
                await DatabaseCore.ExecuteScalarAsync<long>("SELECT EXISTS(SELECT 1 FROM notes);") == 0)
            {
                await DatabaseCore.ExecuteNonQueryAsync(
                    "INSERT INTO notes (desktop_guid, name, content, pinned, sort_order) VALUES (@guid, @name, @content, 0, 0);",
                    ("@guid", _initialDesktop), ("@name", JoJot.Resources.Strings.Welcome_Title),
                    ("@content", JoJot.Resources.Strings.Welcome_Content));
            }
        }
        catch
        {
            await DatabaseCore.CloseAsync();
            _dataLock.Dispose();
            _dataLock = null;
            throw;
        }
    }

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ManagesStorageLifetime = true;
            Options.ValidateDataPath();
            AppEnvironment.SetAppDataDirectory(Options.DataDirectory);
            if (StartupSnapshot.TryLoad())
                PreferenceStore.PreloadProvisional(StartupSnapshot.Preferences);
            StartupSnapshot.Preferences.TryGetValue("language", out var language);
            PanelLanguage.Apply(language);
            _current = this;
            if (Options.BenchmarkOutput is null && Options.CompatibilityExpected is null && Options.VisualOutputDirectory is null)
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var detectedDesktop = VirtualDesktop.GetCurrentDesktopOrDefault();
            _liveDesktops = VirtualDesktop.GetDesktops();
            if (DesktopMoveProbe is { } requested &&
                (detectedDesktop == "default" || requested.Equals(detectedDesktop, StringComparison.OrdinalIgnoreCase) ||
                 !_liveDesktops.Any(item => item.Id.Equals(requested, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidOperationException("The desktop move probe requires a different, existing desktop.");
            _desktopNotifications = DesktopNotifications.TryRegister(() => Dispatcher.UIThread.Post(RefreshDesktops));
            _desktopPoll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _desktopPoll.Tick += (_, _) => RefreshDesktops();
            _desktopPoll.Start();
            if (Options.BenchmarkOutput is not null && Options.DesktopGuid != "default" &&
                !string.Equals(Options.DesktopGuid, detectedDesktop, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Benchmark desktop does not match the current Windows virtual desktop.");
            var initialDesktop = Options.CompatibilityExpected is not null || Options.VisualOutputDirectory is not null ? Options.DesktopGuid :
                Options.BenchmarkOutput is not null && Options.DesktopGuid != "default" ? Options.DesktopGuid : detectedDesktop;
            _initialDesktop = initialDesktop;
            var window = new MainWindow(initialDesktop);
            var info = _liveDesktops.FirstOrDefault(item => item.Id.Equals(initialDesktop, StringComparison.OrdinalIgnoreCase));
            if (info is not null) window.UpdateDesktopTitle(info.Name, info.Index);
            _windows.Add(initialDesktop, window);
            window.Closed += (_, _) => _windows.Remove(window.DesktopGuid);
            window.InitializeDesktopMovement();
            if (DesktopMoveProbe is { } probeTarget)
                window.Opened += async (_, _) =>
                {
                    try
                    {
                        await window.Ready;
                        _probeSource = initialDesktop;
                        if (DesktopMoveProbeMerge)
                        {
                            await NoteStore.InsertNoteAsync(initialDesktop, "Probe source", "Probe source marker", false, 0);
                            await window.ReloadNotesAsync();
                            var targetWindow = GetOrCreateWindow(probeTarget);
                            var targetHandle = targetWindow.TryGetPlatformHandle()?.Handle ?? 0;
                            if (!VirtualDesktop.TryMoveWindowToDesktop(targetHandle, probeTarget))
                                throw new InvalidOperationException("Desktop move probe: target window move failed.");
                            await targetWindow.Ready;
                            await NoteStore.InsertNoteAsync(probeTarget, "Probe target", "Probe target marker", false, 0);
                            await targetWindow.ReloadNotesAsync();
                        }
                        await LogProbeCountsAsync("before", initialDesktop, probeTarget);
                        var handle = window.TryGetPlatformHandle()?.Handle ?? 0;
                        if (!VirtualDesktop.TryMoveWindowToDesktop(handle, probeTarget))
                            throw new InvalidOperationException("Desktop move probe: move failed.");
                        if (!VirtualDesktop.TrySwitchToDesktop(probeTarget))
                            throw new InvalidOperationException("Desktop move probe: switch failed.");
                        window.CheckForDesktopMovement();
                    }
                    catch (Exception ex)
                    {
                        LogService.Error("Desktop move probe failed", ex);
                        Console.Error.WriteLine(ex);
                        FailExitCode = 1;
                        desktop.Shutdown();
                    }
                };
            desktop.MainWindow = window;
            StartupRecovery = RecoverPendingMovesAsync(window);
            if (Options.BenchmarkOutput is null)
                AttachWindowPlacement(window);
            MainWindow.ExitFromMenuAsync = ExitAsync;
            MainWindow.DesktopHasWindow = guid => _windows.ContainsKey(guid);
            MainWindow.DesktopShutdownPending = () => _shuttingDown;
            MainWindow.DesktopMatchingInProgress = () => _desktopMatching;
            MainWindow.DesktopRefreshPending = RefreshPendingReviewAsync;
            MainWindow.DesktopReparent = (oldGuid, newGuid) =>
            {
                if (_windows.Remove(oldGuid, out var moved)) _windows.Add(newGuid, moved);
            };
            MainWindow.DesktopReloadTarget = async guid =>
            {
                if (_windows.TryGetValue(guid, out var target)) await target.ReloadNotesAsync();
            };
            MainWindow.DesktopCloseSource = source => source.Close();
            MainWindow.DesktopReserveTarget = guid =>
            {
                if (_shuttingDown || !_reservedDesktops.Add(guid)) return false;
                _reservationsDrained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                return true;
            };
            MainWindow.DesktopReleaseTarget = guid =>
            {
                _reservedDesktops.Remove(guid);
                if (_reservedDesktops.Count == 0)
                {
                    _reservationsDrained?.TrySetResult();
                    _reservationsDrained = null;
                }
                DrainCommands();
                if (!_shuttingDown && _pendingActivations.Remove(guid)) GetOrCreateWindow(guid).Activate();
            };
            MainWindow.OrphanSessionGuids = () => _orphans.ToArray();
            MainWindow.OrphanSessionResolved = guid => _orphans.Remove(guid);
            MainWindow.ReviewPendingMoves = () => _pendingMoveReview.ToArray();
            MainWindow.PendingMoveResolved = id =>
            {
                _pendingMoveReview.RemoveAll(move => move.Id == id);
            };
            MainWindow.CanOpenRecoveryDesktop = guid => _liveDesktops.Any(item => item.Id.Equals(guid, StringComparison.OrdinalIgnoreCase));
            MainWindow.OpenRecoveryDesktopAsync = guid =>
            {
                if (!_shuttingDown && _liveDesktops.Any(item => item.Id.Equals(guid, StringComparison.OrdinalIgnoreCase)))
                {
                    var target = GetOrCreateWindow(guid);
                    if (guid != "default") VirtualDesktop.TrySwitchToDesktop(guid);
                    target.Activate();
                }
                return Task.CompletedTask;
            };
            MainWindow.RestartForLanguageAsync = RestartForLanguageAsync;
            MainWindow.SaveWindowGeometryAsync = SaveWindowGeometryAsync;
            MainWindow.CurrentGlobalShortcut = () => $"{(_hotkeyModifiers & 8) switch { 0 => "", _ => "Win + " }}{(_hotkeyModifiers & 2) switch { 0 => "", _ => "Ctrl + " }}{(_hotkeyModifiers & 1) switch { 0 => "", _ => "Alt + " }}{(_hotkeyModifiers & 4) switch { 0 => "", _ => "Shift + " }}{GlobalHotkey.KeyName(_hotkeyKey)}";
            MainWindow.PauseGlobalShortcut = paused => _hotkey?.PauseForRecording(paused);
            MainWindow.RecordGlobalShortcutAsync = UpdateHotkeyAsync;
            desktop.ShutdownRequested += (_, _) => FlushForSessionEnd();
            Dispatcher.UIThread.Post(DrainCommands);
            if (Options.BenchmarkOutput is null && Options.CompatibilityExpected is null && Options.VisualOutputDirectory is null)
                _ = InitializeHotkeyAsync();
            desktop.Exit += (_, _) =>
            {
                if (DesktopMoveProbe is not null && !VirtualDesktop.TrySwitchToDesktop(detectedDesktop))
                    Console.Error.WriteLine("Desktop move probe: could not restore the original desktop.");
                _hotkey?.Dispose();
                _desktopPoll?.Stop();
                _desktopNotifications?.Dispose();
                if (_storageTask?.IsCompletedSuccessfully == true)
                {
                    StartupSnapshot.FlushForTestsAsync().GetAwaiter().GetResult();
                    DatabaseCore.CloseAsync().GetAwaiter().GetResult();
                }
                _dataLock?.Dispose();
                _dataLock = null;
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    private async Task RefreshPendingReviewAsync()
    {
        var moves = await PendingMoveStore.GetPendingMovesAsync();
        _pendingMoveReview.Clear();
        _pendingMoveReview.AddRange(moves);
        foreach (var window in _windows.Values) window.NotifyOrphansChanged();
        if (_probeSource is { } source && DesktopMoveProbe is { } target)
        {
            if (moves.Any(move => move.WindowId.Equals(source, StringComparison.OrdinalIgnoreCase)))
                _probePendingObserved = true;
            else if (_probePendingObserved)
            {
                _probePendingObserved = false;
                await LogProbeCountsAsync("resolved", source, target);
                if (_windows.TryGetValue(source, out var owner) &&
                    string.Equals(VirtualDesktop.GetWindowDesktopId(owner.TryGetPlatformHandle()?.Handle ?? 0), source,
                        StringComparison.OrdinalIgnoreCase))
                    VirtualDesktop.TrySwitchToDesktop(source);
            }
        }
    }

    private static async Task LogProbeCountsAsync(string phase, string source, string target)
    {
        var sourceCount = (await NoteStore.GetNotesForDesktopAsync(source)).Count;
        var targetCount = (await NoteStore.GetNotesForDesktopAsync(target)).Count;
        Console.WriteLine($"Desktop move probe {phase}: source={source} target={target} sourceNotes={sourceCount} targetNotes={targetCount}");
    }

    private async void RefreshDesktops()
    {
        if (_desktopRefreshRunning || _storageTask?.IsCompletedSuccessfully != true) return;
        _desktopRefreshRunning = true;
        try
        {
            var live = VirtualDesktop.GetDesktops();
            if (live.Count == 0 || (live.Count == 1 && live[0].Id == "default" && _initialDesktop != "default")) return;
            if (!_liveDesktops.SequenceEqual(live))
            {
                _liveDesktops = live;
                _matchPending = true;
                foreach (var window in _windows.Values)
                {
                    var desktop = live.FirstOrDefault(d => d.Id.Equals(window.DesktopGuid, StringComparison.OrdinalIgnoreCase));
                    if (desktop is not null) window.UpdateDesktopTitle(desktop.Name, desktop.Index);
                }
            }
            if (_matchPending && Options.CompatibilityExpected is null && Options.VisualOutputDirectory is null &&
                (await PendingMoveStore.GetPendingMovesAsync()).Count == 0)
                _matchPending = !await TryMatchDesktopsAsync(live);
        }
        catch (Exception ex) { LogService.Error("Failed to refresh virtual desktops", ex); }
        finally
        {
            _desktopRefreshRunning = false;
            foreach (var window in _windows.Values.ToArray()) window.CheckForDesktopMovement();
        }
    }

    private async Task RecoverPendingMovesAsync(MainWindow initialWindow)
    {
        try
        {
            await initialWindow.Ready;
            var pendingMoves = await PendingMoveStore.GetPendingMovesAsync();
            _pendingMoveReview.Clear();
            _pendingMoveReview.AddRange(pendingMoves);
            foreach (var window in _windows.Values) window.NotifyOrphansChanged();
            if (!_matchedEarly && Options.CompatibilityExpected is null && Options.VisualOutputDirectory is null &&
                CanMatchDesktops() && pendingMoves.Count == 0)
                _matchPending = !await TryMatchDesktopsAsync(_liveDesktops);
            foreach (var move in pendingMoves)
            {
                // The legacy row lacks the identities of moved notes. Reversing every target note
                // could move unrelated existing notes; retain the row for explicit recovery.
                LogService.Warn("Pending desktop move requires recovery: {WindowId}", move.WindowId);
            }
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); }
    }

    private static bool CanMatchDesktops() => _liveDesktops.Count > 0 &&
        !(_initialDesktop != "default" && _liveDesktops.Count == 1 && _liveDesktops[0].Id == "default");

    private void SetOrphans(IEnumerable<string> guids)
    {
        _orphans.Clear();
        foreach (var guid in guids) _orphans.Add(guid);
        foreach (var window in _windows.Values) window.NotifyOrphansChanged();
    }

    private async Task<bool> TryMatchDesktopsAsync(IReadOnlyList<VirtualDesktop.Desktop> desktops)
    {
        if (_desktopMatching || MainWindow.DesktopMoveCheckActive) return false;
        _desktopMatching = true;
        try
        {
            SetOrphans(await DesktopSessionMatcher.MatchAsync(desktops));
            return true;
        }
        finally { _desktopMatching = false; }
    }

    private static void AttachWindowPlacement(MainWindow window)
    {
        window.Opened += async (_, _) =>
        {
            var geometry = StartupSnapshot.GetGeometry(window.DesktopGuid);
            if (geometry is null)
            {
                await EnsureStorageAsync();
                geometry = await SessionStore.GetWindowGeometryAsync(window.DesktopGuid);
            }
            WindowPlacement.Restore(window, geometry);
        };
    }

    private static async Task SaveWindowGeometryAsync(MainWindow window)
    {
        if (Options.BenchmarkOutput is not null) return;
        var geometry = WindowPlacement.Capture(window);
        if (geometry is null) return;
        await SessionStore.SaveWindowGeometryAsync(window.DesktopGuid, geometry);
        StartupSnapshot.UpdateGeometry(window.DesktopGuid, geometry);
    }

    private async Task InitializeHotkeyAsync()
    {
        try
        {
            await EnsureStorageAsync();
            var modifiers = await PreferenceStore.GetPreferenceAsync("hotkey_modifiers");
            var key = await PreferenceStore.GetPreferenceAsync("hotkey_vk");
            _hotkeyModifiers = uint.TryParse(modifiers, out var m) ? m : 0x000C;
            _hotkeyKey = uint.TryParse(key, out var k) ? k : 0x4E;
            StartHotkey();
            if (_hotkey?.Registered != true)
                foreach (var window in _windows.Values) window.ShowGlobalHotkeyConflict();
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); }
    }

    private void StartHotkey()
    {
        _hotkey ??= new GlobalHotkey(_hotkeyModifiers, _hotkeyKey,
            () => Dispatcher.UIThread.Post(() =>
            {
                var guid = VirtualDesktop.GetCurrentDesktopOrDefault();
                if (_shuttingDown) return;
                if (_reservedDesktops.Contains(guid)) { _pendingActivations.Add(guid); return; }
                var target = GetOrCreateWindow(guid);
                target.Activate();
            }));
    }

    private void StopHotkey() { _hotkey?.Dispose(); _hotkey = null; }

    private async Task<bool> UpdateHotkeyAsync(Key key, KeyModifiers modifiers)
    {
        uint vk;
        if (key is >= Key.A and <= Key.Z) vk = (uint)(key - Key.A) + 0x41;
        else if (key is >= Key.D0 and <= Key.D9) vk = (uint)(key - Key.D0) + 0x30;
        else if (key is >= Key.F1 and <= Key.F24) vk = (uint)(key - Key.F1) + 0x70;
        else if (key is >= Key.NumPad0 and <= Key.NumPad9) vk = (uint)(key - Key.NumPad0) + 0x60;
        else vk = key.ToString() switch
        {
            "Back" => 0x08, "Tab" => 0x09, "Return" or "Enter" => 0x0D,
            "Pause" => 0x13, "Capital" or "CapsLock" => 0x14, "Escape" => 0x1B,
            "Space" => 0x20, "PageUp" or "Prior" => 0x21, "PageDown" or "Next" => 0x22,
            "End" => 0x23, "Home" => 0x24, "Left" => 0x25, "Up" => 0x26,
            "Right" => 0x27, "Down" => 0x28, "Insert" => 0x2D, "Delete" => 0x2E,
            "Multiply" => 0x6A, "Add" => 0x6B, "Separator" => 0x6C,
            "Subtract" => 0x6D, "Decimal" => 0x6E, "Divide" => 0x6F,
            "NumLock" => 0x90, "Scroll" => 0x91,
            "OemSemicolon" or "Oem1" => 0xBA, "OemPlus" => 0xBB,
            "OemComma" => 0xBC, "OemMinus" => 0xBD, "OemPeriod" => 0xBE,
            "OemQuestion" or "Oem2" => 0xBF, "OemTilde" or "Oem3" => 0xC0,
            "OemOpenBrackets" or "Oem4" => 0xDB, "OemPipe" or "Oem5" => 0xDC,
            "OemCloseBrackets" or "Oem6" => 0xDD, "OemQuotes" or "Oem7" => 0xDE,
            "Oem8" => 0xDF, "OemBackslash" or "Oem102" => 0xE2,
            _ => 0
        };
        if (vk == 0) return false;
        uint flags = 0;
        if (modifiers.HasFlag(KeyModifiers.Alt)) flags |= 1;
        if (modifiers.HasFlag(KeyModifiers.Control)) flags |= 2;
        if (modifiers.HasFlag(KeyModifiers.Shift)) flags |= 4;
        if (modifiers.HasFlag(KeyModifiers.Meta)) flags |= 8;
        var oldModifiers = _hotkeyModifiers;
        var oldKey = _hotkeyKey;
        StopHotkey();
        _hotkeyModifiers = flags;
        _hotkeyKey = vk;
        StartHotkey();
        if (_hotkey?.Registered != true)
        {
            StopHotkey();
            _hotkeyModifiers = oldModifiers;
            _hotkeyKey = oldKey;
            StartHotkey();
            return false;
        }
        await PreferenceStore.SetPreferenceAsync("hotkey_modifiers", flags.ToString());
        await PreferenceStore.SetPreferenceAsync("hotkey_vk", vk.ToString());
        return true;
    }

    private Task ExitAsync() => FlushAndShutdownAsync(false);

    private Task RestartForLanguageAsync() => FlushAndShutdownAsync(true);

    private async Task FlushAndShutdownAsync(bool restart)
    {
        if (_shuttingDown) return;
        if (_windows.Values.Any(window => window.HasShutdownBlockingInteraction))
            throw new InvalidOperationException("Close the open dialog before exiting or restarting JoJot.");
        _shuttingDown = true;
        var windows = _windows.Values.ToArray();
        var visibleWindows = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows
            .Select(window => (Window: window, window.IsEnabled)).ToArray() ?? [];
        foreach (var entry in visibleWindows) entry.Window.IsEnabled = false;
        try
        {
            if (_reservationsDrained is { } pendingMoves) await pendingMoves.Task;
            foreach (var window in windows)
                if (_windows.ContainsValue(window)) await window.FlushWindowAsync();
            RestartRequested = restart;
            (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        catch
        {
            foreach (var entry in visibleWindows) entry.Window.IsEnabled = entry.IsEnabled;
            _shuttingDown = false;
            DrainCommands();
            foreach (var guid in _pendingActivations.ToArray())
                if (!_reservedDesktops.Contains(guid) && _pendingActivations.Remove(guid))
                    GetOrCreateWindow(guid).Activate();
            throw;
        }
    }

    private void FlushForSessionEnd()
    {
        _shuttingDown = true;
        foreach (var window in _windows.Values.ToArray())
        {
            try
            {
                window.FlushForOsShutdown();
                var geometry = WindowPlacement.Capture(window);
                if (geometry is not null && _storageTask?.IsCompletedSuccessfully == true)
                {
                    SessionStore.SaveWindowGeometryAsync(window.DesktopGuid, geometry).GetAwaiter().GetResult();
                    StartupSnapshot.UpdateGeometry(window.DesktopGuid, geometry);
                }
            }
            catch (Exception ex) { LogService.Error("Failed to flush during session shutdown", ex); }
        }
    }
}
