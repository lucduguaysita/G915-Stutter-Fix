# Release Process

This project uses GitHub releases and git tags for versioned distribution.

## 1) Update version and changelog

- Update version in `KeyboardRepeatFilter.csproj`:
  - `<Version>x.y.z</Version>`
- Add a new section in `CHANGELOG.md` for that version.

## 2) Build release artifacts

- Build `Release` configuration.
- Confirm `releases` is refreshed and contains:
  - `KeyboardRepeatFilter.exe`
  - `Newtonsoft.Json.dll`
  - `config.json`

## 3) Run smoke tests

- Execute checklist in `docs/SMOKE_TESTS.md`.
- Fix issues before tagging.

## 4) Commit and push

Example:

```bash
git add .
git commit -m "Release v1.2.0"
git push origin main
```

## 5) Tag the release

Example:

```bash
git tag -a v1.2.0 -m "Release v1.2.0"
git push origin v1.2.0
```

## 6) Create GitHub release

1. Open the repo on GitHub.
2. Create a new release from tag `v1.2.0`.
3. Title example: `G915 Stutter Fix v1.2.0`.
4. Paste notes from `CHANGELOG.md`.
5. Attach files from `releases`:
   - `KeyboardRepeatFilter.exe`
   - `Newtonsoft.Json.dll`
   - `config.json`
6. Publish release.

## 7) Post-release checks

- Download release assets from GitHub and run once on a clean machine/profile.
- Verify tray startup, filtering, and logs.
- Confirm docs reflect final shipped behavior.
