# Code Intelligence Indexer — Build Plan

A code-indexing and retrieval tool for AI agents. It parses a codebase into
searchable "nodes" (namespaces, classes, methods, etc.), stores them, and lets
an AI navigate the code without loading the whole repo into its context. The AI
searches for nodes, gets lightweight summaries back, picks one, and requests the
full code on demand.

Build this in **phases**. Ship a working C#-only v1 first (Phases 0–6), then add
intelligence (Phases 7–9), then automation (Phase 10), then interface and
hardening (Phases 11–12). Do not build speculative multi-language machinery
before v1 works end to end.

---

## Fixed decisions (constraints for the whole build)

- **Implementation language:** C#.
- **First and only supported target language for v1:** C#, parsed with **Roslyn**
  (Microsoft.CodeAnalysis) — full syntax tree + semantic model, native
  namespace/type/symbol resolution.
- **Language support model:** each language gets its **own parser module written
  in code**, implementing one shared parser interface. There is **no** JSON/config
  system that tries to describe grammar. Adding a language later = writing a new
  parser module against the shared interface, with **zero changes** to the shared
  core. (Rationale: a real per-language parser is correct on real code; a
  config/token-scanning approach is not.)
- **Storage:** a binary (`.bin`) index file, written directly, with a version
  header, atomic writes (temp file + rename), and corruption handling.
- **Session model:** the first `index` run at a path establishes a session
  anchored there; commands run from any child directory resolve to the same
  session; the session is identified by its root path.

---

## The central architecture rule

Two halves, separated by one interface. **The shared core must never know which
language it is dealing with.**

