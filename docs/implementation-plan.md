# binoc — Phased Implementation Plan

A milestone-based plan to build **binoc**: an Avalonia desktop app that inspects
Android (`.apk`, `.aab`) and iOS (`.ipa`) binaries and produces a normalised
report across the 8 shared categories identified in the founding research.

Companion to [`apk-aab-ipa-findings.md`](./apk-aab-ipa-findings.md). Read that first —
this plan assumes its 8 categories, format notes, and tiering decisions.

Date: 2026-09-22

---

## 1. Product shape (the two hard requirements)

1. **Avalonia desktop application**, following the conventions of the sibling
   **Perch** app (`../perch`):
   - Code-first Avalonia (windows/controls built in C#, no per-window XAML).
   - Avalonia **12.0.5**, **.NET 10**, Fluent base theme + Inter fonts.
   - A static `Palette` façade over a single `Theme` — but binoc uses **one fixed
     theme: Nord (dark)**. No theme designer, no theme switching. (Perch imports
     Nord via `ArcticGizmo.Avalonia.Palette.Core`; binoc can hardcode the Nord
     values directly — see §4.)
   - Reuse Perch's `SettingsUi`-style helper vocabulary (`SectionTitle`,
     `BodyText`, `DividerRow`, `Separator`, `FlatButton`) so the surface feels
     like a Quartex-family app.
2. **Dead-simple interaction:** one window. **Select a file** (button → file
   picker) **or drag a file in**. binoc detects the format, runs the analysis,
   and renders a **report** of the relevant information. Nothing else on the
   critical path.

Everything below serves those two requirements.

---

## 2. Architecture

Three projects in one solution (`binoc.slnx`), mirroring Perch's Core/App split.

| Project | Purpose | Depends on |
|---|---|---|
| **Binoc.Core** | All analysis. Format detection, archive walk, per-category analysers, the **normalised report model** + JSON serialisation. **No Avalonia, no UI.** Pure, headless, unit-testable. | — |
| **Binoc.App** | Avalonia desktop head. The single window, drag/drop + file picker, Nord `Palette`, report rendering, export. | Binoc.Core |
| **Binoc.Tests** | xUnit tests over Binoc.Core against a fixture corpus of real/sample binaries. | Binoc.Core |

A **Binoc.Cli** head (headless `binoc <file> --json`) is an optional later add —
the Core/UI split means it's nearly free once Core is done (see M7).

### Key architectural decisions

- **D1 — Pure managed parsing, minimal dependencies, no shelling out.** The
  findings doc lists `aapt2`, `apksigner`, `bundletool`, `codesign`, `otool`,
  etc. as tools to "shell out to." **binoc will not** — and it also avoids
  third-party *parsing* NuGet packages. We parse everything in managed .NET,
  leaning on the BCL and hand-rolled readers scoped to only the fields we need:
  - ZIP: `System.IO.Compression` (BCL)
  - Binary `AndroidManifest.xml` (AXML): small custom decoder
  - AAB protobuf manifests / `BundleConfig.pb`: a **minimal protobuf wire-format
    reader** (varint + length-delimited fields) hand-written for the handful of
    messages we read — **not** `Google.Protobuf` + the bundletool `.proto` set
  - X.509 / APK v2/v3 signing block / CMS `embedded.mobileprovision`:
    `System.Security.Cryptography` (`X509Certificates`, `Pkcs`) — BCL, no OpenSSL
  - `Info.plist`: XML plists via `System.Xml`; binary plists (`bplist00`) via a
    small custom reader — **no** `Claunia.PropertyList`
  - Mach-O / ELF / DEX: custom binary readers (headers + the specific tables we
    report) — **no** `ELFSharp` or similar

  **Net effect:** the only runtime NuGet dependencies are **Avalonia** (+ Inter
  font) for the UI and **Velopack** for install/update. Everything format-related
  is our own code in Binoc.Core.

  **Why:** (1) the target is a Windows endpoint under CrowdStrike + Defender where
  launching external toolchains is fragile and, per the host rules, actively
  discouraged; (2) fewer dependencies means less supply-chain surface, no version
  churn, and nothing to install alongside the app. Pure managed keeps binoc
  offline, portable (the same Core serves a future macOS head), and reproducible.
  Real toolchains (`apksigner`, `bundletool`, …) stay as an **optional, opt-in
  validation harness in tests only** — never referenced on the runtime path or in
  the shipped app.
