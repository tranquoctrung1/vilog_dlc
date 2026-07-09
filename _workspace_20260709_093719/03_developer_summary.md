# 03 Developer Summary — Silent auto-reconnect on "Password Incorrect"

(Prior `01`/`02` in this folder are a separate, completed task about RichTextBox2 logging.
This is a new phase; numbered 03 to avoid overwriting that record.)

## Task
When the running device drops its session mid-stream and sends "Password Incorrect"
(while `Button9.Enabled` = the running-state signal), replace the blocking
`MessageBox.Show("Password Timeout - Please reconnect")` with a fully automatic, silent
teardown + reconnect. No popup; guarded against re-entrancy.

## Files changed
`SerialCom/Form1.vb` (3 edits) + one `Note.txt` changelog line.

### 1. New guard field (~line 24, next to the other state flags)
```vb
Dim isAutoReconnecting As Boolean = False
```

### 2. `ProcessLoggerResponse` (~line 167 — was the MessageBox block)
Before:
```vb
If (buffer.Contains("Password Incorrect") And Button9.Enabled) Then
    MessageBox.Show("Password Timeout - Please reconnect")
End If
```
After:
```vb
If (buffer.Contains("Password Incorrect") And Button9.Enabled) AndAlso Not isAutoReconnecting Then
    isAutoReconnecting = True
    BtnDisconnect_Click(Nothing, Nothing)
    BtnConnect_Click(Nothing, Nothing)
End If
```
Runs on the UI thread (the method's `InvokeRequired` block already marshals there). Reuses
the existing `BtnDisconnect_Click` (close `SerialPort1`, reset button-enabled states) and
`BtnConnect_Click` (reopen port with same `ComboBox1`/9600 settings + AT/password handshake
using stored `TextBox2.Text`) instead of duplicating their bodies. `BtnConnect_Click` already
runs the handshake off the UI thread via `Task.Run`.

### 3. `BtnConnect_Click` — reset the guard in two spots
- Port-open-failure `Catch` path: added `isAutoReconnecting = False` beside the existing
  `isConnecting = False` before `GoTo cannot_connect`.
- Added a `Finally` to the handshake `Task.Run` Try/Catch that sets
  `isAutoReconnecting = False` once the reconnect attempt finishes (success or failure).
  Harmless no-op for a normal user-initiated Connect (flag already False).

## grep for other "Password Incorrect" / "Password Timeout" sites
Only the one occurrence at lines 167-168 existed; no initial-connect/config-handshake flow
uses this string, so nothing else was affected.

## How re-entrancy is guarded
`isAutoReconnecting` is set True before teardown and only reset once `BtnConnect_Click`
finishes — immediately on the synchronous port-open-failure path, or in the `Task.Run`
`Finally` after the async AT/password handshake completes. Two natural guards reinforce it:
(a) `BtnDisconnect_Click` sets `Button9.Enabled = False`, and the trigger condition also
requires `Button9.Enabled`, so no further "Password Incorrect" chunk can re-fire until a
successful reconnect re-enables Button9; (b) `ProcessLoggerResponse` runs atomically on the
UI thread (background `DataReceived` events serialize through `Me.Invoke`), so two kickoffs
cannot interleave. The boolean additionally covers the async-handshake window.

## Revision — reviewer findings addressed (round 2)

Reviewer confirmed a bug: tearing down + reconnecting did NOT stop the live
`SendLoop`/`sendQueue`. If "Password Incorrect" fired mid-Configure, the old SendLoop
thread stayed alive and would dequeue leftover `AT+CFGDEV`/`COMMANDn` commands onto the
reopened port, interleaving with the new handshake and sending config to an unauthenticated
session. Fixes applied in the trigger branch (`ProcessLoggerResponse`, ~line 174), BEFORE
calling the teardown/reconnect handlers:

```vb
SendingFlag = False
SyncLock sendQueue
    sendQueue.Clear()
End SyncLock
IsWaitingFlag = False
isConfiguring = False
isReadingParameter = False
```
- Stopping + draining the queue prevents the stale SendLoop from writing leftover commands.
- Resetting `isConfiguring`/`isReadingParameter` stops a stale mode flag from misreading the
  new handshake's "OK" (isReadingParameter would truncate a Load-Parameters read-back;
  isConfiguring would try to keep pumping the now-empty queue).

MINOR (also fixed): added an unconditional `isAutoReconnecting = False` immediately after
`BtnConnect_Click(Nothing, Nothing)` returns in the trigger branch, so if `ComboBox1.Text`
is empty (BtnConnect_Click returns before its `Task.Run` Finally ever runs) the guard can't
get stuck True and permanently disable auto-reconnect. Since `BtnDisconnect_Click` sets
`Button9.Enabled = False`, the trigger condition (which requires `Button9.Enabled`) still
can't re-fire during the async handshake window, so clearing the guard synchronously here is
safe. The earlier `Task.Run` Finally / open-fail `Catch` resets remain as harmless defense.

Not changed (expected behavior, per user's disconnect+reconnect request): auto-reconnect
only re-authenticates; it does not resume an interrupted `Config()` run.

## Verification
No `msbuild`/`devenv` confirmed on PATH — not compile-verified. This change touches the
connect handshake / DataReceived path, so serial-protocol-reviewer review is recommended;
real confirmation requires a Dragino device that drops its session mid-stream.
