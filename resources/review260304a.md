Manual review:

Bugs:
- only first note retains text, all others lose their text on any action such as tab navigation or font size change

Menu:
- do not show "Recover Sessions" if there aren't any sessions to recover

Font size resizer:
- Leftover label "Reset to 13pt" should just say "100%"
- The tab titles are tool big. Retain the original relative size (tabs a little bit smaller than editor content)
- The tab bottom dates should also scale with the resize

Preferences:
- Remove autosave delay
- Record global hotkey should temporarily disable the actual hotkey. When I record the same sequence, the window gets minimized

tabs:
- Add some leeway to the pin button press target (it's too precise). On hover, cross the pin icon.
- increase the size of X button to be the same as pin icon
- for unpinned items, add a pin button to the left of X button
- actually looking at the tab titles, the font sizes randomly change between the tabs.

drag and drop:
- the dragged tab should be set to invisible (blank empty space). A "ghost" should follow the cursor.
- do not show the new placement horizontal indicator lines for positions that wouldn't change the order (above and below the dragged item)

external files:
- dragging files from windows explorer to the window only works over the toolbar above the editor. It should work on the entire window. Dropped file should be to the first position (or first below pinned)

Recover sessions:
- this should become a side bar like preferences. It should explicitly state the last desktop name. Remove open button for now.

startup:
- automatically silently delete all empty notes on startup

move to another desktop:
- the card should show the source (original) desktop name
- if there's already a window active on the target desktop, do not show the "keep here" button


