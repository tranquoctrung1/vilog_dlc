# Graph Report - VilogLite_V1.3  (2026-07-24)

## Corpus Check
- 23 files · ~13,715 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 202 nodes · 169 edges · 34 communities (23 shown, 11 thin omitted)
- Extraction: 99% EXTRACTED · 1% INFERRED · 0% AMBIGUOUS · INFERRED: 1 edges (avg confidence: 0.7)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `d6aa4799`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- WinForms Project Setup
- Serial Command Config
- Dev Agents & Notes
- Serial Send Queue
- Serial Protocol Reviewer (VilogLDC)
- VilogLDC Feature Workflow Orchestrator
- Workflow
- Developer Summary — Phase 01
- CLAUDE.md
- VilogLDC
- AddToQueue
- Button15_Click_1
- Form1_Load
- isWaiting flag
- ReceivedText1
- ReceivedText2
- ReceivedText3
- sendQueue
- Findings (advisory)
- Developer Summary — Phase 01
- 01 Developer Summary — logTime dropdown filtering by sendTime + sensor
- 01 Developer Summary — AT+TDC parse crash fix
- Verification against the review checklist
- What changed in Form1.vb
- 02 Reviewer Findings — logTime dropdown filter (ComboBox4) by sendTime + sensor
- 02 - Reviewer Findings: RichTextBox2 unconditional device-response logging
- 03 Developer Summary — Silent auto-reconnect on "Password Incorrect"
- 02 - Reviewer Findings: RichTextBox2 unconditional device-response logging
- [1.3] - 2026-07-09
- Follow-up: UI ordering gate (sendTime before logTime)
- VilogLDC

## God Nodes (most connected - your core abstractions)
1. `VilogLDC Serial Protocol` - 9 edges
2. `01 Developer Summary — logTime dropdown filtering by sendTime + sensor` - 9 edges
3. `02 Reviewer Findings — Unify logTime dropdown to 15m/30m/60m` - 8 edges
4. `Developer Summary — Rename-only refactor` - 8 edges
5. `01 - Developer Summary: RichTextBox2 unconditional device-response logging` - 8 edges
6. `VB.NET WinForms Developer (VilogLDC)` - 7 edges
7. `VilogLDC WinForms Conventions` - 7 edges
8. `01 Developer Summary — Unify logTime dropdown to 15m/30m/60m` - 7 edges
9. `Findings (advisory)` - 7 edges
10. `01 Developer Summary — AT+TDC parse crash fix` - 7 edges

## Surprising Connections (you probably didn't know these)
- None detected - all connections are within the same source files.

## Import Cycles
- None detected.

## Communities (34 total, 11 thin omitted)

### Community 0 - "WinForms Project Setup"
Cohesion: 0.25
Nodes (7): Build & verify, Control naming, Licensing gate, Project layout, Threading, VilogLDC WinForms Conventions, Why this exists

### Community 1 - "Serial Command Config"
Cohesion: 0.20
Nodes (9): COMMAND / DATACUT / CMDDL triplets (per Modbus request slot), Cross-thread UI access, Hex/timestamp encoding conventions, Parsing device responses (`ReceivedText1`, `isReadingParameter` branch), Per-device-type command sets (all built in `Config()`), The send queue / handshake pattern, Two serial links, VilogLDC Serial Protocol (+1 more)

### Community 2 - "Dev Agents & Notes"
Cohesion: 0.25
Nodes (7): VB.NET WinForms Developer (VilogLDC), 이전 산출물이 있을 때, 입력/출력 프로토콜, 작업 원칙, 팀 통신 프로토콜, 핵심 역할, 협업

### Community 4 - "Serial Protocol Reviewer (VilogLDC)"
Cohesion: 0.29
Nodes (6): Serial Protocol Reviewer (VilogLDC), 입력/출력 프로토콜, 작업 원칙, 팀 통신 프로토콜, 핵심 역할, 협업