- **Per-language (new code per language):** the parser. Integrates that
  language's real grammar/parser, walks its tree, extracts constructs, and maps
  them into the shared node model — including that language's own scope rules
  (C# namespaces joined by `.`; another language's modules by whatever it uses).
  This is the only part that grows when a language is added.
- **Shared, written once:** node model, `.bin` storage, search/ranking, code
  retrieval, structure/navigation, relationships, incremental updates + git
  hooks, and the AI interface. None of this is touched when a language is added.

**Hard discipline:** no language-specific logic ever leaks upward. If any shared
component ever contains a branch like "if language is C#…", the design has broken.
Everything language-specific stays sealed inside that language's parser module,
expressed only through the shared node model.

---

## Phase 0 — Foundations: the node model + parser interface

This is the most important phase. Everything depends on getting the contract
right, because it is what makes the core language-agnostic and lets future
languages plug in as pure additions.

- **Define the generic node model** — the single shape every parser must emit:
  - stable unique **ID**
  - **fully-qualified name**, built from the node's **scope chain** (this is how
    namespaces are handled generically, not as a C# special case)
  - **location:** file path, start line, end line
  - **node type**, from a fixed generic taxonomy: namespace, class, interface,
    struct, enum, method, property, field, constant, import/using
  - **summary (cheap):** name, signature, parameters, return type, leading
    doc-comment, line count — what the AI browses
  - **body (expensive):** the full source of the node — returned only on demand
  - **metadata flags:** public/private, static, async, is-test, etc.
  - (later) **edges:** references to related nodes (Phase 8)
- **Define the parser interface** every language module implements: given a file,
  return nodes in exactly the model above. The core interacts only with this
  interface, never with Roslyn or any concrete parser.
- **Define the scope model** generically: a node's fully-qualified name is its
  scope chain joined by a language-supplied separator.

Deliverable: a stable node model and parser interface. No parsing yet.

---

## Phase 1 — Session & workspace management

The "run once, then it just knows" behavior.

- **Session anchor:** the first `index` run creates a hidden marker directory
  (e.g. `.codeindex/`) at the current path. It holds the index file and session
  metadata. Its path is the session root.
- **Root discovery:** every command walks *up* the directory tree from the
  current working directory until it finds the marker. That directory is the
  active session — so running a command from a deep child still targets the same
  session.
- **Session identity:** keyed/named by its root path.
- **New vs existing:** no marker found walking up → `index` starts a new session
  here; search/retrieve report "no session here."
- **Nested sessions:** walk-up finds the *nearest* marker (like nested git
  repos); the closest one wins. State this explicitly.
- **Optional registry:** a global list of known session roots keyed by path, for
  listing/inspecting/removing sessions.

Deliverable: run any command from anywhere in the tree and resolve to the correct
session automatically.

---

## Phase 2 — File discovery

- Enumerate files under the session root.
- Select files by the extensions the registered parser(s) own (C# only in v1).
- **Exclusion rules:** respect `.gitignore`; skip `bin/`, `obj/`,
  `node_modules/`, build output, generated files, binaries. Make it configurable.

Deliverable: an accurate, filtered source-file list for any repo.

---

## Phase 3 — C# parser (Roslyn)

The first implementation of the Phase 0 parser interface.

- Parse each C# file into a Roslyn syntax tree; use the semantic model to resolve
  namespaces, types, and symbols.
- Walk the tree and emit nodes in the shared model. C#'s scope separator is `.`.
- Emit `using` directives as import nodes.
- Resilient parsing: a file that fails to parse is logged and skipped, never
  crashes the run (see Phase 12).
- This module knows nothing about storage, search, or sessions — it only produces
  nodes.

Deliverable: C# source → complete, correct nodes via the shared interface.

---

## Phase 4 — Binary storage layer (shared)

- Define the **serialization schema** for nodes and session metadata.
- **Version header:** every file starts with a format version; a mismatch
  triggers a rebuild, never a misread.
- **Atomic writes:** write to a temp file, then rename over the real one, so a
  crash mid-write can't corrupt the index.
- **Access strategy:** decide whether the whole index loads into memory (fine for
  small/medium repos) or uses an internal offset table for random access (as
  repos grow). Keep this swappable without touching callers.
- **Corruption handling:** detect a bad/truncated file and fall back to a clean
  rebuild.

Deliverable: reliable save/load of the full index to and from `.bin`.

---

## Phase 5 — Search & retrieval (the core AI loop)

The two-step flow: search returns summaries, retrieval returns code.

- **Search nodes:** exact name, fuzzy/partial name, with filters (node type,
  namespace, directory, metadata flags). Returns **summaries only**, ranked.
- **Ranking:** best matches first — a search may return many nodes.
- **Get node code:** given a node ID, return the full body plus location and a
  content hash/version for staleness detection.
- (Later enhancement) **Semantic search:** match intent, e.g. "the function that
  handles login" matching `VerifyToken`.

Deliverable: AI can search → receive summaries → pick → fetch full code. This is
v1's core value.

---

## Phase 6 — Structure & navigation (shared)

Let the AI zoom out before zooming in.

- **Directory tree** view.
- **File-to-nodes map:** what's defined in a given file.
- **Namespace/module outline:** the codebase organized by scope, not folders.
- **Locate file** by name or path fragment.

Deliverable: retrievable "map" views the AI can request to orient itself.

**End of v1.** Phases 0–6 give a usable product: session-aware indexing of C#,
`.bin` storage, and the full search → retrieve loop with structure views.

---

## Phase 7 — Relationships (edges)

Turn the flat node list into a graph.

- **Containment:** method → type → namespace → file.
- **Call graph:** which node calls which (Roslyn's semantic model makes this
  reliable for C#).
- **Inheritance/implements:** class extends/implements which types.
- **Imports:** which file/namespace depends on which.

Store edges in the node model so the core stays language-agnostic.

Deliverable: traversable relationships between nodes.

---

## Phase 8 — Reference lookup

- **Find references/usages:** given a node, return everywhere it is called or
  referenced (reverse of the call graph). Essential for AI impact analysis and
  refactoring.

**Semantic tiering note:** C#/Roslyn delivers accurate call graphs and cross-file
references because it resolves symbols. A future language whose parser is
structural-only would provide edges on a best-effort basis and can be deepened
later. The core consumes whatever edges a parser produced — no core changes
needed either way.

Deliverable: reverse lookup from any node to its usage sites.

---

## Phase 9 — Incremental updates & git hooks (shared)

Keep the index fresh automatically without full re-scans.

- **Manual full re-index** command — the source of truth; must exist before any
  automation is layered on.
- **Incremental update:** re-parse only changed files, at **file granularity** —
  if any line in a file changed, rebuild that whole file's nodes. This
  auto-fixes shifted line numbers of unchanged nodes.
- **Deletions/renames:** removing a file must remove its nodes (rename = delete +
  add). No orphaned nodes.
- **Git hooks:**
  - Use **post-commit** — never pre-commit; never block or slow a commit.
  - Also hook **post-merge** and **post-checkout** (pulls and branch switches
    change files that never hit this machine's commit hook).
  - Derive changed files from `git diff`; re-index only those.
  - Run indexing in the **background** so the terminal returns immediately.
  - Provide an **install command** — `.git/hooks` is not cloned or shared.
- **Drift is expected** (`--no-verify`, GUI clients, rebases, other machines), so
  add a lightweight **verify/repair** mode that hashes files and reconciles the
  store against reality.

Mental model: hook keeps it fresh most of the time; manual re-index rebuilds;
verify detects drift.

Deliverable: commits and pulls quietly keep the index current, with manual
rebuild and repair as safety nets.

---

## Phase 10 — AI interface (shared)

- Expose operations as a clear toolset: `index`, `search`, `get node code`,
  `get structure`, `find references`, `get callers/callees`, `verify/repair`.
- Given the use case is AI agents, prefer exposing these as an **MCP server** (the
  standard way to hand an AI a toolset), with a CLI as a secondary/manual surface.
- Every response must be self-describing enough to chain the next call (IDs,
  locations, hashes).

Deliverable: a stable tool surface an AI agent can drive end to end.

---

## Phase 11 — Robustness & edge cases

One bad input never breaks a run.

- Syntax-error files: log and skip, or partial-parse; never crash.
- Unsupported extensions: ignore cleanly.
- Very large generated files: skip or cap.
- Oversized single nodes that could blow an AI's context: flag and/or offer
  truncated retrieval.
- Clear logging after each run of what was skipped and why.

Deliverable: predictable behavior on messy real-world repos.

---

## Adding a language later (post-v1, for reference)

When it's time for a second language, the work is contained:

1. Write a new **parser module** implementing the Phase 0 interface, backed by
   that language's real grammar/parser (e.g. tree-sitter), emitting the shared
   node model with that language's scope rules.
2. Register its file extensions.
3. Ship it — storage, search, structure, retrieval, hooks, and the AI interface
   already work for it unchanged.

Do **not** design this second parser speculatively during v1. Let the real second
language define whatever the parser interface still needs, so the abstraction is
generalized from two real languages instead of guessed from one.

---

## Suggested build order

Phases 0 → 6 = working C# v1 (session, parse, store, search/retrieve, structure).
Then 7–8 (edges, references) add AI-grade intelligence. Then 9 (incremental +
hooks) automates freshness on a solid manual re-index. Then 10 (MCP interface)
and 11 (robustness) harden it. Phase 0's node model + parser interface is the
foundation for all of it — get it right first.