# Protocol Review: AT+TDC parse-loop Try/Catch fix (Bug #5)

**File:** `SerialCom/Form1.vb`, lines ~243-257 (read-back parse loop in `ProcessLoggerResponse`, `isReadingParameter` branch)
**Change:** Wrap the `AT+TDC=` line parse in Try/Catch, silently ignoring a malformed/truncated line.

**Verdict: PASS — no protocol or threading defects. Ship it.**

---

## Verification against the review checklist

### 1. Diff applied correctly / matches sibling style — CONFIRMED
- The TDC block (243-257) now mirrors the `AT+PUBTOPIC=` block (232-241) and `AT+CLOCKLOG=` block (263-292): `Try` around the `Substring` + parse, `Catch ex As Exception` with an "ignore malformed ... line" comment. Style and intent are consistent with the established Bug #5 pattern.
- `Substring(7)` correctly strips the 7-char prefix `AT+TDC=` (A,T,+,T,D,C,= = 7 chars). No off-by-one; the cut and the `Contains("AT+TDC=")` guard agree.
- The `CInt("")` / `CInt(<non-numeric>)` throw described in the background is exactly what the Try now contains (InvalidCastException / FormatException). Fix addresses the reported crash.

### 2. Stale ComboBox3 risk on swallowed exception — ACCEPTABLE (matches siblings)
- Send path (lines 772-799) reads `ComboBox3.Text` and only emits `AT+TDC=` when the text matches a known label ("1m".."24h"); otherwise it sends nothing. It does **not** read `ComboBox3.SelectedItem` set by the parse loop.
- On a swallowed malformed TDC line, ComboBox3 keeps its prior value (stale display), identical to how PUBTOPIC leaves TextBox11/12 stale and CLOCKLOG leaves ComboBox4 stale. This is a rare-path display-fidelity issue, not a correctness regression, and it is consistent across all three sibling blocks. The device's actual TDC is unchanged unless the operator explicitly clicks Config to re-send.
- No risk of transmitting a wrong interval as a *direct result* of this catch: a stale ComboBox3 only ever produces a value the operator can see and must actively confirm before any `AT+TDC=` is queued.

### 3. Threading — CONFIRMED SAFE
- `SerialPort1.DataReceived` -> `ProcessLoggerResponse` guards with `If Me.RichTextBox2.InvokeRequired` at line 154 and re-invokes itself onto the UI thread. The entire `isReadingParameter` parse block (including the new TDC code touching `ComboBox3`) therefore runs on the UI thread. The new code adds no direct cross-thread control access. No marshaling concern introduced.

### 4. Other correctness — none found
- `isWaiting`/`sendQueue` handshake untouched; no `Thread.Sleep` reintroduced.
- No hex-encoding or COMMAND/DATACUT pairing involved in this diff.
- Catch is broad (`Exception`) but that matches the sibling blocks and is appropriate here (best-effort display parse).

---

## Optional (non-blocking) note
If future hardening is wanted, the stale-display gap common to all three Bug #5 blocks could be closed by clearing/resetting the target control in the `Catch`. Out of scope for this fix and should be applied uniformly across siblings if done at all — not required for this change.