### Community 5 - "VilogLDC Feature Workflow Orchestrator"
Cohesion: 0.17
Nodes (11): Error handling, Execution mode: Agent team, Phase 0: Context check, Phase 1: Prepare, Phase 2: Team setup (protocol-touching changes), Phase 3: Implementation + review loop, Phase 4: Wrap-up, Team composition (+3 more)

### Community 6 - "Workflow"
Cohesion: 0.15
Nodes (12): Button click handlers, Developer Summary — Rename-only refactor, Files inspected, NOT changed, Files touched, Not renamed (deliberately), Other handler, Reference updates, Renames (old_name -> new_name) (+4 more)

### Community 7 - "Developer Summary — Phase 01"
Cohesion: 0.25
Nodes (7): 01 Developer Summary — Unify logTime dropdown to 15m/30m/60m, Build / test status, Change made, Flag for reviewer — dead code, Goal, Protocol note, Verification of downstream code (no changes needed)

### Community 9 - "VilogLDC"
Cohesion: 0.22
Nodes (8): 02 Reviewer Findings — Unify logTime dropdown to 15m/30m/60m, 1. `<=14` filter vs narrowed set — no empty list for any sendTime (CONFIRMED), 2. Dead `Case` branches genuinely unreachable and harmless (CONFIRMED), 3. CLOCKLOG encoding and read-back parsing unaffected (CONFIRMED), 4. Note.txt entry appended correctly (CONFIRMED), Non-blocking observation (pre-existing, not introduced by this change), Scope reviewed, Threading / handshake

