import Foundation
import CoreGraphics

/// Ports the Windows KeyboardHookFilter debounce logic to CGEventTap.
final class KeyboardFilter {
    private let injectedMarker: Int64 = 0x5246_4C54 // "RFLT"

    private var config: FilterConfig
    private var excludedKeys = Set<UInt16>()
    private var thresholdMsByKey = [UInt16: Double]()
    private var defaultThresholdMs: Double = 70

    private var lastUpMs = [UInt16: Double](uniqueKeysWithValues: (0..<256).map { ($0, 0.0) })
    private var isPressed = [UInt16: Bool](uniqueKeysWithValues: (0..<256).map { ($0, false) })
    private var swallowNextUp = [UInt16: Bool](uniqueKeysWithValues: (0..<256).map { ($0, false) })

    // BlockRelease state
    private var pendingUp = [UInt16: Bool](uniqueKeysWithValues: (0..<256).map { ($0, false) })
    private var releaseTimers = [UInt16: DispatchSourceTimer]()
    private let sync = NSLock()

    // Burst bypass
    private var burstBypass = false
    private let burstGapMs = 25.0
    private let burstMinStreak = 2
    private var lastDownMs: Double = 0
    private var rapidStreak = 0
    private var inBurst = false

    private var blockRelease = false
    private var eventTap: CFMachPort?
    private var runLoopSource: CFRunLoopSource?
    private let logger: Logger

    var onFiltered: ((UInt16, String) -> Void)?

    init(config: FilterConfig, logger: Logger) {
        self.config = config
        self.logger = logger
        apply(config: config)
    }

    func apply(config: FilterConfig) {
        sync.lock()
        defer { sync.unlock() }

        self.config = config
        blockRelease = config.isBlockRelease
        burstBypass = config.burstBypass
        defaultThresholdMs = max(1, config.minRepeatIntervalMs)
        thresholdMsByKey.removeAll()
        excludedKeys = KeyNames.alwaysExcluded

        for token in config.excludedKeys {
            for code in KeyNames.resolve(token) {
                excludedKeys.insert(code)
            }
        }

        for (token, ms) in config.perKeyMinRepeatIntervalMs where ms >= 0 {
            for code in KeyNames.resolve(token) {
                thresholdMsByKey[code] = ms
            }
        }

        lastDownMs = 0
        rapidStreak = 0
        inBurst = false
        logger.info("Filter mode=\(config.filterMode) threshold=\(defaultThresholdMs)ms burstBypass=\(burstBypass)")
    }