- **D2 — One normalised report model.** `AnalysisReport` is format-agnostic:
  the 8 categories are shared fields; format-specific data hangs off typed
  sub-objects (`AndroidDetails`, `IosDetails`). One JSON schema across all three
  formats (findings §4) so downstream diffing/CI is format-agnostic.
- **D3 — Analysers are a pipeline of independent passes.** Each analyser
  (`IAnalyzer`) takes the opened container + accumulating report and fills its
  slice. Passes are **tiered** (findings §5): Tier-1 (archive/manifest/signature)
  always runs; heavier passes (DEX/Mach-O/ELF deep, per-device sizing) are
  separate passes that can be gated/parallelised later. A failing analyser
  degrades gracefully — it records a category-level error and the rest still run.
- **D4 — Provenance & confidence are first-class.** Per findings §5: every
  timestamp carries its source label; obfuscation/packing emits a confidence
  score, never a bare boolean; AAB signing explicitly flags "upload key, not
  distribution key." These are model fields, not afterthoughts.

### Report model sketch (Binoc.Core)

```
AnalysisReport
  Format            : Apk | Aab | Ipa
  Identity          : package/bundle id, version+build, min/target SDK,
                      debug-vs-release, build time (+ provenance label)
  Signing           : signers, cert fingerprints+validity, schemes, debug-key flag,
                      (AAB) UploadKeyNotDistribution flag
  Provisioning?     : iOS only — profile type, team, entitlements, UDIDs, get-task-allow
  Code?             : DEX count/method/class counts vs 64K (Android) | Mach-O summary (iOS)
  Obfuscation       : { assessment, ConfidenceScore, signals[] }
  SizeBreakdown     : per-type sizes, alignment, (AAB) per-device estimate
  NativeLibs        : ABIs/archs, strip status, checksec (PIE/canary/RELRO/NX | encryption)
  SecurityPosture   : dangerous perms, exported components, cleartext/ATS, secrets, privacy manifest
  Warnings/Errors   : per-category degradation notes
  AndroidDetails? / IosDetails?  : format-specific extras
```

---

## 3. Milestones

Each milestone is a **shippable increment** with explicit exit criteria. M0–M2
deliver a genuinely useful tool (the "80% cheap wins" from findings §core-insight);
M3+ add depth.

### M0 — Foundations & the empty shell  ✅ done
*Goal: a runnable Nord-dark window and a testable Core skeleton. No analysis yet.*

- Solution + three projects (§2), CI-less local build (`run.bat`/`run.sh` like Perch).
- `Binoc.App`: single main window, Fluent + Inter, the Nord `Palette` (§4),
  Perch-style `BinocUi` helper vocabulary.
