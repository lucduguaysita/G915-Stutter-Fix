# G915 Stutter Fix — macOS

User-mode keyboard debounce for chatter / phantom double-presses (G915 and similar). Port of the Windows filter algorithm via `CGEventTap`.

## Requirements

- macOS 13+
- Xcode Command Line Tools (`xcode-select --install`)
- **Accessibility** permission (required for event taps)

## Build

```bash
cd macos
chmod +x Scripts/build.sh
./Scripts/build.sh
```

Creates `macos/dist/G915StutterFix.app`.

## Run

```bash
open dist/G915StutterFix.app
```

Menu bar shows ⌨. First launch: grant Accessibility when prompted.

**System Settings → Privacy & Security → Accessibility** → enable **G915 Stutter Fix**.

If tap fails after rebuild: remove the old entry, add the new `.app`, relaunch.

## Config

Written on first run:

`~/Library/Application Support/G915StutterFix/config.json`

Same shape as Windows `config.json` for the fields that matter:

| Key | Default | Meaning |
|---|---|---|
| `FilterMode` | `BlockRepress` | `BlockRepress` = drop bounce presses; `BlockRelease` = protect held keys |
| `MinRepeatIntervalMs` | `70` | Debounce window (ms); raise if chatter still lands |
| `BurstBypass` | `false` | Pass machine-speed bursts (e.g. YubiKey) |
| `ExcludedKeys` | Back, Return, volume | Never filter these |
| `PerKeyMinRepeatIntervalMs` | — | Per-key threshold overrides |

Tray: **Reload config** after editing. Log: `~/Library/Logs/G915StutterFix.log`.

## Filter modes

- **Block double presses** (`BlockRepress`) — everyday typing; stops `aa` from one tap.
- **Protect held keys** (`BlockRelease`) — gaming / sticky modifiers; suppresses phantom *release*.

## Launch at login

Menu ⌨ → **Launch at login** (checkmark = on).

Writes `~/Library/LaunchAgents/com.g915stutterfix.macos.plist` pointing at this build’s executable.

**Tip:** keep the `.app` in a stable path (e.g. copy to `/Applications`). If you rebuild into another folder, toggle Launch at login off/on once so the plist updates.

## Not ported (yet)

- Mouse click debounce
- Game profile auto-switch
- Heatmap / GameListUpdater
- Update checker

## Troubleshooting

Tap won't start → Accessibility not granted to *this* binary path.

Still chatter → raise `MinRepeatIntervalMs` (try `80`–`100`) or set a noisy key in `PerKeyMinRepeatIntervalMs`.

Held Ctrl/Shift breaks → switch to **Protect held keys**.

Default is **70 ms** (covers ~59 ms+ bounce some G915 units show on macOS). Windows default stays 28 ms.
