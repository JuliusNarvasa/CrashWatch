# CrashWatch

A Windows 11 tray application that watches your system event log and **toast-notifies you when your PC crashes or shows early hardware-failure warnings**.

Built after a machine that was silently crashing for months — nothing tells you *why* your PC rebooted at 3 AM, and the evidence (event log entries) is easy to miss. CrashWatch surfaces it the moment you're back at the desktop.

## What it does

**Boot sweep** — every time Windows starts, CrashWatch checks whether the previous shutdown was a crash and reports it:

> **PC crashed — 0x7F UNEXPECTED_KERNEL_MODE_TRAP**
> DOUBLE FAULT — an exception occurred while handling an exception. Kernel stack/memory corruption; no driver blamed.
> Rebooted at 23:30. Dump saved: C:\WINDOWS\MEMORY.DMP

Bugcheck codes are decoded into plain language (IRQL violations, WHEA machine checks, kernel heap corruption, TDR failures, double faults, …) including meaningful rendering of the bugcheck parameters.

**Live precursors** — while Windows is up, a push-based event log watcher (`EventLogWatcher`, no polling) toasts on the errors that *precede* crashes:

| Source | Events | Meaning |
|---|---|---|
| WHEA-Logger | 1, 17, 18, 19, 20, 46, 47 | hardware / machine-check errors |
| disk | 7, 51, 153 | bad blocks, paging errors, I/O retries |
| storahci | 129, 130 | SATA port resets (drive stopped responding) |
| volmgr | 161 | crash dump failed to write |
| Ntfs | 55, 140 | file system corruption |
| Kernel-Processor-Power | 37, 86 | thermal throttling |

**Smart alerting** — per-source rate limiting and burst aggregation (an I/O retry storm gives you one toast, not fifteen). Recurring WHEA errors escalate to a "RECURRING" warning after 2 in 24 hours.

**History window** — tray icon opens a list of everything seen (time, severity, source, decoded meaning) with a **Clear list** button for entries you've already dealt with.

## Running it

```
CrashWatch.exe            # tray app
CrashWatch.exe --test-toast   # fire a test notification and exit
CrashWatch.exe --test-sweep   # run the crash sweep and exit
```

The app runs non-elevated or elevated; the System event log is readable without admin. It sets itself to `High` process priority.

### Autostart with admin privileges

```
schtasks /Create /F /TN CrashWatch /SC ONLOGON /RL HIGHEST /TR "<path>\CrashWatch.exe"
```

This creates a logon task that runs the app elevated (no UAC prompt at logon).

## Files

| Path | Purpose |
|---|---|
| `%APPDATA%\CrashWatch\config.json` | settings — live-reloaded on change (suppression windows, thresholds, pause, priority) |
| `%LOCALAPPDATA%\CrashWatch\history.csv` | append-only event history |
| `%LOCALAPPDATA%\CrashWatch\state.json` | last-reported shutdown (boot sweep dedupe) |
| `%LOCALAPPDATA%\CrashWatch\crashwatch.log` | diagnostics |

## Building

Requires the .NET 8 SDK (Windows):

```
dotnet publish -c Release -o publish
```

## Notes

- Nothing can toast *during* a BSOD — the boot sweep covers that case by reporting on the next startup.
- CrashWatch is a passive observer: it only reads the event log. It never modifies system settings.