- Window shows an **empty drop zone** ("Drop an APK, AAB or IPA here, or choose a
  file") with a **Choose file…** button wired to `StorageProvider`.
- Drag/drop + picker both resolve to a `string path` and call a stub analyser
  that returns a placeholder report.
- `Binoc.Core`: `AnalysisReport` model + `Format` detection (magic bytes / ZIP
  central-directory sniff, not just extension) + `IAnalyzer` pipeline contract.
- `Binoc.Tests` wired up with a tiny fixture (one minimal ZIP).
- **Velopack wired from the start:** `VelopackApp.Build().Run()` as the first
  statement in `Main` (a no-op until installed, but it must be first so the
  install/update hooks fire). Actual packaging + the installer come at M7.

**Exit:** app launches, Nord dark, drag a file in → placeholder report renders;
format detection unit-tested for apk/aab/ipa/unknown.

### M1 — Archive walk + Identity (first real report, all 3 formats)  ✅ done
*Goal: the cheapest, most universal answers, end to end.*

- **Archive/size walk** (all 3): entry list, compressed/uncompressed sizes,
  per-type breakdown (dex/res/native/assets vs Mach-O/frameworks), zip-alignment
  probe.
- **Identity**:
  - APK: AXML `AndroidManifest.xml` decoder → package, versionCode/Name,
    min/target SDK, `debuggable`.
  - AAB: protobuf base-module manifest → same fields (note per-module structure).
  - IPA: `Info.plist` → bundle id, versions, `MinimumOSVersion`, device family,
    `DTSDKName`/`DTXcode` provenance.
- **Report UI**: replace placeholder with the real **category-card layout** —
  a scrolling column of collapsible cards, one per populated category, Nord-styled.
- Timestamp provenance labelling (D4) lands here.

**Exit:** drop any of the three formats → correct Identity + archive breakdown,
with a screenshot in `./captures/`. Golden-file tests for each format's Identity.

### M2 — Signing & certificates (all 3 formats)  ✅ done
*Goal: "who signed this, and is it trustworthy?" — the highest-value security answer.*

- APK: detect scheme set (v1 JAR / v2 / v3 / v3.1 rotation / v4); parse the v2/v3
  signing block; cert chain, SHA-1/256 fingerprints, validity; **debug-key flag**.
- AAB: signer parse **+ the explicit "upload key ≠ distribution key" warning** (D4).
- IPA: `_CodeSignature`, signing identity + certs, validity.
- Signing card in the report, with trust flags surfaced prominently (debug key,
  expired cert, upload-key caveat).

**Exit:** signer + fingerprints + scheme set shown for all three; debug-key and
expired-cert fixtures assert the right flags. **This is the first "release-worthy" cut.**

### M3 — Format deep-dives (Code + Native libs)  ✅ done
*Goal: the format-specific heavy passes. Split into two parallel tracks.*

- **iOS track:** embedded provisioning profile (CMS/PKCS#7 → XML plist) →
  distribution type (dev/ad-hoc/enterprise/App Store), team, entitlements,
  provisioned UDIDs, `get-task-allow`. Mach-O: architectures (arm64/arm64e, fat
  binary), FairPlay `cryptid`, PIE, ARC, stack canary, strip status, linked
  dylibs/frameworks, embedded `Frameworks/`/`PlugIns/*.appex`/WatchKit.
- **Android track:** DEX — multidex count, defined-vs-referenced method counts
  vs the 64K limit, per-package bloat, Kotlin metadata. Native `.so` — ABIs,
  strip status, checksec (PIE/canary/RELRO/NX).
- Provisioning + Code + NativeLibs cards.

**Exit:** iOS provisioning type + Mach-O checksec correct on a real IPA; Android
DEX counts + `.so` checksec correct on a real APK. Tracks can be built by two
people/agents in parallel — they touch disjoint analysers.

### M4 — Security posture & permissions  ✅ done
*Goal: the "should I worry about this?" surface.*

- Android: dangerous permissions, exported components, `usesCleartextTraffic`,
  `extractNativeLibs`, embedded-secret heuristics.
- iOS: usage-description strings, `PrivacyInfo.xcprivacy`, URL schemes, ATS
  exceptions, embedded-secret heuristics.
- Posture card with severity chips (Nord Aurora hues: red/orange/yellow/green).

**Exit:** posture findings render with severity; permission/ATS fixtures asserted.

### M5 — Obfuscation heuristics & AAB per-device sizing  ✅ done
*Goal: the two genuinely hard, judgement-based passes.*

- **Obfuscation/packing** (D4): R8/ProGuard vs DexGuard/packer signals →
  `{assessment, confidence, signals[]}`. Never a bare yes/no. (APKiD-style
  signature ideas reimplemented in managed code, or a curated signal set.)
- **AAB per-device sizing**: estimate download size across ABI × density ×
  language and savings vs a universal APK — the AAB's headline value. Done by
  reading `BundleConfig.pb` split dimensions + module contents (no bundletool).

**Exit:** obfuscation card shows confidence + evidence; AAB report shows a
per-device size estimate table.

### M6 — Report polish & export
*Goal: make the report shareable and the schema stable.*

- Export the report as **JSON** (the normalised schema, D2), **Markdown**, and a
  self-contained **HTML** report. Save via `StorageProvider` save-picker.
- Freeze v1 of the JSON schema + document it in `docs/`.
- Report UX: search/filter within a report, copy-a-field, expand/collapse all,
  clear empty categories, per-category error/degradation display.
- A short "summary strip" at the top (format, id, version, signer, top posture flags).

**Exit:** a report round-trips to JSON and back; HTML export opens standalone;
schema doc published.

### M7 — Distribution via Velopack (and optional CLI)
*Goal: ship it, with a one-liner install and in-app updates.*

Mirror Perch's proven Velopack setup exactly:
- **`publish.bat`** (+ `publish.sh` later): `dotnet publish -c Release -r win-x64
  --self-contained` (single-file, compressed) → `dnx vpk pack --packId Binoc
  --packTitle "binoc" --mainExe binoc.exe --outputDir releases\`.
- **`SHA256SUMS.txt`** written alongside the pack (sha256sum format, LF, lower-case
  hex) so the installer can verify the download.
- **`install.ps1`** — the one-liner: `irm https://raw.githubusercontent.com/<owner>/binoc/main/install.ps1 | iex`.
  Ported from Perch's: resolves the GitHub release, downloads
  `Binoc-win-Setup.exe`, **verifies it against `SHA256SUMS.txt` before running**,
  raises errors with `throw` (never `exit`, so it can't kill the user's shell),
  and **stays pure ASCII / no BOM** (the Windows PowerShell 5.1 curly-quote trap
  the Perch script documents). Supports `-Version` / `$env:BINOC_VERSION` pinning.
- **In-app updates** thereafter via Velopack (`UpdateManager`) — a "Check for
  updates" action in the window, since binoc has no tray.
- Icon via the `icon-prompt` skill; Windows head first, macOS head kept buildable
  (the pure-managed D1 choice makes this cheap).
- **Optional** `Binoc.Cli`: `binoc <file> --json|--md` reusing Binoc.Core — a
  thin head for CI use.

**Exit:** `install.ps1 | iex` installs a working app that verifies its checksum;
"Check for updates" pulls a newer release; `--json` output matches the schema.

---

## 4. Nord (dark) palette

Perch resolves Nord through its `Theme` model; binoc pins it directly. Map the
official Nord roles onto a small static `Palette` (same member-name vocabulary as
Perch's, so Perch UI helpers port cleanly):

| Palette role | Nord | Hex |
|---|---|---|
| `FormBg` (window) | `nord0` Polar Night | `#2E3440` |
| `Sunken` (rails/drop zone) | `nord1` | `#3B4252` |
| `ButtonBg` / card | `nord2` | `#434C5E` |
| `Border` / separator | `nord3` | `#4C566A` |
| `Fg` (body text) | `nord4` Snow Storm | `#D8DEE9` |
| `Title` | `nord6` | `#ECEFF4` |
| `Muted` | `nord3`-ish | `#7B88A1` |
| `Accent` (primary) | `nord8` Frost | `#88C0D0` |
| `Accent` (secondary) | `nord9`/`nord10` | `#81A1C1` / `#5E81AC` |
| Posture: error | `nord11` Aurora | `#BF616A` |
| Posture: warn | `nord13` | `#EBCB8B` |
| Posture: ok | `nord14` | `#A3BE8C` |
| Posture: info/attention | `nord12`/`nord15` | `#D08770` / `#B48EAD` |

Severity chips in the posture card (M4) draw straight from the Aurora hues.

---

## 5. Sequencing, risk & effort

- **Fastest path to value:** M0 → M1 → M2 is a shippable, genuinely useful tool
  (identity + archive + signing across all three formats — the ~80% cheap-win
  core). Treat that as the first release; M3+ are additive.
- **Parallelism:** after M2, the M3 iOS and Android tracks are independent and
  can run concurrently. M4/M5 depend on M1–M3 data but not on each other.
- **Highest-risk parsers** (spike early, ideally during M0/M1): AXML binary XML,
  the AAB protobuf wire-format reader, Mach-O fat/`cryptid`, and the APK v2/v3
  signing block. Each deserves a focused fixture + test before it's trusted.
  The protobuf reader is deliberately partial — we decode only the fields binoc
  reports, which is far less work than modelling the full bundletool schema.
- **Fixture corpus is a dependency, not an afterthought.** Collect real+minimal
  APK/AAB/IPA samples (incl. debug-signed, expired-cert, multidex, fat-binary,
  obfuscated) into `tests/fixtures/` early; the golden-file tests hang off it.
- **Confidence over false certainty** (D4): where binoc can't be sure
  (obfuscation, "time of creation", secrets), it reports confidence + evidence,
  never a bare claim.

---

## 6. Immediate next steps (M0)

1. Scaffold `binoc.slnx` + `Binoc.Core`, `Binoc.App`, `Binoc.Tests`
   (Avalonia 12.0.5, .NET 10, Fluent + Inter — copy Perch's csproj shape, minus
   the Windows/Mac dual-head machinery until M7).
2. Add the Nord `Palette` (§4) + a `BinocUi` helper trimmed from Perch's `SettingsUi`.
3. Build the single window: drop zone + **Choose file…**, both resolving to a path.
4. Define `AnalysisReport`, `Format`, `IAnalyzer`, and byte-signature format
   detection; wire a stub pipeline end-to-end.
5. Prove the loop: drop a file → placeholder report card renders in Nord dark.
