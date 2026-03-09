using System.Windows;
using System.Windows.Media.Animation;
using JoJot.Services;

namespace JoJot;

public partial class MainWindow
{
    // ─── Window Drag Resolution ─────────

    /// <summary>
    /// Handles window drag detection from VirtualDesktopService.
    /// Only processes the event if this window's HWND matches the moved window.
    /// </summary>
    private void OnWindowMovedToDesktop(IntPtr movedHwnd, string fromGuid, string toGuid, string toName)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            var myHwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (myHwnd != movedHwnd) return;

            await ShowDragOverlayAsync(fromGuid, toGuid, toName);
        });
    }

    /// <summary>
    /// Shows the lock overlay for window drag resolution.
    /// Writes pending_moves row immediately, then configures buttons based on conflict type.
    /// </summary>
    private async Task ShowDragOverlayAsync(string fromGuid, string toGuid, string toName)
    {
        // Context-aware re-entry handling
        if (_isDragOverlayActive)
        {
            // Moved back to original desktop -- auto-dismiss
            if (toGuid.Equals(_dragFromDesktopGuid, StringComparison.OrdinalIgnoreCase))
            {
                await DatabaseService.DeletePendingMoveAsync(_desktopGuid);
                _isMisplaced = false;
                if (Title.Contains(" (misplaced)"))
                    Title = Title.Replace(" (misplaced)", "");
                await HideDragOverlayAsync();
                return;
            }
            // Same target desktop -- no-op
            if (toGuid.Equals(_dragToDesktopGuid, StringComparison.OrdinalIgnoreCase))
                return;
            // Different target desktop -- update overlay in-place (fall through)
            _dragToDesktopGuid = toGuid;
            _dragToDesktopName = toName;
            // Update pending_moves to new target
            await DatabaseService.DeletePendingMoveAsync(_desktopGuid);
            await DatabaseService.InsertPendingMoveAsync(_desktopGuid, _dragFromDesktopGuid!, toGuid);
            // Fall through to update UI below
        }
        else
        {
            _isDragOverlayActive = true;
            _dragFromDesktopGuid = fromGuid;
            _dragToDesktopGuid = toGuid;
            _dragToDesktopName = toName;

            // Flush unsaved content before entering drag state
            await _autosaveService.FlushAsync();

            // Write pending_moves row immediately
            await DatabaseService.InsertPendingMoveAsync(_desktopGuid, fromGuid, toGuid);
        }

        // Determine if target desktop has an existing JoJot session
        var app = System.Windows.Application.Current as App;
        bool targetHasSession = app?.HasWindowForDesktop(toGuid) ?? false;

        // Show source desktop name from live COM (not stale DB)
        string sourceLabel;
        try
        {
            // Use live COM data, not stale DB
            var sourceDesktops = VirtualDesktopService.GetAllDesktops();
            var sourceDesktop = sourceDesktops.FirstOrDefault(d =>
                d.Id.ToString().Equals(_desktopGuid, StringComparison.OrdinalIgnoreCase));
            if (sourceDesktop is not null && !string.IsNullOrEmpty(sourceDesktop.Name))
            {
                sourceLabel = sourceDesktop.Name;
            }
            else if (sourceDesktop is not null)
            {
                sourceLabel = $"Desktop {sourceDesktop.Index + 1}";
            }
            else
            {
                sourceLabel = "Unknown desktop";
            }
        }
        catch
        {
            sourceLabel = "Unknown desktop"; // best-effort
        }
        DragOverlaySourceName.Text = $"From: {sourceLabel}";

        // Configure overlay content with name fallback
        string displayName;
        if (!string.IsNullOrEmpty(toName))
        {
            displayName = toName;
        }
        else
        {
            var targetDesktops = VirtualDesktopService.GetAllDesktops();
            var targetDesktop = targetDesktops.FirstOrDefault(d =>
                d.Id.ToString().Equals(toGuid, StringComparison.OrdinalIgnoreCase));
            displayName = targetDesktop is not null
                ? $"Desktop {targetDesktop.Index + 1}"
                : "another desktop";
        }
        DragOverlayTitle.Text = $"Moved to {displayName}";

        if (targetHasSession)
        {
            DragOverlayMessage.Text = "This desktop already has a JoJot window. What would you like to do?";
            DragMergeBtn.Visibility = Visibility.Visible;
            DragKeepHereBtn.Visibility = Visibility.Collapsed; // Hide "keep here" — target already has window
        }
        else
        {
            DragOverlayMessage.Text = "Keep your notes on this desktop, or go back?";
            DragMergeBtn.Visibility = Visibility.Collapsed;
            DragKeepHereBtn.Visibility = Visibility.Visible; // Show "keep here" — no conflict
        }

        // Reset cancel failure state
        DragCancelBtn.Content = "Go back";
        DragCancelFailureText.Visibility = Visibility.Collapsed;

        // Show with 150ms fade-in animation
        DragOverlay.Opacity = 0;
        DragOverlay.Visibility = Visibility.Visible;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseOut
            }
        };
        DragOverlay.BeginAnimation(OpacityProperty, fadeIn);
    }

    /// <summary>
    /// Reparent — re-scope window and all notes to the new desktop.
    /// </summary>
    private async void DragKeepHere_Click(object sender, RoutedEventArgs e)
    {
        try
        {
        if (_dragToDesktopGuid is null) return;

        string oldGuid = _desktopGuid;
        string newGuid = _dragToDesktopGuid;

        // Re-check at click time: another window may have been created for the target
        // desktop (via IPC) while the overlay was showing.
        var app = System.Windows.Application.Current as App;
        if (app?.HasWindowForDesktop(newGuid) == true)
        {
            // Refresh overlay with keep-here hidden and merge visible
            DragOverlayMessage.Text = "Another window was opened on this desktop. You can merge or go back.";
            DragKeepHereBtn.Visibility = Visibility.Collapsed;
            DragMergeBtn.Visibility = Visibility.Visible;
            return;
        }

        // Update notes in database to new desktop
        await DatabaseService.MigrateNotesDesktopGuidAsync(oldGuid, newGuid);

        // Update this window's desktop GUID
        _desktopGuid = newGuid;

        // Update window registry in App (reuse app from guard above)
        app ??= System.Windows.Application.Current as App;
        app?.ReparentWindow(oldGuid, newGuid);

        // Update window title to new desktop name (use fresh COM name, not stale _dragToDesktopName)
        var desktops = VirtualDesktopService.GetAllDesktops();
        var targetInfo = desktops.FirstOrDefault(d =>
            d.Id.ToString().Equals(newGuid, StringComparison.OrdinalIgnoreCase));
        string name = targetInfo?.Name ?? _dragToDesktopName ?? "";
        UpdateDesktopTitle(name, targetInfo?.Index);

        // Update app_state session with full metadata (guid + name + index)
        string targetName = targetInfo?.Name ?? name;
        int? targetIndex = targetInfo?.Index;
        await DatabaseService.UpdateSessionDesktopAsync(oldGuid, newGuid, targetName, targetIndex);

        // Clear pending move
        await DatabaseService.DeletePendingMoveAsync(oldGuid);

        // Clear misplaced state
        _isMisplaced = false;

        // Hide overlay with fade-out
        await HideDragOverlayAsync();

        LogService.Info("Reparented window from {OldGuid} to {NewGuid}", oldGuid, newGuid);
        }
        catch (Exception ex)
        {
            LogService.Warn("DragKeepHere failed: {ErrorMessage}", ex.Message);
        }
    }

    /// <summary>
    /// Merge — append tabs to existing window on target desktop, close this window.
    /// </summary>
    private async void DragMerge_Click(object sender, RoutedEventArgs e)
    {
        try
        {
        if (_dragToDesktopGuid is null || _dragFromDesktopGuid is null) return;

        string sourceGuid = _desktopGuid;
        string targetGuid = _dragToDesktopGuid;

        // Migrate tabs preserving pin state (unlike orphan recovery which unpins)
        await DatabaseService.MigrateTabsPreservePinsAsync(sourceGuid, targetGuid);

        // Clear pending move
        await DatabaseService.DeletePendingMoveAsync(sourceGuid);

        // Notify target window to reload tabs
        var app = System.Windows.Application.Current as App;
        app?.ReloadWindowTabs(targetGuid);

        // Show toast on target window
        int tabCount = _tabs.Count;
        string fromName = Title.Replace("JoJot \u2014 ", "").Replace(" (misplaced)", "");
        app?.ShowMergeToast(targetGuid, tabCount, fromName);

        // Hide overlay and close this window
        _isDragOverlayActive = false;
        DragOverlay.Visibility = Visibility.Collapsed;

        // Unsubscribe from events before closing
        VirtualDesktopService.WindowMovedToDesktop -= OnWindowMovedToDesktop;

        FlushAndClose();

        LogService.Info("Merged {TabCount} tabs from {SourceGuid} to {TargetGuid}", tabCount, sourceGuid, targetGuid);
        }
        catch (Exception ex)
        {
            LogService.Warn("DragMerge failed: {ErrorMessage}", ex.Message);
        }
    }

    /// <summary>
    /// Cancel — move window back to original desktop.
    /// On failure, replace Go back with Retry + instruction text.
    /// </summary>
    private async void DragCancel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_dragFromDesktopGuid is null) return;

            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            bool success = VirtualDesktopService.TryMoveWindowToDesktop(hwnd, _dragFromDesktopGuid);

            if (success)
            {
                // Clear pending move
                await DatabaseService.DeletePendingMoveAsync(_desktopGuid);
                _isMisplaced = false;

                // Remove "(misplaced)" badge from title
                if (Title.Contains(" (misplaced)"))
                {
                    Title = Title.Replace(" (misplaced)", "");
                }

                // Hide overlay with fade-out
                await HideDragOverlayAsync();

                LogService.Info("Cancel: moved window back to {DesktopGuid}", _dragFromDesktopGuid);
            }
            else
            {
                // Cancel failed — show retry + instruction
                DragCancelBtn.Content = "Retry";
                DragCancelFailureText.Visibility = Visibility.Visible;

                LogService.Warn("Cancel failed: could not move window back to {DesktopGuid}", _dragFromDesktopGuid);
            }
        }
        catch (Exception ex)
        {
            LogService.Warn("DragCancel failed: {ErrorMessage}", ex.Message);
        }
    }

    /// <summary>
    /// Fades out the drag overlay over 150ms, then collapses it and resets state.
    /// </summary>
    private async Task HideDragOverlayAsync()
    {
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseIn
            }
        };

        var tcs = new TaskCompletionSource<bool>();
        fadeOut.Completed += (_, _) => tcs.SetResult(true);
        DragOverlay.BeginAnimation(OpacityProperty, fadeOut);
        await tcs.Task;

        DragOverlay.Visibility = Visibility.Collapsed;
        _isDragOverlayActive = false;
        _dragFromDesktopGuid = null;
        _dragToDesktopGuid = null;
        _dragToDesktopName = null;
    }

    /// <summary>
    /// When a misplaced window gains focus, auto-show the lock overlay.
    /// A window is misplaced when its stored desktop GUID doesn't match the desktop
    /// it's currently on (detected via COM).
    /// Debounces with 300ms delay to let COM state settle during rapid desktop switching.
    /// </summary>
    private async void OnWindowActivated_CheckMisplaced(object? sender, EventArgs e)
    {
        if (!VirtualDesktopService.IsAvailable) return;

        // Cancel any pending check — rapid desktop switches fire Activated multiple times
        _misplacedCheckCts?.Cancel();
        var cts = new System.Threading.CancellationTokenSource();
        _misplacedCheckCts = cts;

        try
        {
            // Let COM state settle before querying
            await Task.Delay(300, cts.Token);
        }
        catch (TaskCanceledException)
        {
            return; // Superseded by a newer Activated event
        }

        if (cts.Token.IsCancellationRequested) return;

        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            Guid currentDesktop = Interop.VirtualDesktopInterop.GetWindowDesktopId(hwnd);
            string currentGuid = currentDesktop.ToString();

            // Double-check after a short pause to confirm it's not a transient COM glitch
            if (!currentGuid.Equals(_desktopGuid, StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(200, cts.Token);
                if (cts.Token.IsCancellationRequested) return;

                // Re-query to confirm the mismatch is stable
                Guid confirmDesktop = Interop.VirtualDesktopInterop.GetWindowDesktopId(hwnd);
                string confirmGuid = confirmDesktop.ToString();
                if (confirmGuid.Equals(_desktopGuid, StringComparison.OrdinalIgnoreCase))
                {
                    // Transient mismatch — COM returned stale data
                    LogService.Info("Misplaced check: transient mismatch resolved (was {CurrentGuid}, now correct)", currentGuid);
                    return;
                }
                currentGuid = confirmGuid;

                if (!_isMisplaced)
                {
                    _isMisplaced = true;
                    string currentTitle = Title;
                    if (!currentTitle.Contains("(misplaced)"))
                    {
                        Title = currentTitle + " (misplaced)";
                    }
                }

                // Auto-show lock overlay
                string toName = "";
                var desktops = VirtualDesktopService.GetAllDesktops();
                var targetInfo = desktops.FirstOrDefault(d =>
                    d.Id.ToString().Equals(currentGuid, StringComparison.OrdinalIgnoreCase));
                toName = targetInfo?.Name ?? "";

                await ShowDragOverlayAsync(_desktopGuid, currentGuid, toName);
            }
            else if (_isMisplaced)
            {
                // Window is now on correct desktop — clear misplaced state
                _isMisplaced = false;
                string currentTitle = Title;
                if (currentTitle.Contains(" (misplaced)"))
                {
                    Title = currentTitle.Replace(" (misplaced)", "");
                }

                // Dismiss the move overlay if it's still showing
                if (_isDragOverlayActive)
                {
                    await DatabaseService.DeletePendingMoveAsync(_desktopGuid);
                    await HideDragOverlayAsync();
                }
            }
        }
        catch (TaskCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            LogService.Warn("Misplaced check failed: {ErrorMessage}", ex.Message);
        }
    }
}
