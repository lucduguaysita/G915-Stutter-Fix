## G915 Stutter Fix for macOS 1.0.0

First macOS build of the stutter filter, contributed by {{CREDIT}}. It is a native menu-bar app that applies the same debounce algorithm as the Windows version, using CGEventTap. Requires macOS 13 or later.

This is a separate, community-maintained track from the Windows app and is not tested by the maintainer. It is published as a pre-release on its own version line (macOS 1.0.0), so it does not change the Windows "latest" release and does not prompt Windows users to update.

### Downloads

- `G915StutterFix-macos-1.0.0-arm64.zip` for arm64 machines
- `G915StutterFix-macos-1.0.0-x64.zip` for Intel (x64) machines

Unzip, move `G915StutterFix.app` wherever you like, and launch it; it runs in the menu bar. The app is ad-hoc signed, so the first launch may require allowing it in your security settings, and it needs Accessibility / Input Monitoring permission to filter keystrokes. See `macos/README.md` for details. Please report macOS issues to the contributor.

### Checksums

`SHA256SUMS.txt` lists the SHA-256 of each archive.