    @discardableResult
    func start() -> Bool {
        stop()

        let mask =
            (1 << CGEventType.keyDown.rawValue) |
            (1 << CGEventType.keyUp.rawValue) |
            (1 << CGEventType.flagsChanged.rawValue)

        let unmanaged = Unmanaged.passUnretained(self).toOpaque()
        guard let tap = CGEvent.tapCreate(
            tap: .cgSessionEventTap,
            place: .headInsertEventTap,
            options: .defaultTap,
            eventsOfInterest: CGEventMask(mask),
            callback: { proxy, type, event, refcon in
                guard let refcon else { return Unmanaged.passUnretained(event) }
                let filter = Unmanaged<KeyboardFilter>.fromOpaque(refcon).takeUnretainedValue()
                return filter.handle(proxy: proxy, type: type, event: event)
            },
            userInfo: unmanaged
        ) else {
            logger.error("CGEvent.tapCreate failed — grant Accessibility (and Input Monitoring) for this app, then restart.")
            return false
        }

        eventTap = tap
        runLoopSource = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, tap, 0)
        CFRunLoopAddSource(CFRunLoopGetMain(), runLoopSource, .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)
        logger.info("Keyboard event tap started.")
        return true
    }

    func stop() {
        if let source = runLoopSource {
            CFRunLoopRemoveSource(CFRunLoopGetMain(), source, .commonModes)
            runLoopSource = nil
        }
        if let tap = eventTap {
            CGEvent.tapEnable(tap: tap, enable: false)
            eventTap = nil
        }
        sync.lock()
        for code in 0..<UInt16(256) {
            cancelPendingUp(UInt16(code))
        }
        sync.unlock()
    }

    private func handle(proxy: CGEventTapProxy, type: CGEventType, event: CGEvent) -> Unmanaged<CGEvent>? {
        if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
            if let tap = eventTap {
                CGEvent.tapEnable(tap: tap, enable: true)
                logger.warn("Event tap re-enabled after disable.")
            }
            return Unmanaged.passUnretained(event)
        }

        // Pass through our own re-injected key-ups.
        if event.getIntegerValueField(.eventSourceUserData) == injectedMarker {
            return Unmanaged.passUnretained(event)
        }

        let keyCode = UInt16(event.getIntegerValueField(.keyboardEventKeycode))
        guard keyCode < 256 else {
            return Unmanaged.passUnretained(event)
        }

        let isDown: Bool
        let isUp: Bool
        switch type {
        case .keyDown:
            isDown = true
            isUp = false
        case .keyUp:
            isDown = false
            isUp = true
        case .flagsChanged:
            let nowPressed = Self.modifierDown(flags: event.flags, keyCode: keyCode)
            sync.lock()
            let wasPressed = isPressed[keyCode] ?? false
            sync.unlock()
            isDown = nowPressed && !wasPressed
            isUp = !nowPressed && wasPressed
            if !isDown && !isUp {
                return Unmanaged.passUnretained(event)
            }
        default:
            return Unmanaged.passUnretained(event)
        }

        if burstBypass && isDown {
            updateBurstState()
        }

        sync.lock()
        defer { sync.unlock() }

        if excludedKeys.contains(keyCode) {
            if isDown { isPressed[keyCode] = true }
            if isUp { isPressed[keyCode] = false; lastUpMs[keyCode] = nowMs() }
            return Unmanaged.passUnretained(event)
        }

        let swallow: Bool
        if blockRelease {
            swallow = handleBlockRelease(keyCode: keyCode, isDown: isDown, isUp: isUp)
        } else {
            swallow = handleBlockRepress(keyCode: keyCode, isDown: isDown, isUp: isUp)
        }

        return swallow ? nil : Unmanaged.passUnretained(event)
    }

    /// Left/right device masks live in the same CGEventFlags word as the
    /// shared .maskShift/.maskControl bits (NX_DEVICE*KEYMASK from IOKit).
    private static func modifierDown(flags: CGEventFlags, keyCode: UInt16) -> Bool {
        let raw = flags.rawValue
        switch keyCode {
        case 56: return (raw & 0x0000_0002) != 0 // left shift
        case 60: return (raw & 0x0000_0004) != 0 // right shift
        case 59: return (raw & 0x0000_0001) != 0 // left control
        case 62: return (raw & 0x0000_2000) != 0 // right control
        case 58: return (raw & 0x0000_0020) != 0 // left option
        case 61: return (raw & 0x0000_0040) != 0 // right option
        case 55: return (raw & 0x0000_0008) != 0 // left command
        case 54: return (raw & 0x0000_0010) != 0 // right command
        case 57: return flags.contains(.maskAlphaShift)
        default:
            return false
        }
    }

    private func updateBurstState() {
        let now = nowMs()
        let gap = now - lastDownMs
        if lastDownMs != 0 && gap < burstGapMs {
            if rapidStreak < burstMinStreak {
                rapidStreak += 1
            }
        } else {
            rapidStreak = 0
        }
        lastDownMs = now
        inBurst = rapidStreak >= burstMinStreak
    }

    private var burstActive: Bool { burstBypass && inBurst }

    private func threshold(for keyCode: UInt16) -> Double {
        thresholdMsByKey[keyCode] ?? defaultThresholdMs
    }

    private func handleBlockRepress(keyCode: UInt16, isDown: Bool, isUp: Bool) -> Bool {
        let now = nowMs()

        if isDown {
            let wasPressed = isPressed[keyCode] ?? false
            let lastUp = lastUpMs[keyCode] ?? 0
            if !wasPressed && (now - lastUp) < threshold(for: keyCode) && !burstActive {
                logFiltered(keyCode, reason: "filtered")
                swallowNextUp[keyCode] = true
                return true
            }
            isPressed[keyCode] = true
            swallowNextUp[keyCode] = false
            return false
        }

        if isUp {
            if swallowNextUp[keyCode] == true {
                swallowNextUp[keyCode] = false
                return true
            }
            lastUpMs[keyCode] = now
            isPressed[keyCode] = false
        }

        return false
    }

    private func handleBlockRelease(keyCode: UInt16, isDown: Bool, isUp: Bool) -> Bool {
        if burstActive {
            if isDown {
                if pendingUp[keyCode] == true { cancelPendingUp(keyCode) }
                isPressed[keyCode] = true
            } else if isUp {
                if pendingUp[keyCode] == true { cancelPendingUp(keyCode) }
                isPressed[keyCode] = false
            }
            return false
        }

        if isDown {
            if pendingUp[keyCode] == true {
                cancelPendingUp(keyCode)
                logFiltered(keyCode, reason: "release-held")
                return true
            }
            isPressed[keyCode] = true
            return false
        }

        if isUp {
            if pendingUp[keyCode] == true {
                return true
            }
            if isPressed[keyCode] != true {
                return false
            }

            pendingUp[keyCode] = true
            scheduleRelease(keyCode: keyCode, delayMs: threshold(for: keyCode))
            return true
        }

        return false
    }

    private func scheduleRelease(keyCode: UInt16, delayMs: Double) {
        releaseTimers[keyCode]?.cancel()
        let timer = DispatchSource.makeTimerSource(queue: .main)
        timer.schedule(deadline: .now() + .milliseconds(Int(delayMs.rounded(.up))))
        timer.setEventHandler { [weak self] in
            self?.emitDeferredKeyUp(keyCode: keyCode)
        }
        releaseTimers[keyCode] = timer
        timer.resume()
    }

    private func cancelPendingUp(_ keyCode: UInt16) {
        pendingUp[keyCode] = false
        releaseTimers[keyCode]?.cancel()
        releaseTimers[keyCode] = nil
    }

    private func emitDeferredKeyUp(keyCode: UInt16) {
        sync.lock()
        guard pendingUp[keyCode] == true else {
            sync.unlock()
            return
        }
        pendingUp[keyCode] = false
        releaseTimers[keyCode] = nil
        isPressed[keyCode] = false
        lastUpMs[keyCode] = nowMs()
        sync.unlock()

        guard let source = CGEventSource(stateID: .hidSystemState),
              let up = CGEvent(keyboardEventSource: source, virtualKey: keyCode, keyDown: false) else {
            return
        }
        up.setIntegerValueField(.eventSourceUserData, value: injectedMarker)
        up.post(tap: .cgSessionEventTap)
    }

    private func logFiltered(_ keyCode: UInt16, reason: String) {
        let name = KeyNames.displayName(for: keyCode)
        logger.info("\(reason) key=\(name) (\(keyCode))")
        onFiltered?(keyCode, reason)
    }

    private func nowMs() -> Double {
        ProcessInfo.processInfo.systemUptime * 1000.0
    }
}
