# 01 Developer Summary — Unify logTime dropdown to 15m/30m/60m

## Goal
Make ComboBox4 (logTime/interval) offer the identical candidate set `{15m, 30m, 60m}` for
all sensors (Krohne, Aichi Tokei, BVT Level Sensor). Aichi was already effectively correct;
Krohne/BVT were wrongly offering a wider `5m/10m/15m/30m/60m` set from a prior session's
expansion. ComboBox3 (sendTime) static list `{15m,30m,1h,2h,3h,6h,12h}` is already shared
and unchanged.

## Change made
File: `SerialCom/Form1.vb`

`LogTimeCandidatesForSensor()` (~line 954) — the current code already returned the full
`{"5m","10m","15m","30m","60m"}` set unconditionally (an earlier session had already removed
the `Select Case ComboBox5.Text` branch described in the task; there was no branch left to
remove). Narrowed the returned array to `{"15m","30m","60m"}` and updated the stale comment
above it (~lines 949-953) that described the 5/10/15/30/60m set. Kept the function itself
(smallest sensible diff; `RebuildLogTimeItems` calls it and the 60m fallback still resolves
via `candidates(candidates.Length - 1)`).

File: `Note.txt` — appended one Vietnamese changelog line reverting the 5m/10m expansion.

## Verification of downstream code (no changes needed)
1. `LogTimeToMinutes()` (~938): still has 5/10/15/30/60 entries. Left as-is — harmless;
   RebuildLogTimeItems simply never passes 5m/10m now.
2. `RebuildLogTimeItems()` `<=14` filter (~961): re-checked every combo of the unified
   candidate set against the 7 sendTime options. Worst case 12h(720min): 15m→48 and 30m→24
   are filtered out (>14), 60m→12 passes, so the list is `{60m}` — non-empty. Every other
   sendTime keeps at least 60m (ratio <=14). No combo yields an empty filtered list, and the
   existing `allowed.Count = 0` fallback remains as belt-and-suspenders.
3. Krohne `Config()` `logTime` Select Case (~769-780): still has unreachable `"5m"`/`"10m"`
   cases; default `logTime = 5` (~768) is now only a theoretical fallback since the dropdown
   only yields 15/30/60m, all of which match a case. No regression — AT+CLOCKLOG /
   setTime math unaffected.
4. Krohne `Config()` `sendTime` Select Case (~742-766): `"1m"/"5m"/"10m"/"24h"` cases were
   already unreachable (ComboBox3's designer list never included them). Unchanged, no
   regression.
5. Aichi `Config()` `logTime` Select Case (~697-709): also retains unreachable `"5m"/"10m"`
   cases (added in the prior expansion per Note.txt line 20). Left in place per task scope.
6. Read-back CLOCKLOG branch (`ReceivedText1`, ~293-322): if a device reports a 5m/10m
   logTime, lines 315-317 transiently re-add the label to ComboBox4.Items so it still
   displays. Pre-existing graceful handling — no change, flagged as a known edge case.

## Flag for reviewer — dead code
The now-unreachable `"5m"`/`"10m"` (and `"1m"`/`"24h"`) `Case` branches in `Config()` for
Krohne and Aichi are dead-but-harmless. My recommendation: leave them. They cause no
misbehavior, and per project convention we don't prune working AT-command logic that might
be wanted back if the dropdown set changes again. Not worth the diff/risk to remove.

## Protocol note
This change only alters which UI candidate labels are offered; it does not alter any
AT-command byte string, register offset, COMMAND/DATACUT pairing, or hex/timestamp encoding.
The `setTime = CInt(sendTime/logTime)` value written in `AT+CLOCKLOG` for Krohne is bounded
by the same `<=14` invariant that RebuildLogTimeItems enforces, so no oversized setTime can
now be produced from the UI.

## Build / test status
No build tool availability was confirmed on this machine (msbuild/devenv not verified on
PATH) and this project has no automated test suite. **No build was run and no build is
claimed verified.** The only real test for protocol-layer behavior is manual verification
against real Dragino/flowmeter hardware.
