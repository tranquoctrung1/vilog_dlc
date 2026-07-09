---
name: vilog-feature-workflow
description: Orchestrates the VilogLDC agent team (VB.NET WinForms developer + serial protocol reviewer) to implement features, bug fixes, or refactors in this codebase. Use for any request to add/change/fix behavior in Form1.vb, popupMonitorScreen.vb, the flowmeter/AT-command configuration logic, or the .vbproj. Also covers follow-up work: "fix the review findings", "redo the config for X device", "update the interval mapping", re-running after feedback.
---

# VilogLDC Feature Workflow Orchestrator

## Execution mode: Agent team

Two teammates, generation-verification pattern: the developer implements, the protocol reviewer verifies protocol/threading correctness before the task is considered done.

## Team composition

| Teammate | Agent type | Role | Skills | Output |
|---|---|---|---|---|
| developer | `vb-winforms-developer` | Implements the change | `vilog-serial-protocol`, `vb-winforms-conventions` | edited `.vb`/`.vbproj` files, `Note.txt` entry |
| reviewer | `serial-protocol-reviewer` | Verifies AT-command/Modbus/threading correctness | `vilog-serial-protocol` | `_workspace/{n}_reviewer_findings.md` |

## Workflow

### Phase 0: Context check

1. Check whether `_workspace/` exists in the project root.
   - **Missing** → fresh run, go to Phase 1.
   - **Exists + user asks to redo/fix part of it** → partial re-run: re-invoke only the relevant teammate, pointing them at the existing `_workspace/*_reviewer_findings.md` or `*_developer_summary.md`.
   - **Exists + user gives a new, unrelated request** → move the old `_workspace/` to `_workspace_{YYYYMMDD_HHMMSS}/`, create a fresh `_workspace/`.
2. Skip team setup entirely for trivial, purely cosmetic changes (a label text, a `Note.txt` line, reordering UI controls with no logic change) — dispatch the developer alone via the `Agent` tool in that case; the two-agent review overhead isn't worth it when nothing touches the protocol layer. Use judgment: if in doubt whether a change touches serial/AT/Modbus logic, run the full team.

### Phase 1: Prepare

1. Read the user's request; identify whether it touches AT-command/Modbus/protocol logic (config sending, response parsing, hex encoding, timestamps) or is UI/cosmetic-only.
2. Create `_workspace/` (or reuse per Phase 0).

### Phase 2: Team setup (protocol-touching changes)

```
TeamCreate(
  team_name: "vilog-team",
  members: [
    { name: "developer", agent_type: "vb-winforms-developer", model: "opus", prompt: "<task description>" },
    { name: "reviewer", agent_type: "serial-protocol-reviewer", model: "opus", prompt: "Wait for the developer to report changed files, then review per the vilog-serial-protocol skill." }
  ]
)

TaskCreate(tasks: [
  { title: "Implement <feature/fix>", description: "<detail>", assignee: "developer" },
  { title: "Review protocol/threading correctness", description: "Review developer's changes once notified", assignee: "reviewer", depends_on: ["Implement <feature/fix>"] }
])
```

### Phase 3: Implementation + review loop

**Mode:** team self-coordinates via `SendMessage`.

1. Developer implements the change, writes `_workspace/01_developer_summary.md`, and messages the reviewer with the files changed.
2. Reviewer reviews against `vilog-serial-protocol`, writes `_workspace/02_reviewer_findings.md`, messages the developer.
3. If findings exist, developer addresses them and re-notifies the reviewer (repeat until clean or the reviewer confirms no further issues — normally one round-trip).
4. Leader monitors via `TaskGet`; if a teammate goes idle without responding, `SendMessage` to check status.

### Phase 4: Wrap-up

1. Confirm both tasks are complete (`TaskGet`).
2. Read `_workspace/02_reviewer_findings.md` (final version) to confirm clean review.
3. `TeamDelete` to tear down the team.
4. Report to the user: what changed, files touched, whether the reviewer found and the developer fixed any protocol issues, and whether a real build/hardware test is still needed (this project has no CI — say so explicitly per `vb-winforms-conventions`).
5. Preserve `_workspace/` — don't delete it.

## Error handling

| Situation | Strategy |
|---|---|
| Developer fails/stalls | Leader detects idle state, `SendMessage` to check, restart task if needed |
| Reviewer fails/stalls | Leader proceeds with a note in the final report that protocol review could not be completed — don't claim it was reviewed if it wasn't |
| Reviewer keeps finding issues past 2 rounds | Leader steps in, summarizes the disagreement to the user rather than looping indefinitely |
| No build tool available to verify compilation | Report this limitation explicitly; don't claim "builds successfully" without having run a build |

## Test scenarios

**Happy path:** User asks "add a new interval option to the reporting dropdown." Phase 1 flags this as protocol-touching (affects `AT+TDC=` mapping). Phase 2 creates the 2-person team. Developer edits `Config()` and `ComboBox3` handling, reviewer checks the new interval's seconds value and any `ComboBox4` cross-adjustment, confirms clean. Phase 4 reports the change and notes hardware verification is still needed.

**Error path:** Developer makes a change to a `COMMAND`/`DATACUT` pair; reviewer finds the DATACUT offset doesn't match the new register count and requests a fix. Developer corrects it, re-notifies; reviewer confirms clean on the second pass. Final report notes one review round-trip occurred.
