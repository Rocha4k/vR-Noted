<p align="center">
  <img src="img/vR-NotedGitHub.png" alt="NOTED" width="720">
</p>

# NOTED

Sticky notes for Windows. Light, always on top, with checkboxes you can actually tick.
Every note is a plain `.md` file in a normal folder.

No account and no sync service. If you delete the folder, the notes are gone. That's it.

> The app itself is in Portuguese: menus, tooltips, and the welcome note. This README is
> in English, so where it names a menu item the Portuguese label is given next to it.

## Why this exists

Windows Sticky Notes keeps its content in a SQLite file you are not supposed to touch,
wants a Microsoft account to sync, and does not understand markdown. Every alternative I
tried either pulled in Electron and 300 MB of RAM or turned into a full note-taking app
with a sidebar and a tag tree.

I wanted something that starts in under a second and writes files I can grep and commit.

## What it does

- Notes float above other windows and come back after a reboot in the same spot
- Markdown rendered in place, with checkboxes that respond to the mouse
- A formatting toolbar that appears when you select text
- Six colours and per-note opacity
- Tags, and reminders that fire once
- Global hotkeys for a new note and for search
- One `.md` file per note. Front matter holds the window state, the body holds the text

## Reading and writing

Every note has two modes.

**Reading** shows the rendered markdown, and the checkboxes are real controls, so ticking
one flips a single character in the source file. Clicking the body switches to writing,
with the caret at the end of the line you clicked. Clicking the empty space below the
text puts the caret at the end of the note.

**Writing** shows the raw text. `Esc`, `Ctrl+E`, or clicking away from the note takes you
back to reading. A note with no content opens straight in writing, which is what you want
after `Ctrl+Alt+N`.

Rendered: headings, **bold**, *italic*, ~~strikethrough~~, `inline code`, fenced blocks
with a language label, bullet lists, numbered lists, checkboxes, block quotes, horizontal
rules, and `[text](url)` links. `####` and deeper parse fine but render at body size, so
only the first three levels look different from each other.

## Formatting

Select text in writing mode and a small toolbar appears above the selection:

| Button | Action | Shortcut |
|---|---|---|
| B | bold | `Ctrl+B` |
| I | italic | `Ctrl+I` |
| S | strikethrough | `Ctrl+Shift+X` |
| `</>` | inline code | `Ctrl+Shift+C` |
| ▤ | fenced code block | `Ctrl+Shift+K` |
| ↗ | link | `Ctrl+K` |
| H | heading | `Ctrl+1`, `Ctrl+2`, `Ctrl+3` |
| • | bullet list | `Ctrl+Shift+L` |
| 1. | numbered list | `Ctrl+Shift+O` |
| ☑ | checkbox | `Ctrl+Enter` |
| ” | block quote | `Ctrl+Shift+Q` |
| ✕ | strip inline markers | |

Right-clicking inside the editor opens the same actions with names, plus cut, copy,
paste, select all, undo, and redo.

Every button toggles. Pressing bold on text that is already bold takes the asterisks
off, and it works whether the markers are inside your selection or hugging it from the
outside. The line actions apply to every line the selection touches, keep the
indentation, and replace whatever marker was there before, so a bullet becomes a
checkbox in one step. With nothing selected, the inline actions grab the word under the
caret.

Pressing `Enter` at the end of `3. item` gives you `4. `, and pressing it again on the
empty `4. ` clears the marker instead of dragging the list along forever. Numbering
restarts from 1 for the block you apply it to.

Every transformation goes through the selection rather than a direct assignment to the
text, so `Ctrl+Z` still walks back through your edits one step at a time.

`Ctrl+K` is a little smarter than the rest. If the selection looks like a URL it builds
`[](url)` and puts the caret where the label goes. Otherwise it uses the selection as the
label and pulls a URL off the clipboard if there is one there.

## Shortcuts

Global, work from any application:

| Shortcut | Action |
|---|---|
| `Ctrl+Alt+N` | new note |
| `Ctrl+Alt+F` | search every note |

If another program already owns one of those, NOTED says so with a tray balloon on
startup instead of failing silently.

In a note:

| Shortcut | Action |
|---|---|
| `Ctrl+E` | switch between reading and writing |
| `Esc` | back to reading |
| `Enter` or `Backspace` in reading mode | switch to writing |
| `Ctrl+T` | pin or unpin from the top |
| `Ctrl+W` | close the note, keeping it on disk |
| `Ctrl+S` | write to disk now instead of waiting for the debounce |
| `Ctrl+Shift+N` | new note in the same colour |
| double click the title bar | roll the note up to just its bar |

Typing any printable character in reading mode switches to writing and keeps the
character. It lands at the end of the note, wherever you happened to be looking.

In the search palette:

| Shortcut | Action |
|---|---|
| `Enter` | open the selected note |
| `Ctrl+Enter` | create a note from what you typed |
| `Up` / `Down` | move through results |
| `#tag` | filter by tag |
| `Esc` | close |

