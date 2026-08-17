import AppKit
import ApplicationServices

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var statusItem: NSStatusItem!
    private var filter: KeyboardFilter!
    private var logger: Logger!
    private var config: FilterConfig!
    private var filteredCount = 0
    private var enabled = true

    func applicationDidFinishLaunching(_ notification: Notification) {
        config = FilterConfig.load()
        logger = Logger(config: config)
        filter = KeyboardFilter(config: config, logger: logger)
        filter.onFiltered = { [weak self] _, _ in
            DispatchQueue.main.async {
                self?.filteredCount += 1
                self?.refreshTooltip()
            }
        }

        setupStatusItem()
        requestAccessibilityIfNeeded()

        if !filter.start() {
            showAccessibilityAlert()
        }

        refreshTooltip()
        syncLaunchAtLoginFromConfig()
        logger.info("G915 Stutter Fix (macOS) ready. Config: \(FilterConfig.defaultConfigURL.path)")
    }

    func applicationWillTerminate(_ notification: Notification) {
        filter.stop()
    }

    private func setupStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let button = statusItem.button {
            button.title = "⌨"
            button.toolTip = "G915 Stutter Fix"
        }
        rebuildMenu()
    }

    private func rebuildMenu() {
        let menu = NSMenu()

        let toggle = NSMenuItem(
            title: enabled ? "Pause filtering" : "Resume filtering",
            action: #selector(toggleEnabled),
            keyEquivalent: ""
        )
        toggle.target = self
        menu.addItem(toggle)

        menu.addItem(.separator())

        let modeMenu = NSMenu()
        let repress = NSMenuItem(title: "Block double presses", action: #selector(setBlockRepress), keyEquivalent: "")
        repress.target = self
        repress.state = config.isBlockRelease ? .off : .on
        modeMenu.addItem(repress)

        let release = NSMenuItem(title: "Protect held keys", action: #selector(setBlockRelease), keyEquivalent: "")
        release.target = self
        release.state = config.isBlockRelease ? .on : .off
        modeMenu.addItem(release)

        let modeItem = NSMenuItem(title: "Filter mode", action: nil, keyEquivalent: "")
        modeItem.submenu = modeMenu
        menu.addItem(modeItem)

        menu.addItem(.separator())

        let openConfig = NSMenuItem(title: "Open config folder", action: #selector(openConfigFolder), keyEquivalent: "")
        openConfig.target = self
        menu.addItem(openConfig)

        let openLog = NSMenuItem(title: "Open log", action: #selector(openLog), keyEquivalent: "")
        openLog.target = self
        menu.addItem(openLog)

        let reload = NSMenuItem(title: "Reload config", action: #selector(reloadConfig), keyEquivalent: "r")
        reload.target = self
        menu.addItem(reload)

        let launchAtLogin = NSMenuItem(
            title: "Launch at login",
            action: #selector(toggleLaunchAtLogin),
            keyEquivalent: ""
        )
        launchAtLogin.target = self
        launchAtLogin.state = LaunchAtLogin.isEnabled ? .on : .off
        menu.addItem(launchAtLogin)

        menu.addItem(.separator())

        let accessibility = NSMenuItem(title: "Open Accessibility settings", action: #selector(openAccessibilitySettings), keyEquivalent: "")
        accessibility.target = self
        menu.addItem(accessibility)

        let about = NSMenuItem(title: "About", action: #selector(showAbout), keyEquivalent: "")
        about.target = self
        menu.addItem(about)

        menu.addItem(.separator())

        let quit = NSMenuItem(title: "Quit", action: #selector(quit), keyEquivalent: "q")
        quit.target = self
        menu.addItem(quit)

        statusItem.menu = menu
    }

    private func refreshTooltip() {
        let mode = config.isBlockRelease ? "Protect held keys" : "Block double presses"
        let state = enabled ? "on" : "paused"
        statusItem.button?.toolTip = "G915 Stutter Fix (\(state))\nMode: \(mode)\nFiltered: \(filteredCount)"
        statusItem.button?.title = enabled ? "⌨" : "⌨⏸"
    }

    private func requestAccessibilityIfNeeded() {
        let opts = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(opts)
    }

    private func showAccessibilityAlert() {
        let alert = NSAlert()
        alert.messageText = "Accessibility permission required"
        alert.informativeText = """
        G915 Stutter Fix must filter keyboard events before apps see them.

        System Settings → Privacy & Security → Accessibility
        Enable this app (or the Terminal/Cursor binary if you launched from there), then use Reload or restart.
        """
        alert.addButton(withTitle: "Open Settings")
        alert.addButton(withTitle: "OK")
        if alert.runModal() == .alertFirstButtonReturn {
            openAccessibilitySettings()
        }
    }

    @objc private func toggleEnabled() {
        enabled.toggle()
        if enabled {
            if !filter.start() {
                enabled = false
                showAccessibilityAlert()
            }
        } else {
            filter.stop()
        }
        rebuildMenu()
        refreshTooltip()
    }

    @objc private func setBlockRepress() {
        config.filterMode = "BlockRepress"
        saveAndApply()
    }

    @objc private func setBlockRelease() {
        config.filterMode = "BlockRelease"
        saveAndApply()
    }

    private func saveAndApply() {
        do {
            try config.save()
        } catch {
            logger.error("Failed to save config: \(error)")
        }
        filter.apply(config: config)
        if enabled {
            _ = filter.start()
        }
        rebuildMenu()
        refreshTooltip()
    }

    @objc private func reloadConfig() {
        config = FilterConfig.load()
        logger = Logger(config: config)
        filter.apply(config: config)
        if enabled {
            if !filter.start() {
                showAccessibilityAlert()
            }
        }
        rebuildMenu()
        refreshTooltip()
    }

    @objc private func toggleLaunchAtLogin() {
        let want = !LaunchAtLogin.isEnabled
        do {
            try LaunchAtLogin.setEnabled(want)
            config.launchAtLogin = want
            try config.save()
            logger.info("Launch at login \(want ? "enabled" : "disabled")")
        } catch {
            logger.error("Launch at login failed: \(error.localizedDescription)")
            let alert = NSAlert()
            alert.messageText = "Could not update Launch at Login"
            alert.informativeText = error.localizedDescription
            alert.runModal()
        }
        rebuildMenu()
    }

    /// Apply saved preference once at startup (e.g. after moving the .app).
    private func syncLaunchAtLoginFromConfig() {
        guard config.launchAtLogin != LaunchAtLogin.isEnabled else { return }
        do {
            try LaunchAtLogin.setEnabled(config.launchAtLogin)
        } catch {
            logger.warn("Could not sync launch-at-login: \(error.localizedDescription)")
        }
        rebuildMenu()
    }

    @objc private func openConfigFolder() {
        let url = FilterConfig.defaultConfigURL.deletingLastPathComponent()
        NSWorkspace.shared.open(url)
    }

    @objc private func openLog() {
        let path = config.logFilePath.isEmpty ? FilterConfig.defaultLogURL.path : config.logFilePath
        NSWorkspace.shared.open(URL(fileURLWithPath: path))
    }

    @objc private func openAccessibilitySettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") {
            NSWorkspace.shared.open(url)
        }
    }

    @objc private func showAbout() {
        let alert = NSAlert()
        alert.messageText = "G915 Stutter Fix (macOS)"
        alert.informativeText = """
        User-mode keyboard debounce for chatter / double-presses (Logitech G915 and similar).

        Config: \(FilterConfig.defaultConfigURL.path)
        Filtered this session: \(filteredCount)

        Port of the Windows filter algorithm (BlockRepress / BlockRelease).
        """
        alert.runModal()
    }

    @objc private func quit() {
        NSApp.terminate(nil)
    }
}
