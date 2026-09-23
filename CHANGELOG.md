# Changelog

All notable changes to binoc are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

---

## [Unreleased]

---

## [v0.1.0] - 2026-09-23

- Drop in an APK, AAB or IPA and get one normalised report — no Android Studio, no Xcode, no toolchain
- Identity at a glance: package/bundle id, versions, min/target SDK, debug vs release
- Signing details: certificate fingerprints, schemes, validity, and the honest "upload key ≠ what Play ships" note for AABs
- Real R8 metrics read straight from `r8.json` — obfuscation, optimisation and shrinking as Play sees them (never guessed)
- Packer / protector detection from concrete on-disk signatures, not a confidence score
- iOS posture: provisioning type, entitlements, Mach-O architectures, FairPlay encryption, hardening flags, privacy strings
- Per-device download-size estimates for AABs, plus an archive breakdown by file type
- Native library checksec (NX, RELRO, PIE, stack canary) and 16 KB page-size readiness
- Searchable report (Ctrl+F) with a recent-files sidebar, all local — nothing leaves the machine
- Everything parsed in managed .NET; the only shell-out is the one you didn't have to install
