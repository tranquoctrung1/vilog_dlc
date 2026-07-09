# 02 Reviewer Findings — logTime dropdown filter (ComboBox4) by sendTime + sensor

Reviewed: `SerialCom/Form1.vb` — `Config()` (465-784), new helpers/handlers (786-885),
read-back parser `ProcessLoggerResponse` (149-278). Cross-checked against
`vilog-serial-protocol` skill and developer summary `01_developer_summary.md`.

Verdict: **1 real finding (medium/regression), 1 secondary (pre-existing, now more
exposed), plus optional hardening. Formula, empty-list, and threading checks are clean.**

---

## Finding 1 (MEDIUM — new regression in the AT+CFG read-back path)
**File/line:** `SerialCom/Form1.vb:253-268` (`ProcessLoggerResponse`, `AT+CLOCKLOG=` branch),
interacting with `RebuildLogTimeItems` at 841-876.

**What's wrong:** During read-back the parser sets, in device-output order,
`ComboBox3.SelectedItem` (TDC, line 243/245) and `ComboBox5.SelectedItem` (COMMAND1,
line 249/251) *before* it sets `ComboBox4.SelectedItem` from the CLOCKLOG value
(line 262/264). The ComboBox3/ComboBox5 assignments now synchronously fire
`RebuildLogTimeItems`, which rebuilds `ComboBox4.Items` down to only the filter-passing
labels. When the CLOCKLOG line then does `ComboBox4.SelectedItem = CStr(...) & "m"`,
WinForms **silently ignores** the assignment if that label was filtered out of `Items`
(SelectedItem left at whatever `RebuildLogTimeItems` set via `SelectedIndex = 0`).

**Concrete failure:** A Krohne device configured (before this build, or manually) with
sendTime = 6h, logTime = 15m. Read-back: `TDC=21600` → ComboBox3 = "6h" → rebuild drops
15m (360/15 = 24 > 14), leaving Items `{30m, 60m}`, SelectedIndex → "30m". Then
`ComboBox4.SelectedItem = "15m"` is silently dropped. **UI now shows 30m while the device
actually holds 15m.** This is a *new* regression: before this change ComboBox4 always
contained 15m/30m/60m (designer static list), so read-back of a 15m/30m/60m device always
displayed faithfully; the filter can now remove those labels. Worse, if the user then
clicks Configure, `Config()` reads the wrong `ComboBox4.Text` and pushes 30m back to the
device, overwriting the correct 15m — the misdisplay propagates to real hardware.

**Note:** the retained old override (475-479) does **not** cover this — it runs at
Config()/write time, not at read-back/display time.

**Fix:** In the CLOCKLOG read-back branch, guarantee the device-reported label is present
before selecting it, e.g. before the `ComboBox4.SelectedItem = ...` assignment do
`If Not ComboBox4.Items.Contains(lbl) Then ComboBox4.Items.Add(lbl)` (then assign), or set
a guard flag that makes `RebuildLogTimeItems` a no-op while `isReadingParameter` is true and
apply the filter once after parsing completes. Faithful display of device state must win
over the proactive filter on the read-back path.

---

## Finding 2 (LOW — pre-existing divergence, now more reachable)
**File/line:** `Config()` Krohne sendTime Select Case `SerialCom/Form1.vb:629-648` and BVT
`693-712`; vs `SendTimeToMinutes` `796-812`.

**What's wrong:** The filter's `SendTimeToMinutes` maps `"1m"→1` and `"5m"→5`, but the
Krohne/BVT `sendTime` Select Case in `Config()` has **no `"1m"` or `"5m"` case** and falls
through to the initializer `sendTime = 10`. So for a Krohne/BVT config with ComboBox3 =
"1m" or "5m", `setTime = CInt(sendTime / logTime)` is computed from 10 minutes, not the
real interval, while `AT+TDC` (741-767) sends the true 60s/300s. The filter therefore does
**not** validate the same number `Config()` actually sends for these two labels.

**Impact:** This does not cause a false-allow of an over-limit combo (small sendTime never
exceeds 14), so it is not a hardware-limit hole — but the filter's promise of "same division
as Config" (dev summary line 38) holds only for sendTime ≥ 10m. The underlying missing
1m/5m cases are a pre-existing `Config()` bug; the new filter surfaces 1m/5m + low logTime
combos more prominently. Recommend either adding `"1m"`/`"5m"` cases to the two Select Case
blocks (so setTime is computed from the real interval) or documenting that 1m/5m sendTime is
unsupported for Krohne/BVT and excluding them. Not blocking for this change.

---

## Optional hardening (not blocking)
- `Config()` Aichi `intervalLogTime` Select Case (485-492) and all three `logTime` Select
  Cases (584-595, 651-662, 715-726) silently keep their initializer default on no-match. The
  sensor-aware candidate split correctly prevents 5m/10m from reaching Aichi under normal UI
  flow (verified: Aichi candidates `{15m,30m,60m}`, and switching sensor re-runs the filter
  which discards a prior "5m"/"10m"). But given Finding 1 can leave ComboBox4 in an
  unexpected state, adding an explicit `Case Else` that logs/guards would make the silent
  fall-through (which would send CFGDEV interval 15m while CLOCKLOG sends a different value)
  impossible rather than merely unreachable-by-construction.

---

## Checks that passed (clean)
- **Formula (Point 2):** filter uses `CInt(sendMin / logMin) <= 14`, identical operation to
  `setTime = CInt(sendTime / logTime)` in the Krohne (670) and BVT (734) branches. For all
  limit-relevant labels (sendTime ≥ 10m) the two tables agree, so no off-by-one / rounding
  divergence at the 14 boundary. (Divergence at 1m/5m is Finding 2, and never flips a
  keep/drop decision.)
- **Aichi encodability (Point 3):** `intervalLogTime` genuinely encodes only 15/30/60m;
  candidate set `{15m,30m,60m}` for Aichi prevents 5m/10m selection, and a sensor switch
  re-filters away a stale 5m/10m. No fall-through to silent default under normal flow.
- **Old override (Point 4, conflict check):** override (475-479) is idempotent with the
  filter — for 12h and 6h it forces exactly the value the filter already leaves selectable,
  so no double-adjust or oscillation. It is dead-but-harmless for Aichi and a no-op for
  Krohne/BVT selections already constrained by the filter. Its stated purpose (guarding the
  read-back path) is not actually achieved — see Finding 1.
- **Empty dropdown / IndexOutOfRange (Point 5):** `RebuildLogTimeItems` falls back to the
  last candidate when nothing passes (859-861) and only sets `SelectedIndex = 0` when
  `Items.Count > 0` (873-874). No sendTime × sensor combo yields an empty list or an
  out-of-range index.
- **Threading (Point 6):** `ProcessLoggerResponse` marshals to the UI thread via the
  `InvokeRequired`/`Me.Invoke` guard at 150-152, so all ComboBox assignments and the
  handler-triggered `RebuildLogTimeItems` run on the UI thread. `RebuildLogTimeItems` touches
  no serial state and is never called from `SendLoop`/`DataReceived` off-thread. No
  cross-thread access and no reentrancy hazard.
