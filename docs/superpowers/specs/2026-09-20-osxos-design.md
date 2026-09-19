# osXos — design

*2026-09-20*

A cross-platform system-utility app in the Cogas design language. One codebase, three
builds; each shows the tools and category names for the OS it is running on.

## Decisions taken before implementation

| Question | Decision |
|---|---|
| Three "versions" | One Avalonia project, published per RID. Per-OS categories chosen at runtime. |
| Category taxonomy | Task-based, six per OS, sharing a spine (Maintenance / shell / System / Network) and diverging at the end (Privacy + Developer on Windows and macOS; Packages + Services on Linux). |
| v1 scope | Shell, themes, settings, plus working tools in more than one category per OS. |
| Name | `osXos`, mixed case, set in Playfair in the sidebar where Cogas sets `COGAS`. |
| Destructive actions | Preview then confirm. Nothing changes until the user acts on a preview they have seen. |
| Per-tool explainer | A detached window per tool, staged Explain → Review → Result. |
| Menu bar | macOS uses the real system menu bar; Windows publishes to Hisashi over hoswl; Linux offers a DBus global menu. Toggleable on all three. |

## Architecture

### The tool contract

`ITool` (`Tools/ToolModel.cs`) is the whole extension point:

- metadata — `Id`, `Platform`, `Category`, `Name`, `Summary`, `IconKey`, `Warning`,
  `IsDestructive`
- `Steps` — the Explain stage
- `InspectAsync` — **read-only, always**; returns a `ToolPreview`
- `RunAsync(preview, ct)` — acts, and only on a preview the user has been shown

`ToolPreview` carries found items (label, detail, optional byte size), a summary, and an
optional `Blocker`. A blocker is how a tool says "systemd-resolved is not what is
resolving here" — Review shows the reason and disables the action, instead of running a
command that would do nothing and reporting success.

Adding a tool is one file plus one line in `ToolCatalog`.

### Testability

The two things that make the tools testable are deliberate:

- **Injected paths.** Every file-sweeping tool takes its directories in a constructor,
  defaulting to the real ones. `InspectAsync` is then tested against a temp directory.
- **`ShellCommand` = file + argument list, run through `IProcessRunner`.** Keeping argv
  structured rather than a command string means a test can assert the exact invocation
  without a process starting. This is the only way the macOS and Linux tools can be
  checked from a Windows machine, and it is why they are written this way.

`FileSweep` holds the shared find/size/delete logic, apart from any tool.

### Categories derive from tools

`CategoryCatalog` reserves six category names per OS. `ToolRegistry` renders only the
ones a tool claims. There are therefore no empty states in the app except a search that
matches nothing, and a category appears by itself when its first tool is added.

### No elevation

Every v1 tool works within the user's own account. This removes a whole subsystem — UAC
relaunch, sudo prompting, polkit — from v1. Where a job genuinely needs root (the
`mDNSResponder` half of the macOS DNS flush), the tool shows the command and says it will
not run it.

## The shell

Ported from Cogas essentially unchanged:

- `App.axaml` — the token brushes and every style class, minus the cover-grid styles and
  the storefront marks, plus osXos's own icons and a `Button.CardHit` for whole-card
  click targets.
- `MainWindow` — custom chrome, 240px dark sidebar, nested rounded canvas at 16px radius,
  48px top bar, `GridSplitter` with a remembered width.
- `SettingsView` — command bar with Save/Discard, gold unsaved-changes banner, card
  stream, and the navigation guard modal.
- `ThemeDefinition` / `ThemeManager` — nine palettes and seven font stacks, verbatim.

Adapted: the brand lockup, the nav list (categories, with count pills), the sidebar OS
badge strip where Cogas has its storefront spectrum, a top-bar search across all
categories, and the About panel.

`ToolWindow` is new but follows Cogas's dialog pattern exactly (plain `Window`,
`CanResize=False`, `CenterOwner`, Mica hint, `Auto,*,Auto` grid, `ShowDialog`). One window
class serves all tools; only the view model differs.

## The menu bar

`AppMenuModel` describes osXos as a menu once and answers every id once. Two bridges
translate from it — `NativeMenuBridge` to Avalonia's `NativeMenu`, `HisashiMenuBridge`
to hoswl nodes — and `MenuBarService` picks one per platform. Describing the menu twice
would guarantee the two drifted, which is the whole reason for the neutral tree.

The Tools menu is generated from the registry, so a new tool appears in the menu bar
with no menu code touched. A test walks the built tree and asserts every static id it
produces is one `Invoke` answers; a dead menu row is not noticed until a user clicks it.

`AppMenuNode.Shortcut` is a display hint that `NativeMenuBridge.TryGesture` converts to
a real `KeyGesture` on macOS, dropping anything that will not parse — a menu row with no
shortcut is fine, one with the wrong shortcut is a trap. The hints themselves are
platform-aware: Alt+F4 does not exist on macOS and Cmd does not exist on Windows.

Turning the setting off clears osXos's menus rather than tearing the bar down, so a Mac
falls back to the default menu bar Avalonia provides instead of having none.

## Storage

`SettingsData` as JSON under `SpecialFolder.ApplicationData/fezcode/osxos`, which resolves
correctly on all three platforms. No SQLite and no DPAPI — nothing here is secret, and a
file the user can read and edit is a feature in a utility.

Every failure mode (missing, unreadable, malformed, unwritable) falls back to defaults
rather than throwing.

## What is deliberately not here

- **Elevation.** See above.
- **macOS and Linux installers.** Forge targets Windows. Those builds ship as tarballs.
- **The other two categories per OS.** System, Privacy, Developer, Packages and Services
  are named in the taxonomy and will appear when a tool lands in them.

## Verification

- 141 unit tests: catalogue well-formedness across all three platforms, category
  derivation and ordering, search, file sweeping against temp directories (including a
  genuinely locked file), settings round-trip and corruption recovery, theme catalogue
  integrity, the exact argv of every shell-driven tool, and the menu tree — its shape,
  its platform-specific shortcut hints, the hoswl translation and gesture parsing.
- `Tests/osXos.Shots` renders the real windows off-screen with Skia, so the design was
  reviewed without a window appearing on the user's desktop.
- **The four Windows tools were verified on Windows 11. The eight macOS and Linux tools
  were not run end to end** — no such machine was available. This is stated in the README
  rather than left for someone to discover.
- The menu bar is built and converted for real on both paths (the harness constructs and
  walks the native menu; the hoswl translation is unit-tested), but **has not been seen
  in a macOS menu bar or a Linux global menu**, and Hisashi was not running here.
