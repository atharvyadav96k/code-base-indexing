# CLAUDE.md

## Exploring this codebase

To understand existing code, use the **`codebase-memory-mcp`** server instead of
reading files wholesale — it keeps context usage (and cost) low. This is an
external development aid and is **not** part of the product being built; do not
confuse it with the indexer in this repo.

Available tools:
- `mcp__codebase-memory-mcp__index_status` — check whether the index is ready and
  up to date before searching. Run this first if search results look stale or empty.
- `mcp__codebase-memory-mcp__search_code` — search the codebase for relevant code
  instead of grepping or opening many files by hand.

Guidance:
- Prefer `search_code` to locate the right code before opening a file directly.
- Only open/read a file in full once search has pointed you to it.
- These tools are for reading/understanding existing code, not for editing.

Guidance for AI agents working in this repository. Read this fully before writing
any code. The companion file `code-index-build-plan.md` holds the detailed phased
plan; this file holds the rules, structure, and standards you must follow.

---

## What this project is

A code-intelligence indexer for AI agents. It parses a codebase into searchable
**nodes** (namespaces, classes, methods, etc.), stores them in a binary index,
and lets an AI navigate large codebases without loading everything into context.
The core loop: the AI **searches** for nodes → receives cheap **summaries** →
picks one → requests the full **code** on demand.

- **Implementation language:** C# (.NET 8 LTS).
- **First supported target language:** C# only, via **Roslyn**
  (Microsoft.CodeAnalysis).
- **Storage:** a binary (`.bin`) index file.

---

## The golden rule (do not violate)

**The shared core must never know which language it is parsing.**

Language support is added as a **new parser module written in code**, implementing
one shared parser interface. There is **no** JSON/config system that describes
grammar. Every parser emits the identical node model; everything above the parser
is written once and shared.

Concretely:
- **Never** put language-specific logic (Roslyn types, C# keywords, `.cs`
  assumptions) anywhere except inside a parser module.
- If you find yourself writing `if (language == "csharp")` in Core, Storage,
  Search, Indexing, or the interface layer — **stop**, the design has broken.
- Core depends on nothing. Every other project points inward toward Core.
- Parser implementations are plugged in at the **composition root** (the app
  startup), not referenced directly by the indexing pipeline.

This rule is the whole point of the architecture. Protect it in every change.

---

## Repository structure

A single solution, `CodeIndexer.sln`, with these projects. Dependencies point
inward toward `Core`; arrows below show allowed references.

```
CodeIndexer.Core            # Node model, parser interface, scope model, shared contracts.
                            # Depends on NOTHING. No Roslyn, no storage, no I/O.

CodeIndexer.Parsing.CSharp  # The Roslyn-backed C# parser. Implements the parser
                            # interface from Core. -> Core, Roslyn.
                            # ALL C#-specific logic lives here and nowhere else.

CodeIndexer.Storage         # Binary (.bin) persistence: schema, version header,
                            # atomic writes, corruption handling. -> Core.

CodeIndexer.Indexing        # Orchestration: session/root discovery, file discovery,
                            # incremental updates, git-hook logic. -> Core, Storage.
                            # Talks to parsers only through the Core interface.

CodeIndexer.Search          # Search, ranking, retrieval, structure/navigation views.
                            # -> Core, Storage.

CodeIndexer.Server          # The AI-facing interface (MCP server) and/or CLI.
                            # Composition root: registers parsers here. -> all above.

CodeIndexer.Tests           # Unit/integration tests. -> all.
```

Key placement rules:
- The **node model** and **parser interface** live in `Core` and are the most
  stable things in the repo. Changing them ripples everywhere — change them
  deliberately, never casually.
- `Indexing` and `Search` reference the parser **interface**, never
  `Parsing.CSharp` directly.
- A future language becomes a new `CodeIndexer.Parsing.<Lang>` project registered
  at the composition root — no other project changes.

---

## Coding standards

**Language & project setup**
- Target `.NET 8`. Enable **nullable reference types** and treat warnings as
  errors. Use an `.editorconfig` as the source of truth for formatting.
