## [macOS 1.0.0] - {{DATE}}

### Added
- **macOS build** (contributed by {{CREDIT}}). A native menu-bar app (`G915StutterFix.app`) that
  applies the same debounce algorithm as the Windows filter using `CGEventTap`, for macOS 13 or
  later, built from `macos/` with Swift Package Manager. It ships as a separate, community-maintained
  track (tags `macos-v*`) published as a pre-release that is intentionally not marked "latest", so
  the Windows update check is unaffected. Prebuilt `arm64` and `x64` archives are attached to the
  `macos-v1.0.0` release. Not tested by the maintainer.
