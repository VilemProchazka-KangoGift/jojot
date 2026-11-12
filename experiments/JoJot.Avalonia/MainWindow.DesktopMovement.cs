using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using JoJot.AvaloniaExperiment.Platform;
using JoJot.Resources;
using JoJot.Services;

namespace JoJot.AvaloniaExperiment;

public sealed partial class MainWindow
{
    internal static Func<string, bool>? DesktopHasWindow { get; set; }
    internal static Action<string, string>? DesktopReparent { get; set; }
    internal static Func<string, Task>? DesktopReloadTarget { get; set; }
    internal static Action<MainWindow>? DesktopCloseSource { get; set; }
    internal static Func<string, bool>? DesktopReserveTarget { get; set; }
    internal static Action<string>? DesktopReleaseTarget { get; set; }
    internal static Func<bool>? DesktopShutdownPending { get; set; }
    internal static Func<Task>? DesktopRefreshPending { get; set; }
    internal static Func<bool>? DesktopMatchingInProgress { get; set; }
    private static int _activeDesktopMoveChecks;
    internal static bool DesktopMoveCheckActive => _activeDesktopMoveChecks > 0;
    internal static Func<nint, string, bool> DesktopMoveWindow { get; set; } = VirtualDesktop.TryMoveWindowToDesktop;
    private bool _desktopMoveChecking;
    private bool _desktopMoveInitialized;
    private Func<string, Task>? _desktopMoveTargetChanged;

    internal void InitializeDesktopMovement()
    {
        if (_desktopMoveInitialized) return;
        _desktopMoveInitialized = true;
        Activated += CheckDesktopMove;
        Closed += (_, _) => Activated -= CheckDesktopMove;
    }

    internal void CheckForDesktopMovement() => CheckDesktopMove(this, EventArgs.Empty);
    internal Task NotifyDesktopMoveTargetAsync(string target) => _desktopMoveTargetChanged?.Invoke(target) ?? Task.CompletedTask;

    private async void CheckDesktopMove(object? sender, EventArgs args)
    {
        if (!_ready || _closing || DesktopGuid == "default") return;
        if (_desktopMoveChecking)
        {
            try
            {
                var current = VirtualDesktop.GetWindowDesktopId(TryGetPlatformHandle()?.Handle ?? 0);
                if (current is not null && _desktopMoveTargetChanged is { } changed) await changed(current);
            }
            catch (Exception ex) { ReportError(ex); }
            return;
        }
        if (DesktopMatchingInProgress?.Invoke() == true) return;
        _desktopMoveChecking = true;
        _activeDesktopMoveChecks++;
        try
        {
            await Task.Delay(300);
            var handle = TryGetPlatformHandle()?.Handle ?? 0;
            var target = VirtualDesktop.GetWindowDesktopId(handle);
            if (target is null || target.Equals(DesktopGuid, StringComparison.OrdinalIgnoreCase)) return;
            await Task.Delay(200);
            var confirmed = VirtualDesktop.GetWindowDesktopId(handle);
            if (!string.Equals(target, confirmed, StringComparison.OrdinalIgnoreCase)) return;
            await ResolveDesktopMoveAsync(handle, target);
        }
        catch (Exception ex) { ReportError(ex); }
        finally
        {
            _desktopMoveChecking = false;
            _activeDesktopMoveChecks--;
        }
    }