- File-scoped namespaces. One public type per file; file name matches the type.

**Naming**
- `PascalCase` for types, methods, properties, constants.
- `camelCase` for locals and parameters.
- `_camelCase` for private fields.
- `I`-prefix for interfaces (e.g. the parser interface).

**Design**
- The node model should be **immutable** — use records/read-only types. Nodes are
  data produced by parsers and consumed by everyone else; they should not be
  mutated after creation.
- Prefer **dependency injection**; wire concrete implementations only at the
  composition root (`Server`). Lower layers depend on interfaces.
- For **expected failures** (a file that won't parse, a missing session, a corrupt
  index) prefer explicit result types over throwing. Reserve exceptions for
  truly exceptional states.
- Pass `CancellationToken` through async work; suffix async methods with `Async`.

**Documentation & comments**
- XML doc comments on all public APIs, especially the node model and parser
  interface (these are the contracts other code and future languages rely on).
- Comment *why*, not *what*. Don't narrate obvious code.

**Testing**
- Every phase ships with tests before the next phase starts.
- Test the parser against real C# snippets covering the awkward cases: nested
  types, generics, multi-line signatures, expression-bodied members, comments and
  strings that contain keywords/braces.
- Test the core loop (search → summary → retrieve) and storage round-trips
  (write → read → verify) independently of any language.

---

## Phase roadmap (build in this order)

Do **not** start a phase until the previous phase's deliverable works and is
tested. Full detail per phase is in `code-index-build-plan.md`.

**v1 — usable C# indexer**
0. **Foundations** — define the node model + parser interface + scope model in
   `Core`. Most important phase; get the contract right first.
1. **Session management** — marker-directory anchor, walk-up root discovery,
   session keyed by root path, nested-session handling.
2. **File discovery** — enumerate + filter by extension, honor `.gitignore`, skip
   `bin/`/`obj/`/generated/binaries.
3. **C# parser (Roslyn)** — first implementation of the parser interface; emit
   nodes with scope separator `.` and `using` imports; skip unparseable files.
4. **Binary storage** — `.bin` schema, version header, atomic temp-file+rename
   writes, corruption→rebuild fallback.
5. **Search & retrieval** — ranked summary search with filters; get-node-code by
   ID with location + content hash.
6. **Structure & navigation** — directory tree, file→nodes map, namespace outline,
   locate-file. **End of v1.**

**Intelligence**
7. **Relationships (edges)** — containment, call graph, inheritance, imports.
8. **Reference lookup** — find-usages (reverse call graph). Note: full cross-file
   accuracy comes from Roslyn's semantic model for C#; future structural-only
   languages give best-effort edges.

**Automation & interface**
9. **Incremental updates & git hooks** — manual full re-index (source of truth);
   file-granularity incremental; handle deletes/renames; **post-commit** +
   post-merge + post-checkout hooks running in background; install command;
   verify/repair for drift.
10. **AI interface** — MCP server (preferred) exposing index/search/get-code/
    structure/find-references/callers-callees/verify; CLI as secondary surface.
11. **Robustness** — syntax errors, oversized files/nodes, unsupported extensions,
    clear skip logging. Harden throughout.

**Later (do not build speculatively during v1):** adding a second language =
a new `Parsing.<Lang>` module backed by a real grammar (e.g. tree-sitter),
implementing the same interface. Let the real second language shape any interface
changes — don't guess them from C# alone.

---

## Working conventions

- **Respect the phase gates.** Finishing and testing a phase before the next one
  keeps the abstraction honest and prevents rework.
- **Keep the parser interface stable.** If a phase seems to need a change to the
  node model or interface, flag it explicitly and justify it — this is a
  high-impact change.
- **Definition of done for a phase:** deliverable works end to end, has tests, and
  introduces no language-specific logic outside a parser module.

## Build & test

Once the solution is scaffolded:

- Build: `dotnet build`
- Test: `dotnet test`
- Run the interface: `dotnet run --project CodeIndexer.Server`

Keep these commands working from a clean checkout at all times.