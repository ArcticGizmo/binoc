# binoc — APK / AAB / IPA Analysis Findings (Summary)

Founding research summary for **binoc**, a tool to inspect Android and iOS app
binaries and surface the metrics that are normally painful to extract: signers,
build/creation time, DEX info, obfuscation, optimisation/size, and security posture.

Date: 2026-09-22

---

## The core insight

Questions people ask about APK, AAB, and IPA files collapse into the **same ~8 categories**.
The formats differ mainly in *encoding*, not in *what you want to know* — so binoc can share
most of its analysis logic and expose a single normalised output across all three.

- **APK** and **IPA** are ZIP archives; **AAB** is also a ZIP but uses protobuf manifests/resources.
- **AAB is not installable** — it's a *publishing* format; Play generates per-device APKs from it.
- Roughly 80% of the useful answers come from **archive + manifest + signature block**, with no
  code decompilation required. That defines the cheap-win core of the tool.

---

## The 8 shared categories

| Category | Typical questions | Data source |
|---|---|---|
| **Identity** | package/bundle ID, version + build, min/target SDK, debug vs release, build time | Manifest / Info.plist |
| **Signing** | who signed it, cert fingerprints + validity, schemes (v1–v4 / codesign), debug key?, key rotation | META-INF + signing block / `_CodeSignature` |
| **Provisioning** *(iOS only)* | dev/ad-hoc/enterprise/App Store, entitlements, provisioned UDIDs, `get-task-allow` | `embedded.mobileprovision` |
| **Code** | DEX count (multidex?), method/class counts vs 64K limit, which libs bloat it | `classes*.dex` / Mach-O |
| **Obfuscation** | obfuscated (R8/ProGuard)?, packed/protected (DexGuard etc.)?, anti-tamper markers | heuristics — report *confidence* |
| **Optimisation / size** | download vs installed size, per-type breakdown, zip-alignment, resource shrinking, **per-device size (AAB)** | ZIP table / `bundletool get-size` |
| **Native libs** | ABIs present, stripped?, PIE/canary/RELRO (Android ELF); archs + encryption + checksec (iOS Mach-O) | `lib/<abi>` / Mach-O |
| **Security posture** | dangerous permissions, exported components, cleartext/ATS, embedded secrets, privacy manifest | Manifest / plist |

---

## Format-specific notes

### APK (Android)
- Identity from `AndroidManifest.xml` (binary XML) + `resources.arsc`.
- Signing: detect scheme set (v1 JAR, v2, v3, v3.1 rotation, v4); parse cert chain, fingerprints,
  validity; flag debug keys.
- DEX: multidex count, defined vs referenced method counts, per-package bloat, Kotlin metadata.
- Size: per-type breakdown (dex/resources/native/assets), zip-alignment (4-byte / 16KB), WebP vs PNG,
  `extractNativeLibs` state.
- Native `.so`: ABIs, strip status, checksec (PIE/canary/RELRO/NX).

### AAB (Android App Bundle)
- Everything APK-conceptual, but **manifests are protobuf** and content is **per-module**.
- Structure: base + feature/dynamic modules, delivery types (install-time / on-demand / conditional),
  asset packs, `BundleConfig.pb` (enabled split dimensions).
- **Signing caveat (important):** the AAB signer is usually the *upload* key, not the distribution
  key — final APK signing is done by **Play App Signing**. binoc must say this explicitly so users
  don't mistake the upload key for what ships.
- Size is the AAB's headline value: estimate **per-device** download size across ABI × density ×
  language, and savings vs a universal APK.

### IPA (iOS)
- Identity from `Payload/<App>.app/Info.plist` (bundle ID, versions, `MinimumOSVersion`, device family,
  `DTSDKName`/`DTXcode` build provenance).
- Code signing & provisioning: signing identity + certs, entitlements, embedded provisioning profile
  (CMS/PKCS#7 wrapping an XML plist) → distribution type, team, validity, provisioned UDIDs,
  `get-task-allow`.
- Mach-O: architectures (arm64/arm64e, fat binary?), FairPlay encryption (`cryptid`), PIE, ARC,
  stack canary, strip status, linked dylibs/frameworks, `LC_CODE_SIGNATURE`.
- Embedded: `Frameworks/`, app extensions (`PlugIns/*.appex`), WatchKit app, `Assets.car`.
- Privacy/posture: usage-description strings, `PrivacyInfo.xcprivacy`, URL schemes, ATS exceptions.

---

## Design decisions surfaced by the research

1. **"Time of creation" is deliberately fuzzy.** ZIP timestamps are often zeroed for reproducible
   builds. binoc should prefer build metadata (Mach-O `LC_BUILD_VERSION`, Android build fingerprint,
   cert not-before as a proxy) and **label the provenance of every timestamp** rather than asserting
   one authoritative date.
2. **Obfuscation/packing is heuristic, not a fact** — emit a confidence score, never a bare yes/no.
3. **Disambiguate upload vs distribution key** for AAB (Play App Signing).
4. **One normalised JSON schema across all three formats** — formats map onto shared fields where
   they overlap; downstream diffing and CI checks then become format-agnostic.
5. **Tier the work:** "archive + manifest + signature" answers are cheap and offline; DEX/Mach-O deep
   analysis and decompilation are opt-in heavier passes.

---

## Suggested analyser modules

| Module | APK | AAB | IPA |
|---|---|---|---|
| Archive/size walk | ✓ | ✓ | ✓ |
| Identity/manifest | ✓ | ✓ | ✓ |
| Signing & certs | ✓ | ✓ | ✓ |
| Provisioning | | | ✓ |
| Code/DEX | ✓ | ✓ | |
| Mach-O | | | ✓ |
| Obfuscation/packing | ✓ | ✓ | (✓) |
| Native libs (ELF) | ✓ | ✓ | |
| Permissions/posture | ✓ | ✓ | ✓ |

---

## Reference tooling (validate against / shell out to)

**Android:** `aapt2`, `apkanalyzer`, `apksigner`, `bundletool`, `zipalign`, `dexdump`, `jadx`,
`keytool`, APKiD (packer/obfuscator signatures).
**iOS:** `codesign`, `security cms`, `plutil`, `otool`, `lipo`, `nm`, `size`, `jtool2`.
**Cross:** ZIP libraries, `openssl` (cert parsing), `readelf` / `checksec` (native libs).

---

*Full question-by-question breakdown lives in the companion reference doc
(`mobile-binary-analysis-questions.md`) in the claude-thoughts repo.*
