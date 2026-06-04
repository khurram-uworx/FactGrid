# Memories

- Phase 2 Follow-up Plan: Add dual MCP routing (global + scoped), resources (entities://list, entities://schema), prompts (entities-guide, entity-guide), and update tools for global mode. Current state: Program.cs has scoped route only; WithResourcesFromAssembly/WithPromptsFromAssembly not registered; GenericSqlQueryTool fails on null CurrentEntity; QueryValidationService already has full AST walk for scoped validation. <!-- id=2d7aad33d8cb47d5954e81b6e3c47908 entity=default type=plan ts=2026-06-04T14:19:27.3188061+00:00 v=1 tags=phase2,,followup,,mcp,,architecture -->
