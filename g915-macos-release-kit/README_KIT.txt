G915-Stutter-Fix macOS release kit
==================================

What this does
--------------
It publishes the contributed macOS build WITHOUT disturbing your Windows users.

Your Windows app's update check reads the GitHub "latest release" endpoint,
which ignores pre-releases and anything not marked "Latest". So this kit ships
the macOS build on its own track:

  - tag:   macos-v1.0.0   (its own version line, not a Windows 3.x bump)
  - type:  pre-release, and NOT marked "Latest"

Result: releases/latest keeps pointing at your Windows v3.3.1, so CheckForUpdates
never notifies Windows users (or you). The Windows version line is untouched.

It writes into your repo:

  .github/workflows/macos-release.yml   builds the macOS app on GitHub's hosted
                                        runners (arm64 and x64) and, on a
                                        macos-v* tag, publishes it as a
                                        pre-release with both zips and
                                        SHA256SUMS.txt
  .github/CODEOWNERS                    routes macos/ to the contributor
  RELEASE_NOTES_macos.md                the macOS release body
  CHANGELOG.md                          a new [macOS 1.0.0] entry at the top
  MACOS_README_NOTE.txt                 a one-line note to paste into README.md

You never need a macOS machine: the build runs on GitHub's runners.

Before you run it
-----------------
- A local clone of your G915-Stutter-Fix repo (the folder with
  KeyboardRepeatFilter.sln and the macos/ folder).
- git installed. You already push this repo, so your credentials are set.

How to run it
-------------
1. Unzip this kit anywhere.
2. Double-click apply.bat  (or run apply.ps1 in PowerShell).
3. It will:
   - find your repo (or ask for its path),
   - ask for the macOS contributor's GitHub @handle (blank to skip),
   - commit and push the workflow + docs (this alone publishes nothing),
   - offer to create and push tag macos-v1.0.0, which builds and publishes the
     macOS pre-release.

After it runs
-------------
- Watch the build: https://github.com/lucduguaysita/G915-Stutter-Fix/actions
- The macOS pre-release appears at:
  https://github.com/lucduguaysita/G915-Stutter-Fix/releases
  Your Windows v3.3.1 keeps the green "Latest" badge.
- Paste the line in MACOS_README_NOTE.txt into your README, then commit.

If the publish step fails with a permissions or 403 error
---------------------------------------------------------
On GitHub: Settings > Actions > General > Workflow permissions, choose
"Read and write permissions", save, then re-run the workflow (or delete and
re-push the tag: git tag -d macos-v1.0.0 ; git push origin :macos-v1.0.0 ;
git tag macos-v1.0.0 ; git push origin macos-v1.0.0).

Later, if you ever want macOS to be a normal (non-pre-release) download, do it
only when you also intend Windows users to be notified, or change the Windows
update check to compare per platform. Until then, keep it a pre-release.

Note on the macOS build
-----------------------
The macOS app is new and community-maintained. You have not tested it, and the
notes and README say so. macOS bug reports should go to the contributor.
