# 02 Reviewer Findings — Unify logTime dropdown to 15m/30m/60m

**Verdict: CLEAN PASS.** No protocol or threading defects found. The change is a pure
UI candidate-set narrowing with no AT-command byte/hex/register impact. Details below.

## Scope reviewed
- `SerialCom/Form1.vb`: `LogTimeCandidatesForSensor` (954), `RebuildLogTimeItems` (961),
  `LogTimeToMinutes` (938), `SendTimeToMinutes` (919), Krohne/Aichi/BVT `Config()`
  Select Case blocks (697-792, 810+), CLOCKLOG read-back (293-322).
- `SerialCom/Form1.Designer.vb`: ComboBox3/ComboBox4 static Items.
- `Note.txt`.

## 1. `<=14` filter vs narrowed set — no empty list for any sendTime (CONFIRMED)
Candidate set `{15m(15), 30m(30), 60m(60)}` filtered by `CInt(sendMin/logMin) <= 14`
against ComboBox3's designer list (line 608: `15m,30m,1h,2h,3h,6h,12h`):

| sendTime | 15m | 30m | 60m | allowed |
|---|---|---|---|---|
| 15m (15)  | 1  | 0  | 0  | {15,30,60} |
| 30m (30)  | 2  | 1  | 0  | {15,30,60} |
| 1h (60)   | 4  | 2  | 1  | {15,30,60} |
| 2h (120)  | 8  | 4  | 2  | {15,30,60} |
| 3h (180)  | 12 | 6  | 3  | {15,30,60} |
| 6h (360)  | 24✗| 12 | 6  | {30,60} |
| 12h (720) | 48✗| 24✗| 12 | {60} |

Every combo yields a non-empty list; worst case (12h) still leaves `{60m}`. The
`allowed.Count = 0` fallback (979-980) remains as belt-and-suspenders. Confirms the
developer's analysis.

## 2. Dead `Case` branches genuinely unreachable and harmless (CONFIRMED)
- Krohne logTime (769-780) and Aichi logTime (698-709) retain `"5m"/"10m"` cases.
  ComboBox4 now only ever holds 15/30/60m (designer list line 527 is also
  `{15m,30m,60m}`, consistent with the narrowed candidate set), so these branches are
  unreachable. They are not masking a bug — each still maps to the correct minute value
  if it ever were hit.
- Default initializers (`logTime = 5` at 697/768) are now theoretical-only; every
  reachable dropdown value (15/30/60) matches an explicit case, so the default is never
  the encoded value. No regression.
- Krohne/BVT sendTime `"1m"/"5m"/"10m"/"24h"` cases (743-766, 811+) were already
  unreachable (ComboBox3 designer list never included them) and are unchanged.

## 3. CLOCKLOG encoding and read-back parsing unaffected (CONFIRMED)
- Krohne write path unchanged: `setTime = CInt(sendTime/logTime)` (788) →
  `AT+CLOCKLOG=1,0,<logTime>,<setTime>` (790). Because the `<=14` UI filter bounds every
  selectable (sendTime,logTime) pair, `setTime` can never exceed 14 from the UI — the
  invariant the developer cites holds. Max reachable: 12h/60m → 12.
- Aichi write path `AT+CLOCKLOG=1,<logTime>,0,0` (710) unchanged.
- Read-back branch (293-322) unchanged. Its transient re-add of a device-reported label
  not in the filtered Items (315-317) still correctly handles a device that reports a
  5m/10m logTime, so faithful display of real device state is preserved. No byte/hex
  string was touched.
- `LogTimeToMinutes` (938) intentionally still lists 5/10/15/30/60; it is only ever fed
  the narrowed candidates now, so the extra rows are inert, not a defect.

## 4. Note.txt entry appended correctly (CONFIRMED)
Line 21 appended in the established Vietnamese changelog style, accurately describing the
revert (15/30/60m for all sensors, dropping 5m/10m for Krohne/BVT, noting the
`Case "5m"/"10m"` in `Config()` left in place as harmless). Consistent with line 20 which
it reverses.

## Threading / handshake
Out of scope for this diff — no `SerialPort.DataReceived`/`SendLoop` code, no
`isWaiting`/`sendQueue` code, and no `AddToQueue` call sites were changed. No new
cross-thread UI access introduced.

## Non-blocking observation (pre-existing, not introduced by this change)
The filter permits logTime > sendTime (ratio 0/1), e.g. sendTime 15m + logTime 60m —
logging less frequently than sending, or the inverse depending on interpretation. This
behavior predates this change and existed with the wider set too; flagging only for
awareness, not as a defect in this diff.
