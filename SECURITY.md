# Security

## Reporting a problem

If you find a security problem in Factory Seat, or a download that doesn't look right, **please report it privately**, not in a public issue:

1. Go to the [Security tab](https://github.com/factoryseatlmuapp/factory-seat/security).
2. Press **Report a vulnerability** and describe what you found.

Only the maintainers can see the report. You'll get an answer there.

## Getting the real app

Factory Seat is only published in two places:

- [GitHub Releases](https://github.com/factoryseatlmuapp/factory-seat/releases)
- Its OverTake listing, which links back here

Each release's notes list the installer's **SHA-256 hash** and its **VirusTotal** result. If a copy from anywhere else has a different hash, don't run it.

## What the app does on your PC

- It reads LMU's results files and race weekend saves. It never changes LMU's files.
- It saves your careers and settings in `%AppData%\LmuCareer`.
- Its only online request is an optional check for a newer release on GitHub, which you can turn off in Settings. It collects no data and has no accounts or servers. The [README](README.md) has the details.
- Content detection reads access information from LMU's local API at `http://localhost:6397`. Clicking **Launch weekend in LMU** explicitly authorizes applying and verifying the configured settings, then loading the weekend. The client does not automatically retry game commands, and loading is blocked when verification fails. No game data is sent to an external service; race results are read exclusively from XML files.
- Manual start is the default and calls no game API. API failures leave a manual continuation available; an uncertain load never causes an automatic replay or resets an already-recorded XML matching start time. No game API probe runs automatically when opening the weekend configuration.
- All four game API entry points, including content detection, require an unchecked consent checkbox followed by explicit acceptance. The session-only notice is translated into every shipped language and distinguishes this fork's integration from the original author's work. Settings can revoke consent; reloading/closing the app clears it. The backend also rejects requests without the expected boolean consent and notice version. This is an application consent guard, not an authentication boundary against modified clients or a guarantee of legal immunity. Revocation cannot recall in-flight commands.
