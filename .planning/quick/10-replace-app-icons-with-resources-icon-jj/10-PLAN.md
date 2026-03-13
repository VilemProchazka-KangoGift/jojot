---
phase: quick-10
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - JoJot/Assets/jojot.ico
  - JoJot/Views/MainWindow.xaml
autonomous: true
requirements: [QUICK-10]
must_haves:
  truths:
    - "The EXE file icon (in Explorer) shows the new JJ notepad icon"
    - "The window title bar icon shows the new JJ notepad icon"
    - "The Windows taskbar icon shows the new JJ notepad icon"
  artifacts:
    - path: "JoJot/Assets/jojot.ico"
      provides: "Multi-size ICO file generated from resources/icon-jj.png"
    - path: "JoJot/Views/MainWindow.xaml"
      provides: "Window Icon property referencing the ICO asset"
  key_links:
    - from: "JoJot/JoJot.csproj"
      to: "JoJot/Assets/jojot.ico"
      via: "ApplicationIcon property"
      pattern: "ApplicationIcon.*Assets.*jojot\\.ico"
    - from: "JoJot/Views/MainWindow.xaml"
      to: "JoJot/Assets/jojot.ico"
      via: "Window Icon attribute"
      pattern: 'Icon='
---

<objective>
Replace the application icon (EXE, title bar, and taskbar) with the new JJ notepad design from `resources/icon-jj.png`.

Purpose: The old icon (`resources/icon.png`) was a placeholder. The new `icon-jj.png` is the final branding for JoJot.
Output: Updated `.ico` file and window icon binding so all three icon surfaces use the new design.
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@resources/icon-jj.png (source PNG, 528x554 RGBA)
@JoJot/JoJot.csproj (ApplicationIcon property at line 25)
@JoJot/Views/MainWindow.xaml (Window element, no Icon property currently set)
@JoJot/Assets/jojot.ico (current ICO, 4 sizes: 16x16, 32x32, 48x48, 256x256)
</context>

<tasks>

<task type="auto">
  <name>Task 1: Generate multi-size ICO from icon-jj.png and set Window Icon</name>
  <files>JoJot/Assets/jojot.ico, JoJot/Views/MainWindow.xaml</files>
  <action>
1. Use Python (PIL/Pillow) to convert `resources/icon-jj.png` into `JoJot/Assets/jojot.ico` with four standard sizes: 16x16, 32x32, 48x48, 256x256. The source is 528x554 (not square), so first crop or pad to square before resizing. Best approach: pad the shorter dimension (width=528) to match the taller dimension (554) by adding equal transparent padding on left and right, creating a 554x554 square. Then resize to each target size using LANCZOS resampling. Save as ICO with all four sizes embedded. This overwrites the existing `JoJot/Assets/jojot.ico`.

   Python script:
   ```python
   from PIL import Image
   img = Image.open('resources/icon-jj.png').convert('RGBA')
   # Pad to square (554x554) with transparent pixels
   size = max(img.size)
   square = Image.new('RGBA', (size, size), (0, 0, 0, 0))
   offset = ((size - img.width) // 2, (size - img.height) // 2)
   square.paste(img, offset)
   # Generate ICO sizes
   sizes = [(16, 16), (32, 32), (48, 48), (256, 256)]
   square.save('JoJot/Assets/jojot.ico', format='ICO', sizes=sizes)
   ```

2. In `JoJot/Views/MainWindow.xaml`, add `Icon="Assets/jojot.ico"` to the `<Window>` element (line 1). This sets the WPF window icon for the title bar and taskbar. The `.csproj` ApplicationIcon already points to the same file for the EXE icon.

   The Window element opening tag should become:
   ```xml
   <Window x:Class="JoJot.MainWindow"
       ...
       Icon="Assets/jojot.ico"
   ```
   Add the `Icon` attribute after the existing attributes (e.g., after `AllowDrop="True"` on line 15, or alongside the other Window properties near `Title="JoJot"`).

3. Verify the ICO file has all 4 sizes embedded by reading it back with PIL.
  </action>
  <verify>
    <automated>cd P:/projects/JoJot && python3 -c "
from PIL import Image
import struct
with open('JoJot/Assets/jojot.ico', 'rb') as f:
    f.read(4)
    count = struct.unpack('<H', f.read(2))[0]
    assert count == 4, f'Expected 4 sizes, got {count}'
    print(f'ICO has {count} entries - OK')
" && dotnet build JoJot/JoJot.slnx --nologo -v q 2>&1 | tail -5</automated>
  </verify>
  <done>
    - `JoJot/Assets/jojot.ico` contains the new JJ icon at 16x16, 32x32, 48x48, 256x256
    - `MainWindow.xaml` Window element has `Icon="Assets/jojot.ico"` attribute
    - Solution builds without errors
  </done>
</task>

<task type="checkpoint:human-verify" gate="blocking">
  <what-built>Replaced all application icons (EXE, title bar, taskbar) with the new JJ notepad icon from resources/icon-jj.png</what-built>
  <how-to-verify>
    1. Run: `dotnet run --project JoJot/JoJot.csproj`
    2. Check the window title bar (top-left corner) shows the new blue JJ notepad icon
    3. Check the Windows taskbar shows the new icon
    4. Close the app, navigate to `JoJot/bin/Debug/net10.0-windows/JoJot.exe` in Explorer and verify the EXE file icon is the new design
    5. If the taskbar still shows the old icon, it may be cached by Windows -- unpin and re-pin, or check after a restart
  </how-to-verify>
  <resume-signal>Type "approved" or describe any issues with the icon appearance</resume-signal>
</task>

</tasks>

<verification>
- ICO file has 4 embedded sizes (16, 32, 48, 256)
- MainWindow.xaml has Icon attribute on Window element
- Project builds successfully
- Visual confirmation: title bar, taskbar, and EXE all show new icon
</verification>

<success_criteria>
All three icon surfaces (EXE in Explorer, window title bar, taskbar) display the new JJ notepad icon from resources/icon-jj.png.
</success_criteria>

<output>
After completion, create `.planning/quick/10-replace-app-icons-with-resources-icon-jj/10-SUMMARY.md`
</output>
