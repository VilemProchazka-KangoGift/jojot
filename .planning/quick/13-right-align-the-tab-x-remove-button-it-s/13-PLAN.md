---
phase: quick-13
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - JoJot/Views/MainWindow.xaml
autonomous: true
requirements: [QUICK-13]

must_haves:
  truths:
    - "The close (X) button's right edge visually aligns with the updated timestamp's right edge below it"
  artifacts:
    - path: "JoJot/Views/MainWindow.xaml"
      provides: "Tab item DataTemplate with right-aligned close button"
      contains: "CloseBtn"
  key_links: []
---

<objective>
Right-align the tab close (X) button so its right edge ends at the same vertical line as the "last updated" timestamp in the row below.

Purpose: Visual consistency in the tab item layout -- the X button and timestamp should share the same right margin.
Output: Updated TabItemTemplate in MainWindow.xaml
</objective>

<execution_context>
@./.claude/get-shit-done/workflows/execute-plan.md
@./.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@JoJot/Views/MainWindow.xaml (TabItemTemplate around lines 36-137)

<interfaces>
The TabItemTemplate layout is a Border with Padding="8,6,8,6" containing a 2-row Grid:

Row 0: Inner Grid with 3 columns (Col0=*, Col1=Auto, Col2=Auto)
  - Col0: TitleBlock (TextBlock, Star width)
  - Col1: PinBtn (Border 22x22, Auto width, Margin="0,0,4,0")
  - Col2: CloseBtn (Border 22x22, Auto width, Margin="4,0,0,0")
    - CloseIcon (TextBlock, FontSize="12", HorizontalAlignment="Center")

Row 1: Inner Grid (no columns defined)
  - CreatedDisplay TextBlock (HorizontalAlignment="Left")
  - UpdatedDisplay TextBlock (HorizontalAlignment="Right")

The close button X glyph is centered inside the 22x22 Border. Since the ~12px glyph is centered in 22px, there is ~5px of dead space on the right side of the close button. The updated timestamp text, by contrast, sits flush at the right edge. This creates a visual misalignment where the X glyph appears offset ~5px to the left of the timestamp's right edge.
</interfaces>
</context>

<tasks>

<task type="auto">
  <name>Task 1: Right-align close button glyph to match timestamp right edge</name>
  <files>JoJot/Views/MainWindow.xaml</files>
  <action>
In the TabItemTemplate (around line 87-99), adjust the CloseBtn Border and CloseIcon TextBlock so the visible X glyph's right edge aligns with the UpdatedDisplay timestamp's right edge below it.

Changes to make in `MainWindow.xaml`:

1. On the CloseBtn Border (line ~87-92): Remove the left margin and right-pad instead. Change `Margin="4,0,0,0"` to `Margin="4,0,-4,0"`. This shifts the close button 4px rightward so its content area extends to compensate for the centered icon offset. Actually, a cleaner approach:

   Change the CloseIcon TextBlock (line ~93-98) from `HorizontalAlignment="Center"` to `HorizontalAlignment="Right"` and add `Margin="0,0,1,0"` (1px right margin for optical alignment -- the glyph has internal padding).

   This keeps the 22x22 hit target unchanged but moves the visible X glyph to the right side of its container, aligning it with the timestamp text below.

2. Verify that the CloseBtn Border itself has no right margin (`Margin="4,0,0,0"` -- the 0 right margin is correct, the button border's right edge is already at the row's right edge).

The key constraint: do NOT change the Border size (22x22) or its Visibility/Opacity behavior -- only adjust the internal icon alignment to achieve visual right-alignment with the timestamp.
  </action>
  <verify>
    <automated>dotnet build JoJot/JoJot.slnx</automated>
  </verify>
  <done>The close (X) icon glyph is right-aligned within its 22x22 hit target, so its right edge visually aligns with the updated timestamp text's right edge in the row below. Build succeeds.</done>
</task>

<task type="checkpoint:human-verify" gate="blocking">
  <what-built>Adjusted the close button X icon alignment in the tab item template so it visually aligns with the updated timestamp below.</what-built>
  <how-to-verify>
    1. Run the app: `dotnet run --project JoJot/JoJot.csproj`
    2. Hover over any tab in the left panel to reveal the X close button
    3. Verify the right edge of the X icon aligns with the right edge of the "last updated" timestamp text below it
    4. Check both pinned and unpinned tabs
    5. Verify the X button is still easily clickable (hit target unchanged)
  </how-to-verify>
  <resume-signal>Type "approved" or describe what needs adjustment</resume-signal>
</task>

</tasks>

<verification>
- Build succeeds: `dotnet build JoJot/JoJot.slnx`
- Visual check: X button right edge aligns with timestamp right edge
</verification>

<success_criteria>
The tab close (X) button's visible glyph ends at the same vertical line as the "last updated" timestamp text in the row below, for both pinned and unpinned tabs.
</success_criteria>

<output>
After completion, create `.planning/quick/13-right-align-the-tab-x-remove-button-it-s/13-SUMMARY.md`
</output>
