<h1 align="center">binoc</h1>

<p align="center">
 <img src="./landing-icon.png" width="150"  />
</p>

<p align="center">
<strong>A close look android/apple app binaries - without the toolchain</strong>
</p>

<br>

binoc is a small Windows desktop app that inspects Android (`.apk`, `.aab`) and iOS (`.ipa`) binaries and
produces one **normalised report** across the categories that matter — identity, signing, provisioning,
code, obfuscation/optimisation, size, and security posture. Drag a file in (or pick one) and read the
result. No Android Studio, no Xcode, no `aapt2` / `apksigner` / `bundletool` / `codesign` — binoc parses
everything itself in managed .NET.

## What you can do with it

- **Drop in a build, get a report.** One window. Select or drag an `.apk`, `.aab` or `.ipa`; binoc detects
  the format, runs every applicable analyser, and renders a searchable report (Ctrl+F). Recent files stay
  in a sidebar.
- **See the identity and signing at a glance.** Package/bundle ID, versions, min/target SDK, debug vs
  release, cert fingerprints and validity, signing schemes — and, for AAB, the honest note that the signer
  is usually the *upload* key, not what Play ships.
- **Read real R8 optimisation metrics, not guesses.** For AABs built with recent AGP/R8, binoc reads
  Google Play's own source — `BUNDLE-METADATA/com.android.tools/r8.json` — and reports the obfuscation /
  optimisation / shrinking percentages, R8 full mode, repackaging, and (optimised) resource shrinking
  exactly as Play sees them. When the metadata isn't there, it says so rather than inventing a number.
- **Spot packers and protectors.** Concrete on-disk signatures for commercial protectors (so you know when
  static analysis of the DEX will be incomplete), not a hand-wavy confidence score.
- **Inspect iOS posture.** Provisioning type, entitlements, embedded profile details, Mach-O architectures,
  FairPlay encryption, hardening flags, and privacy strings.

---

## Building from source

You'll need the **.NET 10 SDK**.

```sh
dotnet run --project src/Binoc.App     # or: run.bat / run.sh
dotnet test binoc.slnx
```

## Layout

| Project | What it is |
| --- | --- |
| `src/Binoc.Core` | The engine: format detection, archive walk, per-category analysers, and the normalised report model + JSON. No UI, no shelling out, no third-party parsers — pure BCL, unit-tested. |
| `src/Binoc.App` | The Avalonia desktop app (code-first, Nord dark theme, Windows-first). |
| `tests/Binoc.Tests` | xUnit over Core against a fixture corpus of real/sample binaries. |

## Icons

The logo's single source of truth is [`binoc.svg`](binoc.svg). The raster assets — the window icon, the
`.exe` icon, and the header image above — are generated from it, so nothing shipping depends on an SVG
renderer:

```powershell
tools/gen-icons.ps1     # or: dotnet run --project tools/IconGen -c Release
```

Run that after editing `binoc.svg`, then commit the regenerated `src/Binoc.App/Assets/binoc.{png,ico}` and
`landing-icon.png`. The `tools/IconGen` project is deliberately kept out of `binoc.slnx`.

## Docs

- **[Implementation plan](docs/implementation-plan.md)** — architecture, milestones, and decisions.
- **[APK / AAB / IPA findings](docs/apk-aab-ipa-findings.md)** — the founding research: the shared category
  model, per-format notes, and how the R8 metrics are sourced.
