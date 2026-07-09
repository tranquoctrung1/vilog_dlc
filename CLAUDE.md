## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).

## Harness: VilogLDC development

**Goal:** implement and review changes to the VilogLDC VB.NET WinForms serial-config app safely, catching AT-command/Modbus protocol mistakes that compile fine but break real hardware.

**Trigger:** for any feature/bugfix/refactor request touching this codebase, use the `vilog-feature-workflow` skill. Also triggers on follow-ups: redo/fix/update a prior change, apply review findings. Simple questions can be answered directly.

**Change log:**
| Date | Change | Target | Reason |
|---|---|---|---|
| 2026-07-07 | Initial harness build | all | Set up dev + protocol-review agent team for AT-command/Modbus-heavy legacy codebase with no tests |
