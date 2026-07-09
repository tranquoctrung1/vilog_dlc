# 01 Developer Summary — logTime dropdown filtering by sendTime + sensor

## Task
Enforce the Dragino data-logger's ~14-buffered-samples-per-send hardware limit at the UI
level, replacing the old last-second `Config()` override with a proactive filter on the
logTime dropdown (ComboBox4) driven by the sendTime dropdown (ComboBox3).

## Files changed
- `SerialCom/Form1.vb`
- `Note.txt` (changelog line)

`Form1.Designer.vb` was **not** touched. Event handlers are wired with `Handles` in code
(the convention used elsewhere in this file, e.g. `TabControl1_SelectedIndexChanged`), so
no designer edit is required. ComboBox3/ComboBox5 are already declared `Friend WithEvents`
in the designer, which is all `Handles` needs.

## What changed in Form1.vb

### New helpers + handlers (added after `Config()`)
- `SendTimeToMinutes(label)` — single source of truth mapping ComboBox3 labels to minutes
  (1m..24h). Mirrors the existing AT+TDC / sendTime tables; no divergent second table was
  introduced — the existing per-branch `Select Case` blocks are left as-is and remain
  consistent with this function.
- `LogTimeToMinutes(label)` — ComboBox4 label -> minutes (5/10/15/30/60).
- `LogTimeCandidatesForSensor()` — sensor-aware candidate set:
  - **Aichi Tokei** -> `{15m, 30m, 60m}` (its `intervalLogTime` hex table only encodes
    15/30/60m — 5m/10m are intentionally excluded).
  - **Krohne / Level Sensor (BVT)** -> `{5m, 10m, 15m, 30m, 60m}` (full set their
    `logTime` `Select Case` supports).
- `RebuildLogTimeItems()` — rebuilds ComboBox4.Items from the sensor candidate set,
  keeping only labels where `CInt(sendMin / logMin) <= 14`.
- `ComboBox3_SelectedIndexChanged` and `ComboBox5_SelectedIndexChanged` both call
  `RebuildLogTimeItems()`.

### Filtering formula
`keep logTime  ⇔  CInt(sendTimeMinutes / logTimeMinutes) <= 14`

This uses the **same division** (`CInt(sendTime / logTime)`) that `Config()` already uses
to compute `setTime`, so the value validated in the UI equals the sample count actually
sent to the device via `AT+CLOCKLOG`. (For all real label combos the quotient is an
integer or a `.5` value far from the 14 boundary, so `CInt` rounding never flips a
keep/drop decision.)

### Empty-list edge case
If no candidate passes (only possible for very long sendTime, e.g. a hypothetical 24h ->
`1440/60 = 24 > 14`), the filter falls back to the **last candidate (60m)**, which
minimizes the ratio, so the dropdown is never empty. When no sendTime is chosen yet
(`sendMin <= 0`) the full sensor-supported set is shown unfiltered.

### Selection preservation
After rebuild, the previous ComboBox4 selection is kept if it survives the filter;
otherwise `SelectedIndex = 0` (smallest valid logTime — still valid because every item in
the list already passed the `<=14` check).

### Old `Config()` override kept as defensive fallback
The lines ~469-473 (`12h -> 60m`, `6h + 15m -> 30m`) were **kept** (with an explaining
comment), not removed. Rationale: the AT+CFG read-back parser in `ReceivedText1`
(lines ~243-264) sets `ComboBox3.SelectedItem`, `ComboBox5.SelectedItem`, and
`ComboBox4.SelectedItem` directly. Although those set-calls now also fire the new
handlers, keeping the last-second guard is a cheap safety net for any path that lands an
out-of-limit combo into ComboBox4. Task point 4 explicitly permits keeping it as a
defensive fallback; I chose that over full removal because the read-back path makes full
removal non-obviously safe.

## Sensor branches affected
- **Aichi Tokei** — logTime candidates now restricted to 15/30/60m via the sensor-aware
  set; the new filter reproduces the old 12h->60m and 6h(->drops 15m) behavior, so the
  legacy override is now redundant for Aichi (but retained defensively).
- **Krohne** — now offers 5/10/15/30/60m filtered by the `<=14` rule.
- **Level Sensor (BVT)** — same candidate set/filtering as Krohne.

## Behavior change to flag for review
For **Krohne / BVT**, the logTime dropdown can now surface **5m and 10m**, which the
designer's static ComboBox4 list (`15m/30m/60m`) did not previously offer. This is
intentional — both branches' `logTime Select Case` already encode 5m/10m — but it is a
user-visible expansion of available options, so it warrants a protocol-review sanity check.

