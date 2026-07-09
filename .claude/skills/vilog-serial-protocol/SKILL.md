---
name: vilog-serial-protocol
description: Domain knowledge for the AT-command / Modbus protocol used by VilogLDC to configure Dragino NB-IoT data-loggers and read Aichi Tokei / Krohne / Bavitech flowmeter sensors over serial. Load before writing or reviewing any code that builds AT+ commands, COMMAND/DATACUT pairs, hex-encodes values, or parses device responses in Form1.vb.
---

# VilogLDC Serial Protocol

## Why this exists

`Form1.vb` builds and parses raw AT-command strings by hand (string concatenation, `Hex()`, manual substring parsing). There is no protocol library, no schema, and no test harness — the only way a mistake surfaces is a misbehaving physical device in the field. This skill is the protocol reference so changes can be checked against the real spec instead of "it compiles."

## Two serial links

- **SerialPort1** — talks to the Dragino NB-IoT data-logger (`AT+...` commands). This is the configuration/control channel. Responses come back through `ReceivedText1`.
- **SerialPort2** — a secondary/local debug link (`ReceivedText2`), where outgoing `AT+` is displayed to the user as `Vilog_...` (and translated back on send).
- **SerialPort3** — a monitor/log link (`ReceivedText3`), read-only, used for `popupMonitorScreen`.

Text display convention: `AT+` is shown to the user as `VILOG_` (link 1) or `Vilog_` (link 2/3) — purely cosmetic rebranding, translated back to `AT+` before actually writing to the port. Don't confuse the two spellings; they're the same substitution used inconsistently (`VILOG_` uppercase on link 1, `Vilog_` mixed-case on link 2/3) — preserve whichever the surrounding code already uses.

## The send queue / handshake pattern

Commands aren't fired one after another blindly. `AddToQueue(data, delayMs)` enqueues a `SendItem`; `SendLoop` (running on `sendingThread`) dequeues and writes one at a time, gated by the `isWaiting` flag:

1. `SendLoop` sets `isWaiting = True` right before `SerialPort1.Write(item.Data)`.
2. `isWaiting` is only cleared back to `False` inside `ReceivedText1` when the response contains `"K"` (see the `isConfiguring` branch) — i.e. the loop won't send the next queued command until the device's ack arrives.
3. `Note.txt` documents this explicitly: the app used to wait with `Thread.Sleep`, then was changed to wait for the device's OK flag instead — **don't reintroduce blind `Thread.Sleep`-based waiting for the send queue**; the `delayMs` field on `SendItem` is currently unused/vestigial for this reason.
4. If you add a new command to `Config()`, add it via `AddToQueue`, not a raw `SerialPort1.Write`, so it respects the queue/handshake.

## COMMAND / DATACUT / CMDDL triplets (per Modbus request slot)

Each numbered slot (`1`..`F`, hex digits) configures one Modbus polling request on the Dragino device:

- `AT+COMMANDn=<addr> <func> <reg_hi> <reg_lo> <count_hi> <count_lo>,1` — raw Modbus request frame: device address (hex), function code (`03`=read holding registers, `04`=read input registers), register address (2 bytes), register count (2 bytes).
- `AT+DATACUTn=<offset>,<len>,<range>` — tells the Dragino how to slice the Modbus response to extract the payload. **The `len`/`range` here must be consistent with the byte count implied by `COMMANDn`'s register count** (each register = 2 bytes). If you change a `COMMAND`'s register count, its paired `DATACUT` must change too — this is the single most common source of silent bugs in this file.
- `AT+CMDDLn=<ms>` — inter-command delay in milliseconds for that slot.

Slot letters are hex (`1`-`9`, `A`-`F`), giving up to 15 polling slots per configuration.

## Per-device-type command sets (all built in `Config()`)

| `ComboBox5` value | Function code | Notes |
|---|---|---|
| `Aichi Tokei` | `03` (read holding registers) | Up to 15 slots (`1`..`F`), each covering a different Aichi register block. Also sets device clock (`CFGDEV` sub-commands 0x0B/0x0C/0x0D = year/month-day/hour-minute) and start-logging time (0x01/0x02/0x03/0x04) via hex-encoded `DateTime.Now` fields. |
| `Krohne` | `04` (read input registers) | 4 fixed slots for flow rate / totalizer registers. |
| `Level Sensor (BVT)` (Bavitech) | `03` | Single slot, register `0002`, 1 register. |

When adding a new flowmeter type, follow the existing pattern: register offsets for `COMMANDn`/`DATACUTn` come from that device's Modbus register map (in its datasheet) — never guess an offset from a similar-looking device.

## Hex/timestamp encoding conventions

- All numeric fields inside Modbus command bytes and `CFGDEV` timestamp bytes are zero-padded to **2 hex digits**: `Microsoft.VisualBasic.Right("00" & Hex(x), 2)`. A missing pad silently shortens the frame and desyncs every byte after it — always check new hex-building code pads the same way.
- Device timestamp sync (`Button15_Click_1`, `AT+TIMESTAMP=`) uses Unix epoch seconds computed via `DateDiff("s", "01/01/1970 00:00:00", ...)` adjusted for the local UTC offset — not `DateTimeOffset.ToUnixTimeSeconds()`. Match this existing approach rather than introducing a different epoch calculation, since the device's own clock-sync logic (`CFGDEV` hour+1 offset in `Config()`) is tuned against it.
- `AT+TDC=<seconds>` sets the reporting interval; `ComboBox3` maps human labels (`"1m"`, `"6h"`, etc.) to seconds — the mapping table lives inline in `Config()`; extend it there if adding new interval options, keeping both the seconds mapping and any `ComboBox4` cross-adjustment (e.g. `12h` forces logging interval to `60m`) in sync.

## Parsing device responses (`ReceivedText1`, `isReadingParameter` branch)

When reading back device config (`AT+CFG`), the response is split on newlines and scanned line-by-line for known prefixes (`AT+PUBTOPIC=`, `AT+TDC=`, `AT+COMMAND1=`, `AT+CLOCKLOG=`). If you add a new configurable parameter, add both directions: the write side in `Config()` and the read-back parse side here, using the same prefix string, or the UI will silently fail to reflect the device's actual state after a reload.

## Cross-thread UI access

`SerialPort.DataReceived` fires on a background thread, and `SendLoop` runs on `sendingThread`. Any UI control access from these must go through `Invoke`/`InvokeRequired` (see `ReceivedText1`'s `Me.RichTextBox2.InvokeRequired` check, and `SendLoop`'s `Me.Invoke(New Action(Sub() ... End Sub))`). Missing this doesn't fail during casual testing — it intermittently throws `InvalidOperationException` or corrupts UI state under real serial traffic timing.
