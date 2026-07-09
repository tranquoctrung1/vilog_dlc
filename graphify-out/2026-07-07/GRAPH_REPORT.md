# Graph Report - .  (2026-07-07)

## Corpus Check
- cluster-only mode — file stats not available

## Summary
- 19 nodes · 29 edges · 4 communities (3 shown, 1 thin omitted)
- Extraction: 93% EXTRACTED · 7% INFERRED · 0% AMBIGUOUS · INFERRED: 2 edges (avg confidence: 0.65)
- Token cost: 41,669 input · 58 output

## Community Hubs (Navigation)
- [[_COMMUNITY_WinForms Project Setup|WinForms Project Setup]]
- [[_COMMUNITY_Serial Command Config|Serial Command Config]]
- [[_COMMUNITY_Dev Agents & Notes|Dev Agents & Notes]]
- [[_COMMUNITY_Serial Send Queue|Serial Send Queue]]

## God Nodes (most connected - your core abstractions)
1. `vilog-serial-protocol skill` - 12 edges
2. `vb-winforms-conventions skill` - 6 edges
3. `vb-winforms-developer agent` - 5 edges
4. `serial-protocol-reviewer agent` - 4 edges
5. `SendLoop` - 4 edges
6. `isWaiting flag` - 3 edges
7. `Config()` - 2 edges
8. `ReceivedText1` - 2 edges
9. `ReceivedText3` - 2 edges
10. `AddToQueue` - 2 edges

## Surprising Connections (you probably didn't know these)
- `vilog-serial-protocol skill` --references--> `ReceivedText1`  [EXTRACTED]
  .claude/skills/vilog-serial-protocol/SKILL.md → SerialCom/Form1.vb
- `vilog-serial-protocol skill` --references--> `ReceivedText3`  [EXTRACTED]
  .claude/skills/vilog-serial-protocol/SKILL.md → SerialCom/Form1.vb
- `vb-winforms-conventions skill` --references--> `Form1_Load`  [EXTRACTED]
  .claude/skills/vb-winforms-conventions/SKILL.md → SerialCom/Form1.vb
- `vilog-serial-protocol skill` --references--> `AddToQueue`  [EXTRACTED]
  .claude/skills/vilog-serial-protocol/SKILL.md → SerialCom/Form1.vb
- `vilog-serial-protocol skill` --references--> `Button15_Click_1`  [EXTRACTED]
  .claude/skills/vilog-serial-protocol/SKILL.md → SerialCom/Form1.vb

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Send queue handshake pattern** — serialcom_form1_addtoqueue, serialcom_form1_sendloop, serialcom_form1_sendqueue, serialcom_form1_iswaiting, serialcom_form1_receivedtext1 [EXTRACTED 0.90]
- **VilogLDC agent/skill documentation set** — claude_agents_serial_protocol_reviewer, claude_agents_vb_winforms_developer, claude_skills_vb_winforms_conventions_skill, claude_skills_vilog_serial_protocol_skill [INFERRED 0.75]

## Communities (4 total, 1 thin omitted)

### Community 0 - "WinForms Project Setup"
Cohesion: 0.33
Nodes (3): vb-winforms-conventions skill, Form1_Load, ReceivedText3

### Community 1 - "Serial Command Config"
Cohesion: 0.50
Nodes (5): vilog-serial-protocol skill, AddToQueue, Button15_Click_1, Config(), ReceivedText2

### Community 3 - "Serial Send Queue"
Cohesion: 0.50
Nodes (4): isWaiting flag, ReceivedText1, SendLoop, sendQueue

## Knowledge Gaps
- **4 isolated node(s):** `ReceivedText2`, `sendQueue`, `Form1_Load`, `Button15_Click_1`
  These have ≤1 connection - possible missing edges or undocumented components.
- **1 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `vilog-serial-protocol skill` connect `Serial Command Config` to `WinForms Project Setup`, `Dev Agents & Notes`, `Serial Send Queue`?**
  _High betweenness centrality (0.699) - this node is a cross-community bridge._
- **Why does `vb-winforms-conventions skill` connect `WinForms Project Setup` to `Serial Command Config`, `Dev Agents & Notes`?**
  _High betweenness centrality (0.380) - this node is a cross-community bridge._
- **Why does `SendLoop` connect `Serial Send Queue` to `Serial Command Config`, `Dev Agents & Notes`?**
  _High betweenness centrality (0.114) - this node is a cross-community bridge._
- **What connects `ReceivedText2`, `sendQueue`, `Form1_Load` to the rest of the system?**
  _4 weakly-connected nodes found - possible documentation gaps or missing edges._