    internal async Task ResolveDesktopMoveAsync(nint handle, string target)
    {
        var source = DesktopGuid;
        await FlushWindowAsync();
        if ((await PendingMoveStore.GetPendingMovesAsync()).Any(m => m.WindowId.Equals(source, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A previous desktop move needs review before another move can be resolved.");
        // A null destination marks an uncommitted Avalonia decision. WPF recovery skips it;
        // the target remains in memory until the atomic resolution commits and deletes the row.
        await PendingMoveStore.InsertPendingMoveAsync(source, source, null);
        if (DesktopRefreshPending is { } refreshPending) await refreshPending();
        var targetInfo = VirtualDesktop.GetDesktops().FirstOrDefault(d => d.Id.Equals(target, StringComparison.OrdinalIgnoreCase));
        var targetLabel = !string.IsNullOrEmpty(targetInfo?.Name) ? targetInfo.Name : targetInfo is null
            ? Strings.Drag_AnotherDesktop : string.Format(Strings.Drag_DesktopN, targetInfo.Index + 1);
        var message = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var hint = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var action = new Button();
        var back = new Button { Content = Strings.Drag_GoBack };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(action);
        buttons.Children.Add(back);
        var body = new StackPanel { Margin = new Thickness(18), Spacing = 12 };
        var targetText = new TextBlock { Text = string.Format(Strings.Drag_MovedTo, targetLabel) };
        body.Children.Add(targetText);
        body.Children.Add(message);
        body.Children.Add(hint);
        body.Children.Add(buttons);
        var dialog = new Window { Title = "JoJot", Content = body, SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        var resolved = false;
        var migrated = false;
        var reparented = false;
        bool? mergeChoice = null;
        var changing = false;
        var resolving = false;
        dialog.Closing += (_, e) => { if (!resolved && DesktopShutdownPending?.Invoke() != true) e.Cancel = true; };
        void UpdateChoice()
        {
            var conflict = DesktopHasWindow?.Invoke(target) == true;
            action.Content = conflict ? Strings.Drag_MergeNotes : Strings.Drag_KeepHere;
            message.Text = conflict ? Strings.Drag_ConflictMessage : Strings.Drag_KeepOrGoMessage;
        }
        UpdateChoice();
        _desktopMoveTargetChanged = async current =>
        {
            if (changing || resolving || resolved || migrated) return;
            changing = true;
            try
            {
                if (current.Equals(source, StringComparison.OrdinalIgnoreCase))
                {
                    await PendingMoveStore.DeletePendingMoveAsync(source);
                    resolved = true;
                    dialog.Close();
                    return;
                }
                if (!current.Equals(target, StringComparison.OrdinalIgnoreCase))
                {

                    target = current;
                    targetInfo = VirtualDesktop.GetDesktops().FirstOrDefault(d => d.Id.Equals(target, StringComparison.OrdinalIgnoreCase));
                    targetLabel = !string.IsNullOrEmpty(targetInfo?.Name) ? targetInfo.Name : targetInfo is null
                        ? Strings.Drag_AnotherDesktop : string.Format(Strings.Drag_DesktopN, targetInfo.Index + 1);
                    targetText.Text = string.Format(Strings.Drag_MovedTo, targetLabel);
                    hint.Text = "";
                    back.Content = Strings.Drag_GoBack;
                }
                UpdateChoice();
            }
            finally { changing = false; }
        };
        action.Click += async (_, _) =>
        {
            if (changing || resolving) return;
            resolving = true;
            var reserved = false;
            try
            {
                action.IsEnabled = back.IsEnabled = false;
                if (DesktopReserveTarget is { } reserve && !reserve(target)) return;
                reserved = true;
                mergeChoice ??= DesktopHasWindow?.Invoke(target) == true;
                if (!migrated)
                {
                    await DesktopMoveStore.ResolveAsync(source, target, mergeChoice == true, targetInfo?.Name, targetInfo?.Index);
                    migrated = true;
                }
                if (mergeChoice == true)
                {
                    if (DesktopReloadTarget is { } reload) await reload(target);
                    resolved = true;
                    dialog.Close();
                    DesktopCloseSource?.Invoke(this);
                    return;
                }
                if (!reparented)
                {
                    DesktopGuid = target;
                    DesktopReparent?.Invoke(source, target);
                    reparented = true;
                    UpdateDesktopTitle(targetInfo?.Name, targetInfo?.Index);
                }
                await ReloadNotesAsync();
                resolved = true;
                dialog.Close();
            }
            catch (Exception ex) { ReportError(ex); }
            finally
            {
                if (reserved) DesktopReleaseTarget?.Invoke(target);
                resolving = false;
                action.IsEnabled = back.IsEnabled = true;
            }
        };
        back.Click += async (_, _) =>
        {
            try
            {
                if (migrated)
                {
                    hint.Text = Strings.Drag_RetryHint;
                    return;
                }
                if (!DesktopMoveWindow(handle, source))
                {
                    back.Content = Strings.Drag_Retry;
                    hint.Text = Strings.Drag_RetryHint;
                    return;
                }
                await PendingMoveStore.DeletePendingMoveAsync(source);
                resolved = true;
                dialog.Close();
            }
            catch (Exception ex) { ReportError(ex); }
        };
        try { await dialog.ShowDialog(this); }
        finally
        {
            _desktopMoveTargetChanged = null;
            if (DesktopRefreshPending is { } refreshPendingAfterClose) await refreshPendingAfterClose();
        }
    }
}