### Community 21 - "Findings (advisory)"
Cohesion: 0.20
Nodes (9): Finding 1 — MEDIUM: `serialLock` held across a blocking `SerialPort1.Write` on the UI thread, Finding 2 — LOW: flat 15 s `WaitTimeoutMs` ignores per-command expected latency, Finding 3 — MEDIUM (needs hardware/datasheet confirmation): year encoding change alters emitted bytes, Finding 4 — LOW / pre-existing: connect "OK" match may fire before password validation, Finding 5 — LOW / pre-existing (not in this diff): Krohne read-back case mismatch, Findings (advisory), Protocol Review — Phase 02 (fixes for bugs #1-8,10,13-15), Trivial (+1 more)

### Community 22 - "Developer Summary — Phase 01"
Cohesion: 0.29
Nodes (6): Bug-by-bug, Confirmation, Developer Summary — Phase 01, New shared infrastructure (top of class), Phase 02 — reviewer Finding 1 (MEDIUM) fix, Verification limitation

### Community 23 - "01 Developer Summary — logTime dropdown filtering by sendTime + sensor"
Cohesion: 0.11
Nodes (17): 01 Developer Summary — logTime dropdown filtering by sendTime + sensor, Behavior change to flag for review, Empty-list edge case, Files changed, Filtering formula, Finding 1 (MEDIUM regression) — read-back CLOCKLOG silently dropped, Finding 2 (LOW consistency) — missing 1m/5m sendTime cases for Krohne/BVT, Follow-up: reviewer findings 02 addressed (+9 more)

### Community 24 - "01 Developer Summary — AT+TDC parse crash fix"
Cohesion: 0.25
Nodes (7): 01 Developer Summary — AT+TDC parse crash fix, File / location, Fix pattern, Root cause, Scope, Verification note, What changed

### Community 25 - "Verification against the review checklist"
Cohesion: 0.25
Nodes (7): 1. Diff applied correctly / matches sibling style — CONFIRMED, 2. Stale ComboBox3 risk on swallowed exception — ACCEPTABLE (matches siblings), 3. Threading — CONFIRMED SAFE, 4. Other correctness — none found, Optional (non-blocking) note, Protocol Review: AT+TDC parse-loop Try/Catch fix (Bug #5), Verification against the review checklist

### Community 26 - "What changed in Form1.vb"
Cohesion: 0.13
Nodes (14): 01 - Developer Summary: RichTextBox2 unconditional device-response logging, 1. `ProcessLoggerResponse` (SerialPort1.DataReceived chain) — unconditional append, 2026-07-08 — Button15 "Sync Time" → "Send Data", 2. Status overwrites (`Text =`) changed to append (`AppendText`), Changes, File changed, Files changed, Notes / conventions preserved (+6 more)

### Community 27 - "02 Reviewer Findings — logTime dropdown filter (ComboBox4) by sendTime + sensor"
Cohesion: 0.33
Nodes (5): 02 Reviewer Findings — logTime dropdown filter (ComboBox4) by sendTime + sensor, Checks that passed (clean), Finding 1 (MEDIUM — new regression in the AT+CFG read-back path), Finding 2 (LOW — pre-existing divergence, now more reachable), Optional hardening (not blocking)

### Community 28 - "02 - Reviewer Findings: RichTextBox2 unconditional device-response logging"
Cohesion: 0.18
Nodes (10): 03 Developer Summary — Silent auto-reconnect on "Password Incorrect", 1. New guard field (~line 24, next to the other state flags), 2. `ProcessLoggerResponse` (~line 167 — was the MessageBox block), 3. `BtnConnect_Click` — reset the guard in two spots, Files changed, grep for other "Password Incorrect" / "Password Timeout" sites, How re-entrancy is guarded, Revision — reviewer findings addressed (round 2) (+2 more)

### Community 29 - "03 Developer Summary — Silent auto-reconnect on "Password Incorrect""
Cohesion: 0.25
Nodes (7): [1.2] - 2026-06-15, [1.3] - 2026-07-09, Nhật ký thay đổi (Changelog), Sửa lỗi, Thay đổi, Thêm mới, Đổi tên (không đổi hành vi)

### Community 30 - "02 - Reviewer Findings: RichTextBox2 unconditional device-response logging"
Cohesion: 0.50
Nodes (3): 02 - Reviewer Findings: RichTextBox2 unconditional device-response logging, Non-blocking observation (not a finding, out of style scope), Verdict: CLEAN — no protocol or threading defects found.

### Community 31 - "[1.3] - 2026-07-09"
Cohesion: 0.25
Nodes (7): [1.2] - 2026-06-15, [1.3] - 2026-07-09, Added, Changed, Changelog, Fixed, Renamed (no behavior change)

### Community 32 - "Follow-up: UI ordering gate (sendTime before logTime)"
Cohesion: 0.33
Nodes (6): Change (2 edits, `SerialCom/Form1.vb`), ComboBox5 gating — decided NO, Follow-up: UI ordering gate (sendTime before logTime), Gap, Read-back path (ProcessLoggerResponse CLOCKLOG branch) — verified safe, Verification

## Knowledge Gaps
- **146 isolated node(s):** `VilogLDC`, `핵심 역할`, `작업 원칙`, `입력/출력 프로토콜`, `협업` (+141 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **11 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `01 Developer Summary — logTime dropdown filtering by sendTime + sensor` connect `01 Developer Summary — logTime dropdown filtering by sendTime + sensor` to `Follow-up: UI ordering gate (sendTime before logTime)`?**
  _High betweenness centrality (0.011) - this node is a cross-community bridge._
- **Why does `Follow-up: UI ordering gate (sendTime before logTime)` connect `Follow-up: UI ordering gate (sendTime before logTime)` to `01 Developer Summary — logTime dropdown filtering by sendTime + sensor`?**
  _High betweenness centrality (0.005) - this node is a cross-community bridge._
- **What connects `VilogLDC`, `핵심 역할`, `작업 원칙` to the rest of the system?**
  _146 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `01 Developer Summary — logTime dropdown filtering by sendTime + sensor` be split into smaller, more focused modules?**
  _Cohesion score 0.1111111111111111 - nodes in this community are weakly interconnected._
- **Should `What changed in Form1.vb` be split into smaller, more focused modules?**
  _Cohesion score 0.13333333333333333 - nodes in this community are weakly interconnected._