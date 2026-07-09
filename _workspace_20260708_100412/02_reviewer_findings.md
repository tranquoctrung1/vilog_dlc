# Protocol Review — Phase 02 (fixes for bugs #1-8,10,13-15)

Scope: verified the 12 fixes in `SerialCom/Form1.vb` per the six review questions.
Out-of-scope items (sendTime/logTime/intervalLogTime/setTime, the "24h"/interval
`Select Case` blocks — bugs #9/#11/#12) were confirmed **untouched** and not assessed.

Overall: **substantially clean.** The core fixes are correct. No protocol-breaking or
deadlock defect found. Items below are advisory (low/medium), plus one change that needs
hardware/datasheet confirmation because it alters actual command bytes.

---

## Verified correct

1. **OK/ERROR buffer detection vs existing parsing (Q1)** — OK. The `isConfiguring`
   ack check (lines 205-208) runs on the accumulated `receiveBuffer` and clears it on
   ack; the `isReadingParameter` read-back parse (lines 222-276) uses the separate
   `DeviceParameterString` and is not affected. The two accumulators don't interfere.
   `"OK"` (not bare `"K"`) is matched, fixing the mid-word `CLOCKLOG` false match.
2. **SyncLock nesting / deadlock (Q2)** — No lock-order inversion. `stateLock` and
   `serialLock` are never nested (in `SendLoop`, `ClearReceiveBuffer` releases `stateLock`
   before entering `SyncLock serialLock`, lines 421→423). `Me.Invoke` calls happen after
   `serialLock` is released. No classic deadlock. (But see Finding 1 re: blocking under
   `serialLock`.)
3. **Task.Run handshake race (Q3)** — OK. `pwd` is captured on the UI thread (line 335)
   then used off-thread; the whole handshake is wrapped in `Try/Catch` (lines 337-348), so
   a concurrent `Button4` disconnect or `FormClosing` that closes the port makes the
   `Write` throw and is swallowed. `SendLoop` is not running during connect, so no
   `serialLock` contention there.
4. **Timeout queue recovery (Q4)** — Correct recovery: on timeout `IsWaitingFlag=False`
   (line 401); the next iteration dequeues and calls `ClearReceiveBuffer()` before the
   write (line 421), so a stale/late ack can't linger. (See Finding 2 on the threshold.)
5. **Hour rollover (Q5)** — `Hex((TS.Hour + 1) Mod 24)` (line 500) is correct: 23→0 →`"00"`,
   avoids the invalid `Hex(24)="18"` hour byte; 2-digit padding preserved.
6. **CrLf-tolerant split (Q5)** — Correct and correctly ordered: replace `vbCrLf`→`vbLf`
   *first*, then lone `vbCr`→`vbLf`, then split on `vbLf` (lines 225-226). No double-split.
7. **No time-calc code touched (Q6)** — Confirmed. The `intervalLogTime`/`logTime`/
   `sendTime`/`setTime` `Select Case` blocks (lines 474-482, 573-585, 618-664, 682-726)
   and the COMMAND/DATACUT/CMDDL slot definitions are unchanged.

---

## Findings (advisory)

### Finding 1 — MEDIUM: `serialLock` held across a blocking `SerialPort1.Write` on the UI thread
`Form1.vb` Button3 (841-843), Button12 (879-881), Button13 (888-890), Button14 (903-905),
Button15 (915-917), and `SendLoop` (423-425).
- `SerialPort1.WriteTimeout` is never set, so it defaults to `InfiniteTimeout`. If
  `SendLoop` is mid-`Write` under `serialLock` and the device/driver output buffer stalls
  (flow control, cable pulled), a UI-thread button click that also takes `serialLock`
  blocks the UI thread indefinitely — the same freeze class bug #15 set out to remove.
- Fix: set a finite `SerialPort1.WriteTimeout` (e.g. 2000 ms) once at open, and/or don't
  issue port writes from UI-thread handlers while a config run is active. Low likelihood in
  the lab, real in the field — matches the "intermittent, hardware-only" failure profile.

### Finding 2 — LOW: flat 15 s `WaitTimeoutMs` ignores per-command expected latency
`Form1.vb:39`, vs `AddToQueue` delays of `10000` for CFGDEV/CLOCKLOG (492-501, 586, 662, 726).
- 15 s > the 10 s the historical `delayMs` values imply for CFGDEV/CLOCKLOG acks, so it will
  not fire spuriously under normal slow responses — acceptable as-is.
- Margin is only ~5 s, though. If a CFGDEV register-write ack ever exceeds 15 s the queue
  advances early and the next command's ack detection can bind to the previous command's
  late "OK" (desync). Consider deriving the timeout from `item.DelayMs` (the now-vestigial
  field already encodes per-command expectation) plus a margin, rather than a flat constant.
  Not blocking — do not lower 15 s.

### Finding 3 — MEDIUM (needs hardware/datasheet confirmation): year encoding change alters emitted bytes
`Form1.vb:488-497` — `Right("0000" & Hex(TS.Year),4)` → `yearHi`/`yearLo`, e.g. 2026 → `07 EA`.
- This is the only fix that changes actual command bytes, and it is protocol-*consistent*:
  `AT+CFGDEV=01 06 01 0B XX YY` is a Modbus function-06 single-register write, whose value is
  a 16-bit register, so year hi/lo = `07 EA` for 2026 is the technically-correct 2-byte form,
  and each byte keeps the 2-hex-digit padding convention.
- I could NOT independently confirm the dev's "byte-identical to before" claim: there is no
  git history and the shipped `VilogLDC_26052026.exe` contains no `AT+` literals (predates
  this feature), so no prior-source diff was possible. Please confirm on real hardware that
  the Aichi/Dragino year register (0x010B and 0x0101) expects the 16-bit **hex** year
  (`07 EA`), not a decimal-split (`20 26`) or year-offset form, before shipping.

### Finding 4 — LOW / pre-existing: connect "OK" match may fire before password validation
`Form1.vb:169` — connect detection now matches `"OK"` on the accumulated buffer while
`isConnecting`. During the `Button1` handshake the device's `"OK"` reply to the bare `"AT"`
(sent before the password, line 340) can flip `isConnecting=False` and display "Connected"
before the password is actually accepted. The `"OK"` match predates this change, but
accumulating into `receiveBuffer` (bug #8) makes it slightly more likely. If field devices
distinguish pre/post-auth, prefer gating connect on `"Password Correct"` only. Not introduced
by this diff.

### Finding 5 — LOW / pre-existing (not in this diff): Krohne read-back case mismatch
`Form1.vb:250` — read-back parse matches lowercase `"AT+COMMAND1=01 04 0f a2 00 02"`, but
`Config()` writes uppercase `"01 04 0F A2 00 02"` (line 602). If the device echoes `AT+CFG`
in uppercase, the Krohne `ComboBox5` auto-select silently fails. Untouched by this change;
noted for a future pass (use case-insensitive compare).

### Trivial
- `receiveBuffer` is not cleared during read-parameter/idle states (only during connect and
  config), so it grows unbounded across a long session via line 158. Memory impact is
  negligible; optional to clear in `Button12`/on read-complete.
