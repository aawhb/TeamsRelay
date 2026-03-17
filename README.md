# TeamsRelay

[![License: GPL-3.0](https://img.shields.io/badge/License-GPL--3.0-blue.svg)](LICENSE)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows-0078D6.svg)](https://github.com/aawhb/TeamsRelay/releases)

**Get Microsoft Teams notifications on one or more mobile devices without installing Teams on them.**

```
Teams (on your PC)  →  TeamsRelay  →  KDE Connect  →  phone/tablet
```

## Why TeamsRelay?

Many companies require you to install a management profile (MDM) on any device that runs company apps like Teams. Once that profile is on your phone, IT can enforce policies, track the device, or even remotely wipe it. Your personal device effectively becomes a company phone.

TeamsRelay sidesteps all of that. It reads notification banners directly from the Teams desktop app on your Windows PC and forwards them to your mobile device through [KDE Connect](https://kdeconnect.kde.org/). No Teams app on the phone, no company profile, no MDM.

## How It Works

TeamsRelay runs in the background on your Windows PC. When a Teams notification banner appears, it uses [Windows UI Automation](https://learn.microsoft.com/en-us/windows/win32/winauto/entry-uiauto-win32) to read the text, deduplicates and normalizes it, then sends it as a ping through KDE Connect to your paired device(s). Android shows the full message text; iOS shows a generic ping alert (see [Known Limitations](#known-limitations)).

![TeamsRelay demo](TeamsRelay.gif)

## Prerequisites

- **Windows PC** running the Microsoft Teams desktop app
- **[KDE Connect](https://kdeconnect.kde.org/)** installed and paired on both PC and phone (same network)

GitHub Releases include two Windows ZIP variants:

- `TeamsRelay-win-x64-self-contained-v<version>.zip` — recommended, no .NET install needed
- `TeamsRelay-win-x64-v<version>.zip` — smaller, requires matching .NET runtime

## Quick Start

> Using a source checkout? Replace `TeamsRelay.exe` with `just` or `.\tr.cmd`. See [Development](#development).

```powershell
TeamsRelay.exe config init     # 1. create config\relay.config.json (once)
TeamsRelay.exe doctor          # 2. verify KDE Connect + device pairing
TeamsRelay.exe devices         # 3. list paired devices
TeamsRelay.exe start           # 4. start relaying in the background
```

Every Teams notification banner now forwards to your paired device(s). If `deviceIds` is empty in your config, TeamsRelay prompts you to pick a device on first start.

```powershell
TeamsRelay.exe status          # is the relay running?
TeamsRelay.exe logs --follow   # watch notifications live
TeamsRelay.exe stop            # stop the relay
```

Use `run` instead of `start` for foreground mode (Ctrl+C to stop).

## Configuration

Settings live in `config\relay.config.json`. Run `config init` to generate defaults.

<details>
<summary>Default config</summary>

```json
{
  "version": 1,
  "source": {
    "kind": "teams_uia",
    "captureMode": "strict"
  },
  "target": {
    "kind": "kde_connect",
    "kdeCliPath": "kdeconnect-cli",
    "deviceIds": []
  },
  "delivery": {
    "mode": "full_text",
    "genericPingText": "New Teams activity",
    "maxMessageLength": 220,
    "filter": {
      "directMessages": true,
      "conversationMessages": true,
      "unknownTypes": true
    },
    "format": {
      "template": null,
      "directMessageTemplate": "{sender} | {message}",
      "conversationMessageTemplate": "{sender}: {message} | {conversationTitle}",
      "fallbackTemplate": "{text}"
    }
  },
  "runtime": {
    "logLevel": "info",
    "memorySnapshotIntervalSeconds": 0,
    "uiaSubscriptionMode": "both"
  }
}
```

</details>

| Setting | Default | Notes |
| ------- | ------- | ----- |
| `source.captureMode` | `strict` | `strict` captures clear Teams notification events only. `hybrid` is more permissive. |
| `target.kdeCliPath` | `kdeconnect-cli` | Change only if the CLI isn't on your PATH. |
| `target.deviceIds` | `[]` | Empty = prompt at startup. Add device IDs from `devices` output to skip the prompt. |
| `delivery.mode` | `full_text` | `generic_ping` sends a fixed alert instead of message text (for privacy). |
| `delivery.genericPingText` | `New Teams activity` | Alert text used in `generic_ping` mode. |
| `delivery.maxMessageLength` | `220` | Messages longer than this are trimmed. Range: 20-2000. |
| `delivery.filter.directMessages` | `true` | Forward one-to-one chat notifications. |
| `delivery.filter.conversationMessages` | `true` | Forward group/channel notifications. |
| `delivery.filter.unknownTypes` | `true` | Forward app notifications (Viva Insights, Updates, etc.) and anything the parser can't classify. |
| `delivery.format.template` | `null` | Shared template. Variables: `{sender}`, `{message}`, `{conversationTitle}`, `{text}`. |
| `delivery.format.directMessageTemplate` | `{sender} \| {message}` | Set to `null` to fall back to `template`. |
| `delivery.format.conversationMessageTemplate` | `{sender}: {message} \| {conversationTitle}` | Set to `null` to fall back to `template`. |
| `delivery.format.fallbackTemplate` | `{text}` | Last resort before `genericPingText`. Uses cleaned raw text. |
| `runtime.logLevel` | `info` | `debug` for troubleshooting. |
| `runtime.memorySnapshotIntervalSeconds` | `0` | Set to e.g. `60` to log memory/queue stats periodically. |
| `runtime.uiaSubscriptionMode` | `both` | Diagnostic. `window_opened_only` or `structure_changed_only` to isolate event sources. |

Templates fall through: if a type-specific template references a missing variable, TeamsRelay tries the next template rather than rendering broken output.

## CLI Reference

| Command | Description |
| ------- | ----------- |
| `run` | Foreground relay (Ctrl+C to stop) |
| `start` | Background relay |
| `stop` | Stop relay and clean up processes |
| `status` | Check if relay is running |
| `devices` | List paired KDE Connect devices |
| `logs [--follow]` | View or tail the notification log |
| `doctor` | Health check |
| `config init` | Generate default config |

<details>
<summary>Full syntax</summary>

```
teamsrelay run [--device-name <name>]... [--device-id <id>]... [--config <path>]
teamsrelay start [--device-name <name>]... [--device-id <id>]... [--config <path>]
teamsrelay stop [--timeout-seconds <n>] [--force]
teamsrelay status
teamsrelay devices [--config <path>]
teamsrelay logs [--follow]
teamsrelay doctor [--config <path>]
teamsrelay config init [--path <path>] [--force]
```

</details>

## Known Limitations

| Limitation | Details |
| ---------- | ------- |
| **iOS: generic ping only** | KDE Connect iOS shows "Ping received" instead of message text. This is a [KDE Connect iOS limitation](https://apps.apple.com/app/kde-connect/id1580245991). Android shows full content. |
| **Windows only** | Uses Windows UI Automation APIs. No macOS/Linux support. |
| **Desktop app only** | Teams in a browser won't trigger captures. |

## Troubleshooting

**No devices found** — Open KDE Connect on both PC and phone. Ensure same Wi-Fi and paired. Try `kdeconnect-cli -l` to verify the CLI works.

**Notifications not appearing on phone** — Check Teams *Settings > Notifications* shows desktop banners (not just badges). Verify KDE Connect has notification permissions on the phone.

**"kdeconnect-cli" not recognized** — Find your KDE Connect install directory and either add it to PATH or set `target.kdeCliPath` in config.

**Relay stops on its own** — Check `logs`. Common causes: Teams closed, KDE Connect disconnected, PC went to sleep.

**Repeated text in forwarded notifications** — TeamsRelay normalizes duplicated segments. If you still see odd text, set `runtime.logLevel` to `debug`, reproduce, and check the raw payload in the logs.

**RAM usage growing** — Set `runtime.memorySnapshotIntervalSeconds` to `60` and watch `logs --follow` for `memory_snapshot` entries showing working set and queue depths.

## Runtime Files

Working files under `runtime\` are managed automatically.

| File | Purpose |
| ---- | ------- |
| `runtime\logs\relay-*.log` | Notification log (JSON lines) |
| `runtime\state\relay.pid` | Running relay PID |
| `runtime\state\relay.meta.json` | Session metadata |
| `runtime\state\relay.stop` | Graceful shutdown signal |

## Development

### Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- Optional: [`just`](https://github.com/casey/just) command runner (or use `.\tr.cmd`)

### Build, test, run

```powershell
just build                  # or: dotnet build TeamsRelay.sln
just test                   # or: dotnet test
just start                  # start relay from source
just stop                   # stop it
just doctor                 # health check
just devices                # list devices
just logs                   # view logs
```

### Publishing

```powershell
just publish-self-contained   # recommended: artifacts\publish-self-contained\TeamsRelay.exe
just publish                  # framework-dependent: artifacts\publish\TeamsRelay.exe
```

### Project structure

```
src/
  TeamsRelay.App/                          CLI, commands, device selection
  TeamsRelay.Core/                         Config, pipeline, state, logging, relay loop
  TeamsRelay.Source.TeamsUiAutomation/      Teams notification capture via UI Automation
  TeamsRelay.Target.KdeConnect/            KDE Connect discovery and delivery
tests/
  TeamsRelay.Tests/                        Unit tests (xUnit)
```

## Contributing

- **Bug?** [Open an issue](../../issues) with repro steps.
- **Feature idea?** Open an issue first to discuss.
- **Code?** Fork, change, run `just ci`, then open a PR.

## License

[GPL-3.0-only](LICENSE)
