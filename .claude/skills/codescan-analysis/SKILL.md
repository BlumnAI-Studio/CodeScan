---
name: codescan-analysis
description: |
  Use CodeScan CLI and git CLI for source-code analysis, project indexing,
  keyword search, graph search, Cypher-like graph queries, and change history
  analysis.

  Trigger this skill when the user asks for:
  - code analysis, source analysis, codebase exploration, or project structure
  - method search, class search, dependency tracing, or relationship lookup
  - CodeScan usage, codescan search, graph search, or Cypher-like graph queries
  - recent changes, git history, git log analysis, or blame-based ownership
  - project scanning, indexing, re-indexing, source update, or DB refresh
  - any repository-level question where locating relevant files first will
    reduce token usage before reading source files directly
---

# CodeScan Analysis Skill

Use this skill to analyze source code with the `codescan` CLI and `git` CLI.

CodeScan indexes projects into `~/.codescan/db/codescan.db`, extracts classes,
methods, comments, docs, git blame metadata, and source graph relationships,
then exposes the data through CLI, TUI, and local GUI interfaces.

Platform status:

- CodeScan is currently tested first on Windows PowerShell.
- macOS/Linux-compatible CLI usage and skill command wrappers are being prepared.
- On Linux-like environments, use CodeScan by building directly from source with the .NET SDK for now.

Core idea:

1. Use `codescan` to find relevant projects, files, methods, graph nodes, and relationships.
2. Read only the files that matter.
3. Use `git` for recent change history and exact diffs.

PowerShell helper scripts live in this skill's `scripts/` directory, but direct
`codescan` commands are usually clearer and easier to compose.

---

## 1. CodeScan CLI Reference

### Project Management

| Command | Purpose | Example |
|---------|---------|---------|
| `codescan projects` | List indexed projects | `codescan projects` |
| `codescan project <id>` | Show project summary | `codescan project 1` |
| `codescan project <id> --detail` | Show files, methods, docs, authors | `codescan project 1 --detail` |
| `codescan project-addinfo <id> <text>` | Add AI-friendly project description | `codescan project-addinfo 1 "Spring Boot API"` |
| `codescan project-update <id> [opts]` | Update path, description, or source index | `codescan project-update 1 --source` |
| `codescan project-delete <id> [-f]` | Remove project from CodeScan DB only | `codescan project-delete 1 -f` |

`project-update` options:

- `--path <path>`: update the stored project root path.
- `--addinfo <text>`: update the project description.
- `--source`: run `git pull` if possible, then perform a full rescan and DB re-index.

```powershell
codescan project-update 1 --source
codescan project-update 1 --source --addinfo "Payment gateway service"
```

### Scan And Index

| Command | Purpose | Example |
|---------|---------|---------|
| `codescan scan [path]` | Register and analyze a project using smart defaults | `codescan scan D:\repo` |
| `codescan list <path>` | Scan with custom output/filtering options | `codescan list D:\repo --detail --tree` |

Useful `list` options:

- `-i, --include <exts>`: include extensions, comma separated.
- `-e, --exclude <dirs>`: exclude directory names, comma separated.
- `-d, --depth <n>`: maximum traversal depth.
- `--tree`: tree output.
- `-s, --stats`: include file and size statistics.
- `--detail`: class/method extraction, comment extraction, git blame, and graph indexing.

Prefer `codescan scan <path>` for initial registration unless the user asks for
custom traversal filters.

### Keyword Search

| Command | Purpose | Example |
|---------|---------|---------|
| `codescan search <query>` | Hybrid DB full-text + git log search | `codescan search "HttpClient"` |
| `codescan search <query> -p <id>` | Search within one project | `codescan search "Order" -p 1` |
| `codescan search <query> -t <type>` | Filter by indexed type | `codescan search "TODO" -t comment` |

Search types:

`method`, `file`, `doc`, `comment`, `commit`

Result limit:

`-l <n>`, default 30.

Examples:

```powershell
codescan search "HttpClient"
codescan search "OrderService" -p 1 -t method
codescan search "TODO" -p 1 -t comment -l 50
codescan search "authentication" -t doc
```

### Graph Search