Tags typed as `#work` in the palette become real tags on the note that `Ctrl+Enter`
creates, rather than ending up in the body.

In the tray: left click opens search, middle click creates a note, right click opens the
menu.

## Naming a note

`...` → **Nome...** gives a note a name, which shows in its title bar, in the search
results, and on the taskbar button. Leave it empty and the title bar falls back to the
first line of text that is not a marker, updated as you type.

## Finding the app

The notes deliberately stay out of the taskbar and out of alt-tab, which leaves the tray
icon as the only way to reach the app. Windows 11 hides new tray icons by default, so on
a fresh install the app can look like it never started.

Two things address that. **Mostrar na barra de tarefas** in the tray menu puts every note
in the taskbar and in alt-tab, labelled with the note's name. And starting with every
note closed now reopens the most recently written one instead of leaving an empty screen.

To pull the tray icon out of the overflow: Settings → Personalization → Taskbar → Other
system tray icons → turn NOTED on. Or drag it out of the overflow panel onto the tray.

## The note menu

The `...` button on each note opens **Nome...**, colour, opacity, **Sempre por cima**
(pin on top), **Alerta** (reminder), **Tags**, **Copiar texto** (copy to the clipboard),
**Duplicar**, **Abrir pasta das notas** (open the notes folder), and **Apagar nota**
(delete, behind a yes/no prompt). Deleting is the only thing here that cannot be undone.

Reminder presets run from five minutes to tomorrow morning, or you can type a date.

When a reminder fires the note comes back to the front and its title bar flashes. It
fires once and the reminder is then cleared from the file. The flash itself leaves the
note's colour and pin state alone, which was not true in an earlier version and quietly
turned notes pink.

Reminders are driven by one timer armed for the next one due, so nothing polls. The timer
does wake every 12 hours for reminders further out than that, because `DispatcherTimer`
saturates on long intervals.

## Search

`Ctrl+Alt+F` opens a palette in the middle of the screen. It matches plain words against
the body and `#tag` tokens against tags, and both kinds can be mixed in one query.
Results show the tag list, the reminder, how many checkboxes are done, whether the note
is currently on screen, and its colour.

The order is by file write time as of startup, so new notes sit at the top and the rest
keep the order they had when the app launched. Editing an old note does not push it up
until the next start or a reload from disk.

## Where the notes live

`%APPDATA%\NOTED\notes\<id>.md`

```markdown
---
id: fbcc4d25c016
color: amber
pos: 701,359
size: 572,487
topmost: true
collapsed: false
rolled: false
opacity: 1
name: Shopping
tags: work, urgent
remind: 2026-08-25 09:00
---
# title

- [ ] to do
- [x] done
```

`collapsed` means the note is off screen but still saved. `rolled` means it is on screen
with only its title bar showing.

Edit the files by hand if you want. **Recarregar do disco** in the tray menu picks up
outside changes from a `git pull`, OneDrive, or another editor. Writes are atomic: NOTED
writes a `.tmp` next to the target and moves it over, so a crash mid-save cannot leave you
with half a note. Files are written with LF endings even though the editor produces CRLF.

## Performance

Measured on a Release build, clean start, one note open in reading mode:

| | |
|---|---|
| CPU while idle | 0 s across repeated 6 to 20 s windows |
| RAM (private) | ~69 MB |
| RAM (working set) | ~120 MB |
| Startup | under 1 s |

Occasional sub-second CPU blips show up. I couldn't reproduce them or tie them to any
project setting, so I'm calling it environment noise. With no reminder pending, nothing
in the app is running on a timer.

## Build and run

Needs the .NET 10 SDK on Windows.

```bash
dotnet run -c Release --project src/Noted/Noted.csproj
```

The executable lands in `src/Noted/bin/Release/net10.0-windows/NOTED.exe`.

There is no main window. The app lives in the tray, and **Iniciar com o Windows** in the
tray menu writes the usual `HKCU\...\Run` value.

## Architecture

```
src/Noted/
  App.xaml.cs                single instance, tray, global hotkeys, crash log
  Assets/noted.ico           app icon, also embedded so the tray can size it
  Interop/Native.cs          topmost, tool window, layered opacity, monitor bounds
  Interop/HotKeyManager.cs   message-only window that receives the hotkeys
  Models/Note.cs             the model and the colour palette
  Storage/NoteStore.cs       front matter parse and serialize, atomic writes
  Services/NoteManager.cs    owns notes and windows, saves on a 700 ms debounce
  Services/Settings.cs       the two app preferences, kept in HKCU\Software\NOTED
  Services/ReminderService   one timer armed for the next reminder due
  Markdown/Blocks.cs         the block and span types everything else is built on
  Markdown/MarkdownParser    text to blocks, carrying source offsets for the caret
  Markdown/MarkdownFormat    the selection transforms behind the toolbar
  Markdown/MarkdownView      blocks to WPF elements (a StackPanel, not a FlowDocument)
  UI/NoteWindow.xaml(.cs)    the note itself
  UI/FormatBar.cs            the toolbar that follows the selection
  UI/EditorMenu.cs           right click menu inside the editor
  UI/NoteMenu.cs             the per-note options menu
  UI/SearchWindow.xaml(.cs)  the search palette
  UI/Theme.xaml              the dark palette, menu and dialog styles
  UI/PromptWindow.cs         the dark dialogs: ask, confirm, alert
  UI/TrayIcon.cs             the tray icon and its menu
```

