# 01 Developer Summary — AT+TDC parse crash fix

## What changed
Wrapped the `AT+TDC=` read-back parse block in `ReceivedText1`'s `isReadingParameter`
branch with a `Try/Catch` so a malformed/truncated `AT+TDC=` line (empty or non-numeric
after the `.Substring(7)` cut) no longer crashes the app.

## File / location
- `SerialCom/Form1.vb`, inside the `isReadingParameter` line-by-line parse loop.
- Block now at ~lines 243–256 (was ~243–251 before the edit).

## Root cause
`Dim TDC_Integer As Integer = CInt(TDC)` threw `System.InvalidCastException`
(inner `FormatException`) when the serial line was `"AT+TDC="` with nothing (or only
whitespace/CR) after it. `CInt("")` cannot parse an empty string.

## Fix pattern
Matched the existing sibling defensive blocks (Bug #5 for `AT+PUBTOPIC=` and
`AT+CLOCKLOG=`) exactly:
- `' Bug #5: ...` comment
- `Try` wrapping the `.Substring`, `CInt`, and `ComboBox3.SelectedItem` assignments
- `Catch ex As Exception` that ignores the malformed line (no popup, no logging —
  consistent with siblings)

Used `Try/Catch + CInt` rather than `Integer.TryParse` to match the idiom the adjacent
CLOCKLOG block already uses.

## Scope
No other code touched. `Note.txt` changelog entry appended.

## Verification note
No automated tests exist in this project. Not build-verified (msbuild/devenv
availability unconfirmed). Real confirmation requires read-back against a device that
returns a truncated `AT+TDC=` line.