## Verification status
No build tool was run — `msbuild`/`devenv` availability was not confirmed in this
environment, so **compilation was not verified**. Logic was reasoned through by hand
against every ComboBox3 label. Real-hardware verification (actual sendTime/logTime combo
pushed to a Dragino + flowmeter) is still required.

## Follow-up: reviewer findings 02 addressed

### Finding 1 (MEDIUM regression) — read-back CLOCKLOG silently dropped
`ProcessLoggerResponse` set `ComboBox3`/`ComboBox5` before `ComboBox4`, so the
`RebuildLogTimeItems` those two fire could trim the device's real logTime label out of
`ComboBox4.Items` before the CLOCKLOG line tried to select it — WinForms then no-ops the
`SelectedItem` assignment, leaving a stale/wrong logTime displayed (and pushable back to
the device on next Configure).
**Fix (approach b — faithful display wins on read-back):** in the CLOCKLOG branch
(`SerialCom/Form1.vb`), build the label first, and `If Not ComboBox4.Items.Contains(logLabel)
Then ComboBox4.Items.Add(logLabel)` before `ComboBox4.SelectedItem = logLabel`. This only
touches `Items` transiently — any later manual sendTime/sensor change re-runs
`RebuildLogTimeItems`, which rebuilds `Items` from the sensor-aware candidate set, so the
proactive filter is not permanently corrupted. Chose (b) over the suppress-flag approach (a)
because re-running the filter after parsing would re-trigger the same drop unless the label
were re-added anyway; (b) is the minimal direct fix.

### Finding 2 (LOW consistency) — missing 1m/5m sendTime cases for Krohne/BVT
Added `Case "1m" : sendTime = 1` and `Case "5m" : sendTime = 5` to **both** the Krohne and
BVT `sendTime` Select Case blocks in `Config()`, so `setTime = CInt(sendTime / logTime)`
is computed from the real interval and matches what `SendTimeToMinutes` (and thus the UI
filter) assumes. Previously these fell through to the `sendTime = 10` initializer.

### Optional hardening — skipped
Did **not** add `Case Else` guards to the Aichi `intervalLogTime` / three `logTime` Select
Case blocks. They are unreachable-by-construction and adding them was flagged optional; kept
the diff minimal to avoid scope creep.

### Verification
Compilation still not verified (no build tool in env). Read-back fix reasoned by hand
against the 6h/15m regression scenario in the finding; Finding 2 fix mirrors the existing
`SendTimeToMinutes` table exactly.

## Follow-up: UI ordering gate (sendTime before logTime)

### Gap
The filter (`RebuildLogTimeItems`) rebuilt ComboBox4's items when ComboBox3/ComboBox5 changed,
but ComboBox4 was enabled at form load, so a user could pick a logTime before any sendTime was
set — the ordering the filter assumes was not enforced in the UI.

### Change (2 edits, `SerialCom/Form1.vb`)
- `Form1_Load` (~line 117): added `ComboBox4.Enabled = False` so logTime starts locked.
- `ComboBox3_SelectedIndexChanged` (~line 903): after `RebuildLogTimeItems()`, set
  `ComboBox4.Enabled = (ComboBox3.SelectedIndex >= 0 AndAlso ComboBox3.Text <> "")` so logTime
  becomes selectable only once sendTime is chosen.

### ComboBox5 gating — decided NO
`LogTimeCandidatesForSensor()` falls through to `Case Else` (full 5/10/15/30/60m set) when
ComboBox5.Text is empty, so `RebuildLogTimeItems` already behaves sensibly with no sensor
selected. Gating ComboBox4 on ComboBox5 too would be over-engineering; left as-is.

### Read-back path (ProcessLoggerResponse CLOCKLOG branch) — verified safe
On AT+CFG read-back, ComboBox3 is set (line 243) BEFORE the ComboBox4 CLOCKLOG assignment
(line 278). Setting ComboBox3 fires `ComboBox3_SelectedIndexChanged`, which re-enables
ComboBox4 (ComboBox3 now has a value) — so ComboBox4 is enabled by the time the CLOCKLOG branch
selects its label. Also, WinForms allows setting `.SelectedItem` on a disabled ComboBox anyway,
so even out-of-order the programmatic assignment is not blocked. Net: gate ends up correctly
True after read-back.

### Verification
Compilation not verified (no build tool in env). UI-gating logic reasoned by hand.
