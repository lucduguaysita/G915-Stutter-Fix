# Security Policy

## Supported versions

Security fixes are provided for the latest release series.

Current supported version:

- `2.0.x`

Older versions may not receive security updates.

## Reporting a vulnerability

Please do **not** open public GitHub issues for suspected security vulnerabilities.

Instead, report privately with:

- A clear description of the issue
- Steps to reproduce
- Impact assessment (what an attacker could do)
- Logs or proof of concept if available

If you do not have a private contact channel configured yet, add one in the repository settings or profile and update this file accordingly.

## Scope notes

This application:

- runs in user mode (no elevation required or requested)
- does not install kernel drivers
- does not modify firmware
- does not inject into other processes
- does not make any network connections (fully offline / air-gap friendly)

To detect when its keyboard filter is being bypassed by an elevated window, the app performs a
**read-only** integrity-level query on the foreground process (`OpenProcess` with
`PROCESS_QUERY_LIMITED_INFORMATION`). It does not read window contents, keystrokes of other
processes, or any other process memory.

Even with this lower-risk architecture, vulnerabilities may still exist (for example in parsing, logging paths, startup registration, integrity queries, or dependency handling).

## Disclosure process

When a valid report is received:

1. The issue is reproduced and triaged.
2. A fix is developed and tested.
3. A patched release is published.
4. Public disclosure follows after users have a reasonable time to update.