Graph search returns graph nodes and visible relationships from the source
knowledge graph. Use it when the question is about structure, relationships,
dependencies, ownership, or neighboring code concepts.

```powershell
codescan graph "HttpClient"
codescan graph "SearchCommand" --project 1 --depth 2
codescan search "HttpClient" --graph --depth 2
```

Options:

- `-p, --project <id>`: restrict to one project.
- `-d, --depth <n>`: expand neighbor hops, default 1, max 4.
- `-l, --limit <n>`: max matched seed nodes.

### Cypher-Like Graph Query

Use `codescan query` when you need a structured graph retrieval pattern.
`codescan cypher` is an alias. `codescan graph` also auto-detects queries that
start with `MATCH`.

This is not full Cypher. It is a safe CodeScan subset mapped to the local
SQLite graph tables.

Supported patterns:

```cypher
MATCH (n:kind)
MATCH (a:kind)-[r:edge_kind]->(b:kind)
```

Supported fields:

| Alias Type | Fields |
|------------|--------|
| Node alias | `kind`, `label`, `path`, `detail` |
| Edge alias | `kind`, `label` |

Supported operators:

`=`, `CONTAINS`, `STARTS WITH`, `ENDS WITH`

Supported clauses:

- `WHERE ... AND ...`
- `RETURN ...` is accepted for readability, but the renderer ignores it.
- `LIMIT <n>` limits matched seed nodes or matched edges.

Common node kinds:

`project`, `directory`, `file`, `class`, `method`, `comment`, `doc`, `author`, `type`, `module`

Common edge kinds:

`contains`, `defines`, `authored`, `has_comment`, `documents`, `imports`,
`inherits_or_implements`, `creates`, `uses_type`

Examples:

```powershell
# Find class nodes
codescan query "MATCH (c:class) WHERE c.label CONTAINS 'Service' LIMIT 20"

# Find classes that use a specific type
codescan query "MATCH (c:class)-[r:uses_type]->(t:type) WHERE t.label = 'HttpClient'"

# Find file imports
codescan query "MATCH (f:file)-[r:imports]->(m:module) WHERE m.label CONTAINS 'System.Net'"

# Find authored methods and expand one neighbor hop
codescan query "MATCH (a:author)-[r:authored]->(m:method) WHERE a.label CONTAINS 'kim'" --depth 1

# Search command can force query mode
codescan search "MATCH (f:file)-[r:imports]->(m:module) LIMIT 20" --query
```

When to use graph query instead of keyword search:

- Use keyword search for "where is this word or method name?"
- Use graph search for "show related nodes around this concept."
- Use graph query for "find relationships of a specific shape."

### TUI And GUI

TUI:

```powershell
codescan tui
```

The TUI is intended for interactive human exploration. This skill should usually
prefer non-interactive CLI commands. Mention TUI only when the user wants to
browse manually.

GUI:

```powershell
codescan gui start --port 8085
codescan gui stop --port 8085
```

The GUI provides keyword search, graph search, graph query, 2D graph view, and
camera-controlled 3D graph view. Use it when the user wants visual graph
inspection.

---

## 2. Analysis Workflows

### 2.1 Start With Indexed Project Discovery

Before analyzing, check whether the project is already indexed.

```powershell
codescan projects
codescan project <id>
codescan project <id> --detail
```

If the project is not registered:

```powershell
codescan scan <project-path>
codescan project-addinfo <id> "Short project description"
```

### 2.2 Refresh Source And Index

When the user asks to update source, refresh, rescan, or use latest code:

```powershell
codescan project-update <id> --source
codescan project <id>
```

Behavior:

1. Detect git repository from the stored project path.
2. Try `git pull`.
3. Continue even if pull fails, with a warning.
4. Full scan, method/comment extraction, git blame, graph indexing.
5. Store a new scan record.

### 2.3 Locate Code Before Reading Files

Use CodeScan first to avoid reading the entire repository.

```powershell
codescan search "OrderService" -p <id> -t method
codescan search "Controller" -p <id> -t file
codescan graph "OrderService" -p <id> --depth 1
```

Then read the relevant files directly.

### 2.4 Trace Dependencies Or Relationships

