# Native interaction checks — 2026-11-12

These checks used isolated experiment databases and NativeAOT builds on Windows. They did not access installed JoJot notes. Desktop A was `85ad6151-cf19-4365-bec7-e3a3bd672b7d`; desktop B was `30515d2f-0cc5-4f1e-a469-43be358d833f`. The Windows desktop moves were initiated through the app's COM probe, followed by interactive use of the normal decision UI. This does not establish that manually dragging a window in Task View works.

- **File dialogs and saves:** Open cancellation restored editor focus. Ctrl+O imported a Unicode file; Ctrl+S saved `Native save verified — č Ω 🐇` as 38 bytes of UTF-8 with BOM. Save As produced the same bytes in `saved-as-check.txt`. Evidence: `.local/avalonia-native-checks/f21344e8a95e42788b9e662d10808f36/`.
- **Global shortcut and residency:** Recorded Ctrl+Alt+Shift+F11 in preferences, closed the window, then invoked the shortcut from Explorer. The original process reopened a focused editor; repeating the shortcut focused the same window without creating an extra tab. Exit left a clean-shutdown marker. The initial process-visibility check was corrected using an elevated process query. The Win modifier and its recording hook were not exercised by this interaction run.
- **Keep Here:** A probe-owned window moved from A to B; the decision updated the window title. The database retained the 470-character note on B and had no pending move. Graceful exit returned to A with exit code 0. Evidence: `.local/avalonia-native-checks/move-keep-d2306df7deaf4b21b26e6ee82ff060ef/`.
- **Go Back:** The decision returned the probe-owned window to A. Database counts were A: 1, B: 0, pending: 0; graceful exit returned code 0. Evidence: `.local/avalonia-native-checks/move-back-e91498edade94409b83d43e65c706a88/`.
- **Merge:** Two probe-owned windows were placed on A and B with distinct marker notes. Merge closed the source window and showed four notes in the target. Database counts were A: 0, B: 4, pending: 0; both markers remained intact. Graceful exit returned to A with exit code 0. Evidence: `.local/avalonia-native-checks/move-merge-165e3796f1364bbbb581c5f8b67fd54e/`.

The check used Windows keyboard input through Computer Use (SendInput) for the recorded shortcut and interactive controls for the move decisions. This exercised the registered global shortcut's keyboard and message path, but did not test physical keyboard hardware, a Win-key shortcut, manual Task View moves, OS shutdown, or monitor/DPI placement. Existing headless and synthetic results are separately described in [parity status](PARITY.md).

## Repeat the move probes

Build the current experiment as described in the [README](README.md). Use a fresh disposable data directory for each run and select a different existing desktop from `--desktop-probe`; the app rejects the current or nonexistent target. These commands switch the active desktop and move only the probe's own windows. The probe returns to the original desktop on graceful exit; Go Back also returns the view after its pending decision clears. Close the app through its Exit command to complete that restoration.

```powershell
$exe = '<native-executable>'
$target = '<different-existing-desktop-guid>'
& $exe --desktop-probe
& $exe --data-dir '<fresh-disposable-keep-or-back-directory>' --desktop-move-probe $target
& $exe --data-dir '<fresh-disposable-merge-directory>' --desktop-move-probe-merge $target
```

Choose Keep Here or Go Back for the first run, and Merge for the second. The merge probe creates its own destination window and adds distinct source and target marker notes. Probe output records desktop GUIDs and note counts before and after resolution; inspect the isolated database to verify the markers and pending-move rows. Re-run the first command with another fresh directory to exercise the other choice.