No third-party packages. The markdown parser is under 300 lines and covers the subset
that makes sense on a sticky note.

## Things that were harder than they looked

**`InvariantGlobalization` cannot be turned on.** It looks like a free win and it breaks
WPF instead: the caret asks for the keyboard culture (`pt-PT`, 0x0816) and the app dies
with `CultureNotFoundException` on the first `Focus()`.

**`WS_EX_LAYERED` only below 100% opacity.** Leave the layer on all the time and every
caret blink recomposites the whole window, which reads as idle CPU usage in Task Manager.

**Reasserting topmost needs a brake.** Without one, `Deactivated` calling `SetWindowPos`
feeds itself and burns 27% of a core. With a two-second brake it costs nothing.

**Win32 layered opacity, not `AllowsTransparency`.** WPF's `AllowsTransparency` drops the
window out of hardware acceleration.

**A `Run` is not a `Visual`.** Clicking rendered text hands you a
`System.Windows.Documents.Run`, which is a `FrameworkContentElement`. Passing it to
`VisualTreeHelper.GetParent` throws `InvalidOperationException` and used to kill the app
on every click. See `MarkdownView.ParentOf`.

**Bubbling, not tunnelling, for clicks on the body.** With `PreviewMouseLeftButtonUp` the
outer handler runs first and steals every click from the checkboxes. With
`MouseLeftButtonUp` the checkbox marks the event handled and the outer handler never sees
it.

**A `StackPanel` needs a non-null `Background`** before it will receive clicks on empty
space.

**Writing to `TextBox.Text` throws away the undo stack.** Anything that rewrites the text
has to go through `Select` plus `SelectedText`, which is the only path WPF records. Worth
knowing if you test this: the undo manager only attaches once the box is inside a window
that has been shown, so a headless `TextBox` reports `CanUndo == false` after an edit that
is perfectly undoable in the real app.

**`MinHeight` beats an explicit `Height`.** Rolling a note up to its bar used to set
`Height = 26` against a `MinHeight` of 60, so the window stuck at 60 and the real height
got overwritten on the way past. The fix swaps `ResizeMode` to `NoResize` first (the
resize frame counts towards the window height, so measuring before the swap leaves a strip
of empty note under the bar), then drops `MinHeight`, then measures. The expanded height
survives in `Note.H` because `Persist` skips writing it while the note is rolled.

**Leaving edit mode on lost focus needs a delay.** Menus and the formatting toolbar take
keyboard focus for an instant without the user having gone anywhere, so the decision is
posted to the dispatcher and re-checked once WPF has settled. Acting on the event directly
made right-clicking inside the editor snap the note back to reading mode.

**`WindowStyle="None"` plus a resizable window paints a white strip.** DWM draws its own
frame over the top of the window, six device pixels of `#F3F3F3` sitting above the title
bar. It is not visible in the XAML and layout cannot fix it. A `WindowChrome` with
`GlassFrameThickness="0"` removes it while `ResizeBorderThickness` keeps the edges
draggable. Found by sampling the pixel rows of a screenshot, not by eye.

**A submenu that flips left loses the mouse.** WPF's stock menu template pads its popup
to make room for a drop shadow. With the submenu on the right that padding faces away from
the parent and nobody notices. Near the right edge of a screen the submenu flips left and
the padding lands *between* the two menus, and a transparent region of a layered popup is
click-through, so the pointer falls out of the menu entirely and the submenu closes halfway
there. The fix is to carry no margin at all around the popup border, so the submenu sits
flush against its parent whichever way it opens.

**And the submenu popup has to be called `PART_Popup`.** `MenuItem` finds it by that name
to drive opening and closing. Naming it anything else costs you the mouse tracking in
*both* directions, which is a worse bug than the one being fixed, and it fails silently:
the menu still renders, still opens, and still closes at the wrong moment.

**No external parser.** Markdig does not render to WPF, so it would only hand over a tree
and the renderer would still have to be written. The parser here costs less than the
dependency would.

## Not supported

- Syntax highlighting inside code blocks. Code renders monospaced and uncoloured.
- Tables and images in the rendered view.
- Nested list rendering beyond a flat indent.
- Repeating reminders.

## Roadmap

- [ ] Syntax highlighting in code blocks
- [ ] Tables and images
- [ ] Configurable notes folder, so it can point at OneDrive or a git repo
- [ ] Repeating reminders
- [ ] Windows toast notifications instead of only flashing the note
- [ ] A file system watcher instead of the manual reload
- [ ] Self-contained publish with `PublishReadyToRun` for a faster cold start
- [ ] An English UI to match this README
