import Foundation

/// Maps config key names (Windows-style where possible) to macOS HID key codes.
enum KeyNames {
    static let nameToCode: [String: UInt16] = [
        // Letters (ANSI US)
        "a": 0, "s": 1, "d": 2, "f": 3, "h": 4, "g": 5, "z": 6, "x": 7,
        "c": 8, "v": 9, "b": 11, "q": 12, "w": 13, "e": 14, "r": 15,
        "y": 16, "t": 17, "1": 18, "2": 19, "3": 20, "4": 21, "6": 22,
        "5": 23, "equal": 24, "=": 24, "9": 25, "7": 26, "minus": 27, "-": 27,
        "8": 28, "0": 29, "rightbracket": 30, "]": 30, "o": 31, "u": 32,
        "leftbracket": 33, "[": 33, "i": 34, "p": 35, "return": 36, "enter": 36,
        "l": 37, "j": 38, "quote": 39, "'": 39, "k": 40, "semicolon": 41, ";": 41,
        "backslash": 42, "\\": 42, "comma": 43, ",": 43, "slash": 44, "/": 44,
        "n": 45, "m": 46, "period": 47, ".": 47, "tab": 48, "space": 49,
        "grave": 50, "`": 50, "back": 51, "backspace": 51, "delete": 51,
        "escape": 53, "esc": 53,
        "command": 55, "cmd": 55, "lwin": 55, "rwin": 54,
        "shift": 56, "lshift": 56, "capslock": 57, "caps": 57,
        "option": 58, "alt": 58, "lalt": 58, "loption": 58,
        "control": 59, "ctrl": 59, "lcontrol": 59, "lctrl": 59,
        "rshift": 60, "roption": 61, "ralt": 61, "rcontrol": 62, "rctrl": 62,
        "fn": 63,
        "f17": 64, "keypaddecimal": 65, "keypadmultiply": 67, "keypadplus": 69,
        "keypadclear": 71, "volume_up": 72, "volume_down": 73, "mute": 74,
        "keypaddivide": 75, "keypadenter": 76, "keypadminus": 78, "f18": 79,
        "f19": 80, "keypadequals": 81, "keypad0": 82, "keypad1": 83,
        "keypad2": 84, "keypad3": 85, "keypad4": 86, "keypad5": 87,
        "keypad6": 88, "keypad7": 89, "f20": 90, "keypad8": 91, "keypad9": 92,
        "f5": 96, "f6": 97, "f7": 98, "f3": 99, "f8": 100, "f9": 101,
        "f11": 103, "f13": 105, "f16": 106, "f14": 107, "f10": 109,
        "f12": 111, "f15": 113, "help": 114, "home": 115, "pageup": 116,
        "forwarddelete": 117, "f4": 118, "end": 119, "f2": 120, "pagedown": 121,
        "f1": 122, "left": 123, "right": 124, "down": 125, "up": 126,
    ]

    /// Always excluded: CapsLock toggle desyncs LED/state if filtered.
    static let alwaysExcluded: Set<UInt16> = [57]

    static func resolve(_ token: String) -> [UInt16] {
        let trimmed = token.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty { return [] }

        if let n = UInt16(trimmed) {
            return [n]
        }

        let key = trimmed
            .replacingOccurrences(of: "VK_", with: "", options: .caseInsensitive)
            .lowercased()

        // Modifiers: bare name covers left+right like Windows app.
        switch key {
        case "shift":
            return [56, 60]
        case "control", "ctrl":
            return [59, 62]
        case "option", "alt":
            return [58, 61]
        case "command", "cmd", "win":
            return [55, 54]
        default:
            if let code = nameToCode[key] {
                return [code]
            }
            return []
        }
    }

    static func displayName(for code: UInt16) -> String {
        if let pair = nameToCode.first(where: { $0.value == code }) {
            return pair.key.uppercased()
        }
        return "KC_\(code)"
    }
}
