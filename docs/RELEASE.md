# Release Process

This project uses GitHub releases and git tags for versioned distribution.

## 1) Update version and changelog

- Update the assembly version in `Properties/AssemblyInfo.cs` (this is what the **About** box
  shows):
  - `AssemblyVersion`, `AssemblyFileVersion`, and `AssemblyInformationalVersion`
- Update the matching version in `KeyboardRepeatFilter.csproj`:
  - `<Version>x.y.z</Version>`
- If `KeyboardHeatmap` changed, bump its version in `KeyboardHeatmap/Properties/AssemblyInfo.cs`
  (it is versioned independently of the main app).
- Add a new dated section in `CHANGELOG.md` for that version.

## 2) Build release artifacts

- Build `Release` configuration.
- Confirm `releases` is refreshed and contains:
  - `KeyboardRepeatFilter.exe`
  - `KeyboardHeatmap.exe`
  - `Newtonsoft.Json.dll`
  - `config.json`

## 3) Run smoke tests

- Execute checklist in `docs/SMOKE_TESTS.md`.
- Fix issues before tagging.

## 4) Commit and push

Example:

```bash
git add .
git commit -m "Release 3.0.0"
git push origin main
```

## 5) Tag the release

Example:

```bash
git tag -a v3.0.0 -m "Release v3.0.0"
git push origin v3.0.0
```

## 6) Create GitHub release

1. Open the repo on GitHub.
2. Create a new release from tag `v3.0.0`.
3. Title example: `G915 Stutter Fix v3.0.0`.
4. Paste notes from `CHANGELOG.md`.
5. Attach files from `releases`:
   - `KeyboardRepeatFilter.exe`
   - `KeyboardHeatmap.exe`
   - `Newtonsoft.Json.dll`
   - `config.json`
6. Publish release.

## 7) Post-release checks

- Download release assets from GitHub and run once on a clean machine/profile.
- Verify tray startup, filtering, and logs.
- Run `KeyboardHeatmap.exe` and confirm the HTML report is generated correctly.
- Confirm docs reflect final shipped behavior.
