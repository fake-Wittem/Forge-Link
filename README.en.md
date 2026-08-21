[简体中文](./README.md) | **English**

# ForgeLink

ForgeLink is a PLC data collection and forwarding system for Windows x64 industrial PCs and edge computers. This repository contains a buildable and runnable .NET 10 first release: an independent Collector Service performs collection, while the WPF desktop client displays devices, points, and real-time values through a local named-pipe management API.

## Core capabilities

- Separate Collector Service and WPF Desktop processes;
- Protocol-neutral `IPlcDriver`, `IHistoryChannel`, and configuration repository contracts;
- Asynchronous bounded-channel collection pipeline and thread-safe real-time cache;
- Versioned SQLite initialization for management configuration and encrypted connection credentials only, never point history;
- TDengine history gate, disabled by default with no SQLite fallback;
- A TDengine 3.x WebSocket foundation adapter using the official `TDengine.Connector` 3.2.1, with typed supertables, batch writes, and SQL queries;
- Light WPF UI theme, shared design tokens, and virtualized data grids;
- A shell with eight independent menu views, typed navigation, and page-scoped refresh lifecycles;
- Local health, status, device, point, and real-time value APIs;
- A simulated PLC driver for end-to-end demos without field equipment.
- An NModbus-based Modbus TCP driver with address areas, contiguous batch reads, Unit ID, timeouts, and register ordering;
- Full device/point CRUD, protected deletion, connection tests, and runtime hot reload;
- Point CSV import/export with transaction rollback and quoted-field support.
- A TDengine settings page with DPAPI-backed password storage, redacted reads, full connection testing, and explicit enable/disable controls;
- Six point-history policies, a 100,000-value/10-minute bounded memory buffer, 1,000-value batch writes, exponential retry, and gap metrics;
- A seven-day-bounded history query API plus Desktop numeric trend preview and virtualized details;

## Architecture

```text
ForgeLink Desktop
       │ Windows named pipe ForgeLink.Collector
       ▼
Collector Service ── SQLite configuration
       │
       ├─ Scheduler ── IPlcDriver ── PLC/simulator
       ├─ Bounded pipeline ── real-time memory cache
       └─ HistoryGate ── IHistoryChannel (disabled by default)
```

## Supported protocols

The repository implements both `Simulation` and an NModbus-based `Modbus TCP` driver. Modbus TCP passes an integration test against a local NModbus slave but has not yet been accepted against a specific field PLC model. See the [Modbus TCP configuration guide](./docs/modbus-tcp.md). OPC UA, Siemens S7, Modbus RTU, Mitsubishi MC, and Omron FINS remain future candidates.

## Environment and installation

- Windows x64;
- .NET SDK 10.0.400 or a compatible .NET 10 patch;
- .NET Desktop Runtime 10 and ASP.NET Core Runtime 10 for runtime deployment.

The WiX offline installer and Windows Service registration belong to the engineering delivery phase. The current development build starts from the command line.

## Quick start

```powershell
.\tools\verify.ps1
.\tools\run-dev.ps1
```

The second command starts the Collector Service in the background, waits for its health endpoint, and opens the desktop client. Closing the desktop client stops the service process created by the script.

## Configuration

- The IPC pipe defaults to `ForgeLink.Collector` and can be overridden with `ForgeLink__PipeName`;
- `ForgeLink__DataRoot` selects the development data directory;
- `%ProgramData%\ForgeLink` is used when no override is supplied;
- First startup seeds one de-identified simulated device and three demo points.
- The local TDengine development baseline is Enterprise 3.4.2.6 at `D:\TDengine`, using WebSocket `127.0.0.1:6041`; the connector does not depend on native DLLs from that directory;
- See the [TDengine configuration guide](./docs/tdengine.md) for compatibility and integration requirements.

## Data and security boundaries

- SQLite never stores collected point history;
- Disabled or unverified history is neither stored, queued, nor backfilled;
- PLC writes are rejected by default;
- The management API is exposed only through a local named pipe and does not listen on a TCP management port;
- `data/`, databases, logs, certificates, and private keys are excluded from source control;
- All sample addresses and values are simulated and de-identified.

## Development and testing

`ForgeLink.slnx` is the solution entry point. The verification gate restores dependencies, builds with warnings as errors, checks formatting, and runs xUnit tests. Current automated coverage includes engineering conversion, configuration validation, history gating, hot-reload signaling, CSV handling, protected deletion, batch transaction rollback, typed desktop navigation, and device/point editors.

Local TDengine 3.4.2.6 with `forge_link` has passed real authentication, schema creation, batch write, query, and test-table cleanup. The default root account is development-only; production still requires a dedicated least-privilege account. Real PLCs, TDengine failure recovery, REST/MQTT, Windows Service behavior, installers/upgrades, and 72-hour stability still require dedicated acceptance work.

## License and contributing

No project license has been selected yet. Run `.\tools\verify.ps1` before submitting changes and keep both README files structurally aligned. See [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md) for third-party licenses.
