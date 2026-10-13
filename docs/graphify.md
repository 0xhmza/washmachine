# Project knowledge graph

Graphify maps the application's code, documentation, and configuration into a
local graph. The project-scoped Codex skill is in `.codex/skills/graphify/`;
`AGENTS.md` tells Codex to query the graph for codebase questions.

## Install on another machine

```powershell
uv tool install graphifyy==0.9.88
uv tool update-shell
graphify install --project --platform codex
```

Restart the terminal after updating PATH. On this Windows installation, the
executable is also available at `$env:USERPROFILE\.local\bin\graphify.exe`.

## Build and use

In Codex, invoke `$graphify .` to build the complete code and documentation graph.
The skill uses local AST parsing for code and the assistant for document
extraction; an API key is optional.

For a standalone code-only build, or a routine refresh after source changes:

```powershell
graphify extract . --code-only
graphify update .
graphify query "How are the web and desktop interfaces connected?"
graphify explain "AppPaths"
graphify export html
```

`graphify update .` refreshes code relationships without an LLM call. Rerun the
Codex skill when documentation changes need semantic extraction.

Generated outputs are under `graphify-out/`:

- `graph.html`: interactive graph.
- `GRAPH_REPORT.md`: communities, hubs, and suggested questions.
- `graph.json`: persisted graph used by query/path/explain.

Outputs and caches are ignored by Git. `.graphifyignore` excludes toolchains,
build artifacts, sync-conflict copies, local test specimens, packaging artwork,
and Graphify's own integration files. Application source files are not changed
by extraction. Some language constructs may not produce AST symbols; the graph
is a navigation aid, and source remains the authority for implementation details.
