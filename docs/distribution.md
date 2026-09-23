# Distribution, releases & the changelog

How binoc ships, how a user installs it, and how the changelog flows from git history into the running app.
This mirrors [Perch's](https://github.com/ArcticGizmo/perch) proven setup, trimmed to binoc's Windows-only
single head (no `perch-hook`, no Supabase, no macOS build yet).

## The one-liner install

```powershell
irm https://raw.githubusercontent.com/ArcticGizmo/binoc/main/install.ps1 | iex
```

[`install.ps1`](../install.ps1) resolves the latest GitHub release, downloads `Binoc-win-Setup.exe`,
**verifies it against `SHA256SUMS.txt` before running**, then launches the Velopack installer (per-user, no
admin). Pin a version through the pipe:

```powershell
$env:BINOC_VERSION = '0.1.0'; irm https://raw.githubusercontent.com/ArcticGizmo/binoc/main/install.ps1 | iex
```

The script raises failures with `throw` (never `exit`, which would close the caller's shell) and is kept
**pure ASCII with no BOM** — Windows PowerShell 5.1 decodes it as the system codepage, and a stray em dash
would arrive as a curly quote that silently terminates a string. Keep it that way when editing.

## Cutting a release

A `v*` tag pushed to GitHub triggers [`.github/workflows/release.yml`](../.github/workflows/release.yml):

1. Publish a self-contained, single-file `binoc.exe` (`-r win-x64`), version stamped from the tag.
2. `vpk pack` (Velopack) → `Binoc-win-Setup.exe` + the update feed + a portable `.zip` in `releases/`.
3. Hash everything into `SHA256SUMS.txt` (the manifest `install.ps1` verifies against).
4. Publish the GitHub Release with auto-generated notes and all of `releases/*` attached.

To cut the same build locally (e.g. to smoke-test the installer), run [`publish.bat`](../publish.bat) — it
reads the version from the csproj (or takes one as an argument) and writes an identical `releases/` folder.

## Versioning & the changelog

- **The last git tag is the source of truth** for the version, not the csproj `<Version>` (which drifts).
- The [`bump-version`](../.claude/skills/bump-version/SKILL.md) skill (`/bump-version`) derives the next
  patch version from the last tag, bumps `src/Binoc.App/Binoc.App.csproj`, backfills any tagged-but-
  undocumented versions, and summarises everything since the last tag into a new [`CHANGELOG.md`](../CHANGELOG.md)
  section — in the deadpan house voice. It edits the two files and reports; it does not commit, tag or push.

## The changelog, embedded in the app

`CHANGELOG.md` is embedded into the app at build time as `Binoc.CHANGELOG.md`
([csproj](../src/Binoc.App/Binoc.App.csproj) `<EmbeddedResource>`), so the app renders the same text it ships
with — no network, no separate asset.

| Piece | Where | Job |
| --- | --- | --- |
| `ChangelogParser` | `src/Binoc.Core/Changelog/` | Splits the markdown into per-version sections; picks the ones between "last seen" and "current". Pure, unit-tested. |
| `ChangelogMarkdown` | `src/Binoc.App/Windows/` | Loads the embedded resource and renders the lightweight markdown into themed controls. |
| `ChangelogWindow` | `src/Binoc.App/Windows/` | The "what's new" window — post-update popup (a range, with a "don't show again") or the full history. |
| `AppSettings` | `src/Binoc.App/` | `%APPDATA%\binoc\settings.json`: the last-seen version + the suppress flag. Best-effort, local-only. |
| `AppInfo` | `src/Binoc.App/` | The running version (from the assembly) + the repo URL. |

**Behaviour:** on launch, `App` compares `AppInfo.Version` to the last-seen version in settings. On a genuine
update it pops `ChangelogWindow` with just the sections in between, then records the new version. A fresh
install shows nothing (no history to diff). The footer's "binoc vX.Y.Z · What's new" opens the full changelog
any time; the popup's "Don't show changelogs again" flips the suppress flag.

## Not yet wired

- **In-app updates** via Velopack `UpdateManager` (a "Check for updates" action) — the installer and update
  feed exist, but the in-app pull is still on the M7 list.
- **macOS head** — the pure-managed core keeps it cheap, but there's no mac build job or `.app`/DMG packaging.