For dependency questions, start with graph query:

```powershell
codescan query "MATCH (c:class)-[r:uses_type]->(t:type) WHERE c.label CONTAINS 'Order'" -p <id>
codescan query "MATCH (f:file)-[r:imports]->(m:module) WHERE m.label CONTAINS 'Http'" -p <id>
codescan query "MATCH (c:class)-[r:creates]->(t:type) WHERE t.label CONTAINS 'Client'" -p <id>
```

For ownership:

```powershell
codescan query "MATCH (a:author)-[r:authored]->(m:method) WHERE m.label CONTAINS 'Order'" -p <id>
```

### 2.5 Analyze Recent Changes

Use git directly for exact history and diffs.

```powershell
git status
git log --oneline -20
git diff --name-only HEAD~5
git show <commit-hash> --stat
git show <commit-hash> -- <path>
```

Use CodeScan to locate impacted files or methods after identifying changed
areas.

### 2.6 Cross-Project Search

When the user does not know which project contains a concept:

```powershell
codescan search "WebSocket"
codescan search "authentication" -t method
codescan graph "HttpClient" --depth 1
```

Then narrow with `-p <id>`.

---

## 3. Helper Scripts

Scripts are optional wrappers for repeated tasks.

| Script | Purpose | Main Parameters |
|--------|---------|-----------------|
| `project-overview.ps1` | Project summary and recent git activity | `-ProjectId <id>` |
| `search-code.ps1` | Multi-type code search | `-Query <text> [-ProjectId <id>] [-Types <list>]` |
| `analyze-changes.ps1` | Git change history analysis | `-ProjectPath <path> [-Days <n>] [-Count <n>]` |
| `cross-project-search.ps1` | Search across indexed projects | `-Query <text> [-Type <type>]` |
| `scan-project.ps1` | Register and scan a project | `-Path <path> [-Include <exts>] [-Exclude <dirs>]` |
| `update-source.ps1` | Source update and rescan | `-ProjectId <id>` |

Run from PowerShell:

```powershell
.\.claude\skills\codescan-analysis\scripts\search-code.ps1 -Query "HttpClient" -ProjectId 1
```

---

## 4. Safety Guardrails

This skill is primarily for analysis and indexing.

Allowed:

- `codescan` commands for scan, search, graph, query, project, projects, GUI/TUI guidance.
- `git status`, `git log`, `git diff`, `git show`, `git blame`.
- `git pull` only as part of source refresh when requested.
- Reading source files after CodeScan identifies relevant paths.

Avoid unless the user explicitly asks:

- `git commit`, `git push`, `git merge`, `git rebase`.
- branch switching with `git checkout` or `git switch`.
- editing source files.
- deleting CodeScan projects unless the user asks for DB cleanup.

If the user asks for commit or push, leave this analysis skill and follow the
normal repository workflow instead of treating it as read-only analysis.

---

## 5. Common Task Recipes

### Find API Code

User asks: "Find the order API code."

1. `codescan projects`
2. `codescan search "Order" -p <id> -t method`
3. `codescan search "Controller" -p <id> -t file`
4. Read the returned files.

### Understand A Class Dependency

User asks: "What does OrderService depend on?"

1. `codescan query "MATCH (c:class)-[r:uses_type]->(t:type) WHERE c.label = 'OrderService'" -p <id>`
2. `codescan query "MATCH (c:class)-[r:creates]->(t:type) WHERE c.label = 'OrderService'" -p <id>`
3. Read the files for the returned class/type nodes if deeper analysis is needed.

### Find Who Last Touched A Method

1. `codescan search "MethodName" -p <id> -t method`
2. `codescan query "MATCH (a:author)-[r:authored]->(m:method) WHERE m.label CONTAINS 'MethodName'" -p <id>`
3. Use `git blame` or `git log -- <path>` for exact history.

### Refresh And Re-Analyze

1. `codescan project-update <id> --source`
2. `codescan project <id>`
3. Repeat keyword or graph query search.

### Visualize Graph

1. `codescan gui start --port 8085`
2. Open `http://127.0.0.1:8085/`
3. Use Keyword, Graph Search, or Query.
4. Switch between 2D and 3D graph views.
