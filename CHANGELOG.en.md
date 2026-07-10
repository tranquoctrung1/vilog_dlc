# Changelog

All notable changes to VilogLDC between v1.2 and v1.3.

## [1.3] - 2026-07-09

### Fixed
- **Send-queue deadlock** — no timeout on device ack wait, could hang forever if device never replied. Added 15s timeout (`WaitTimeoutMs`), also unblocks on write exception.
- **Ack detection false positives/misses** — matched bare `"K"` (false match mid-word, e.g. `CLOCKLOG`) and didn't handle a device response split across multiple `DataReceived` events (`"O"` + `"K"`). Now matches full `"OK"`/`"ERROR"` against an accumulated receive buffer, cleared per command.
- **Cross-thread races** — `isWaiting`/`sending` flags and raw `SerialPort.Write` were touched from both the UI thread and the background `SendLoop` without synchronization. Added `stateLock`/`serialLock` and thread-safe `IsWaitingFlag`/`SendingFlag` properties; all writes now wrapped in `SyncLock serialLock`.
- **Parser crashes on malformed device lines** — `PUBTOPIC`/`TDC`/`CLOCKLOG` read-back lines threw unhandled exceptions if malformed/truncated. Wrapped each in `Try/Catch`, malformed lines are now ignored instead of crashing the app.
- **NIC index crash** — `nics(1)` threw on machines with 0 or 1 network interfaces. Falls back to `nics(0)` or skips the MAC check if none available.
- **Stuck `isConnecting` flag / ports left open** — a failed `SerialPort1.Open()` didn't reset `isConnecting`, so a later stray "OK" byte could be misread as a successful connect. Ports were also never closed on app exit. Fixed the reset path and added a `Form1_FormClosing` handler that closes all 3 ports and stops `SendLoop`.
- **Hour+1 rollover bug** — `Hex(TS.Hour + 1)` produced an invalid hex byte (`"18"`) when hour rolled from 23 to 24. Fixed with `Mod 24`.
- **Line-split parsing assumed `vbNewLine` only** — devices sending bare `vbLf` line endings broke parameter parsing. Now normalizes `vbCrLf`/`vbCr` to `vbLf` before splitting.
- **Year encoding fragile** — relied on `Hex(Year)` always being exactly 3 hex digits (via `Mid` slicing). Replaced with a robust zero-padded 2-byte hi/lo hex encoding.
- **UI freeze on connect/config/read** — `Thread.Sleep(1000/5000)` calls on the UI thread blocked the whole app during the connect handshake and config/read commands. Handshake moved to `Task.Run`; blocking sleeps after fire-and-forget writes removed (response handling is async via `DataReceived`).
- **24h send-time bug** — `Case "24h"` incorrectly computed `60 * 12` (duplicate of the 12h case). Fixed to `60 * 24`.

### Added
- **Auto-reconnect** on `"Password Incorrect"` from the device — silently tears down (drains send queue, joins send thread, closes port) and reconnects automatically instead of showing a blocking `MessageBox`. Guarded by `isAutoReconnecting` to prevent overlapping reconnect cycles.
- **sendTime/logTime dependency filter** — new `RebuildLogTimeItems`, `SendTimeToMinutes`, `LogTimeToMinutes` helpers dynamically filter the logTime dropdown (ComboBox4) based on the selected sendTime (ComboBox3), so only combinations respecting the device's ~14-sample buffer limit (`sendTime / logTime ≤ 14`) are selectable. Wired to `ComboBox3`/`ComboBox5` `SelectedIndexChanged`.
- **New sendTime options** — `"1m"` and `"5m"` added (previously started at 10m).
- **New logTime options** — `"5m"` and `"10m"` hex mappings added (`00 05`, `00 0A`).
- **Config() pre-check** — `BtnConfigure_Click` now validates ID / Site Name / Meter / Cycle Send / Cycle Log are filled before configuring; shows a Vietnamese warning dialog listing missing fields if not.
- **Serial port timeouts** — `WriteTimeout`/`ReadTimeout` = 2000ms added to both SerialPort1 and SerialPort2, preventing an indefinite block on a stalled port.
- **Error visibility** — errors that were previously silently swallowed (port open errors, write errors, debug send errors, file save errors) are now surfaced to the log box (RichTextBox2).
- Status messages (Connected / Loading parameter / Vilog is Stopped / Load parameters completed) now **append** to the log instead of overwriting it.

### Changed
- **Server address** — `AT+SERVADDR` endpoint switched from `157.66.81.22:1883` to `43.216.183.205:1883` (old value left commented out, not deleted).
- **Raw response logging** — every device response chunk is now logged to RichTextBox2, not only while `isConfiguring`.
- **Button15 repurposed** — was "Sync Time" (`AT+TIMESTAMP=...`), now "Send Data" (`AT+GETSENSORVALUE=1`). Designer label updated to match.
- Error message "Lỗi gửi" changed to English "Send error".

### Renamed (no behavior change)
- `ReceivedText1/2/3` → `ProcessLoggerResponse` / `ProcessDebugResponse` / `ProcessMonitorResponse`
- `SerialPort1/2/3_DataReceived` → `SerialPortLogger/Debug/Monitor_DataReceived`
- Button click handlers renamed from generic (`Button1_Click_1`, `Button9_Click`, ...) to descriptive names (`BtnConnect_Click`, `BtnConfigure_Click`, `BtnSendDebugCommand_Click`, `BtnRefreshConfigPorts_Click`, `BtnRefreshDebugPorts_Click`, `BtnOpenDebugPort_Click`, `BtnCloseDebugPort_Click`, `BtnBrowseDebugFolder_Click`, `BtnReadDebugFile_Click`, `BtnSaveDebugFile_Click`, `BtnLoadParameters_Click`, `BtnGetSensorValue_Click`, `BtnStopLogger_Click`, `BtnSendData_Click`)
- `RichTextBox1_KeyDown` → `DebugCommandInput_KeyDown`
- Extracted `RebrandDeviceNameText` / `RebrandLoggerText` / `RebrandDebugText` helper functions (dedup of inline `"AT+"`→`"VILOG_"`/`"Vilog_"` and `"DRAGINO"`→`"BAVITECH"` replace logic).

## [1.2] - 2026-06-15
Baseline version prior to the above changes.
