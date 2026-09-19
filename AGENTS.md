# osXos working and release flows

## Scope and defaults

This repository is osXos, a cross-platform system-utility app built with Avalonia/.NET.
Preserve existing changes when working in a dirty checkout. Build scripts live at the
repository root; Forge is the sibling `../Forge` project. Cogas is the source of the
design language, not osXos's release target, and Hisashi is an optional integration
osXos publishes menus to, never a dependency.

osXos ships one binary per operating system from one codebase. Windows is verified
here; macOS and Linux are cross-compiled and have never been run on their own systems.
Do not describe them, or the menu bar on them, as verified.

## Commit messages

Use a normal commit title and body only. Do not add `Co-Authored-By` trailers or
AI/assistant attribution (including Claude or Codex) to commits or release notes.
For multiline messages or messages containing quotes, write a temporary UTF-8
message file and use `git commit -F <file>`. Check the exit code and verify the
resulting commit before tagging; PowerShell 5.1 can split inline quoted messages.

## RELEASE workflow

Only an explicit request to **RELEASE** triggers the complete publishing flow.
Ordinary fixes, builds, and installer requests do not imply a version bump,
commit, push, tag, or GitHub release. When RELEASE is requested, perform these
steps in order and stop/report any failure before proceeding:

1. Run `./version.ps1 -Bump patch` by default, or `-Set x.y.z` for a requested
   version. Clarify conflicting/ambiguous version instructions. The script keeps
   three locations synchronized: `<Version>` in `osXos.csproj`, the Forge `[app]`
   version, and the registry Version in `forge.toml`. The wizard strings use
   `${app.version}` and need no rewriting — if a literal version string is ever
   added to `forge.toml`, teach `version.ps1` about it rather than editing by hand.
   Run `./version.ps1` to verify.
2. Run `./build.ps1 -All` to test once and publish every release RID
   (`win-x64`, `osx-arm64`, `osx-x64`, `linux-x64`) into `dist/<rid>`, tarring the
   non-Windows ones into `dist/packages`. Do not skip tests for a release. Publishing
   closes any running osXos window, because a publish cannot overwrite a running exe
   and would otherwise leave a stale binary that looks like a successful build.
   Report this effect and respect authorization already given.
3. Run `./installer.ps1 -SkipBuild` immediately afterward. Forge requires sibling
   `../Forge/build/forge.exe` and `uninstall.exe`, produced by `gobake build` in
   Forge. The script re-verifies the version with `-RequireBuild`, which checks the
   published binary too — source files agreeing with each other says nothing about
   what the installed app will report. Verify
   `dist/installer/osXos-Setup-<version>.exe` and surface that exact path for
   testing. During RELEASE, launch this new Setup executable for the user to
   install/test; an existing installed copy remains old until Setup is run. Keep its
   option to launch the new app after installation enabled rather than restarting the
   old installed executable. Honor any requested test gate.
4. Review and commit the intended changes without attribution, using a message
   file. Verify the commit landed and record its hash before the next steps.
5. Push the commit to osXos's configured remote/release branch (normally
   `origin main`). Inspect `git remote -v` and the branch first. Never use Cogas's or
   Hisashi's remote, infer a missing remote, or force-push. The repository is
   public, so a push is visible immediately; never push work the user has not
   agreed to publish.
6. Create the matching `vX.Y.Z` tag on the verified commit, push it, and create a
   GitHub release with `gh release create vX.Y.Z`, attaching the matching
   `dist/installer/osXos-Setup-X.Y.Z.exe` and both
   `dist/packages/osXos-X.Y.Z-<rid>.tar.gz` archives. Use title `osXos vX.Y.Z`;
   notes start with `## osXos vX.Y.Z`, followed by `### ✨ <feature>` sections and
   bullets. Supply notes through `--notes-file` to avoid PowerShell multiline
   argument splitting. State plainly in the notes which platforms were actually
   verified. Verify the published assets.

## Build and installer maintenance

- Keep `forge.toml` on the Mica wizard theme, using osXos's icon and identity.
- Forge targets Windows. macOS and Linux ship as tarballs from `build.ps1 -Package`;
  never claim an installer for them.
- Preserve user data under `%APPDATA%/fezcode/osxos` unless the user chooses the
  installer's option to remove settings/data. The equivalent paths on the other two
  platforms are `~/Library/Application Support/fezcode/osxos` and
  `~/.config/fezcode/osxos`.
- Fail on inconsistent versions, failed tests, failed publishing, missing payload,
  or non-GUI Setup executables. Never report an old installer as a new success.
- Check GUI Forge process exit codes with `Start-Process -Wait -PassThru`.
  Quote arguments containing spaces and keep background build processes hidden.
- Use the Windows `System32\tar.exe` explicitly when packing, never bare `tar`:
  Git for Windows puts GNU tar on PATH, and GNU tar reads a `D:\...` argument as a
  remote host spec.
- Scope build cleanup and process shutdown to the selected repository output;
  preserve other installations, release installers, and unrelated `dist` files.

## Working in this codebase

- Adding a tool is one class implementing `ITool` plus one line in
  `Tools/ToolCatalog.cs`. `InspectAsync` must stay read-only; `RunAsync` only ever
  acts on a preview the user has already been shown.
- Every tool opens in the one shared `ToolWindow`, staged Explain → Review → Result.
  Do not give a tool its own view without a reason that survives being written down.
- Categories derive from the tools that exist. Never add a "no tools yet" placeholder.
- No elevation anywhere: no UAC, no sudo, no polkit. A job that genuinely needs root
  shows the command instead of asking for rights.
- The menu is described once in `Menus/AppMenuModel.cs` and translated by both
  bridges. Add rows there, never to a bridge.
- `Tools/` is source. Scripts live in `scripts/` because Windows treats `tools/` as
  the same directory; do not recreate `tools/`.
- Check design changes with `Tests/osXos.Shots`, which renders the real windows
  off-screen with Skia, rather than launching the app onto the user's desktop.
