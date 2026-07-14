# CodeIndexer — instructions for an AI agent

You have access to a command-line tool called **CodeIndexer** that lets you search and navigate a codebase without reading every file. It supports C# (`.cs`), JavaScript (`.js`/`.jsx`/`.mjs`/`.cjs`), and TypeScript (`.ts`/`.mts`/`.cts`).

The executable is at:
```
c:\System\Code base indexing\publish\CodeIndexer.Server.exe
```

Run it from a shell (PowerShell: prefix with `&` since the path has spaces).

## Step 1 — Index the project (only needed once, or after big changes)

```powershell
& "c:\System\Code base indexing\publish\CodeIndexer.Server.exe" index "<path-to-project>"
```

This creates a `.codeindex\` folder inside `<path-to-project>` holding the index. It reports how many files/nodes it indexed and lists any files it had to skip (syntax errors, etc.) — that's normal and not a failure.

## Step 2 — All other commands run from *inside* the indexed folder

```powershell
cd "<path-to-project>"
& "c:\System\Code base indexing\publish\CodeIndexer.Server.exe" <command> <argument>
```

They resolve their session from the current directory (walking up to find `.codeindex\`), not from a path argument.

## Commands

| Command | Argument | What it does |
|---|---|---|
| `search` | name/fragment | Ranked name search. Prints `id  [language]  kind  name` per hit — nothing more. |
| `info` | nodeId | Prints kind, qualified name, file:line, signature, and doc comment for one node. |
| `get-code` | nodeId | Prints the full source body of one node. |
| `tree` | — | Directory tree of every indexed file. |
| `outline` | — | Namespace/class/method outline (organized by code scope, not folders). |
| `locate` | fragment | Find files by name or path fragment. |
| `refs` | nodeId | Every node that references this one, via **any** relationship. |
| `callers` | nodeId | Methods that call this method. |
| `callees` | nodeId | Methods this method calls. |
| `subtypes` | nodeId | Types that `extends`/`implements` this one. |
| `usages` | nodeId | Parameters/fields/properties typed as this one — e.g. constructor dependency injection. |
| `update` | — | Incremental re-index: only re-parses changed/added files, drops deleted ones. Much faster than `index` after small edits. |
| `verify` | — | Reports which files are stale vs. the index, without changing anything. |
| `install-hooks` | — | Installs git hooks (post-commit/post-merge/post-checkout) that run `update` automatically in the background. |

## Recommended workflow for answering a question about the code

1. **`search <term>`** to find candidate symbols. Use the `[language]` and `kind` columns to disambiguate same-named hits across files/languages (e.g. a TS interface vs. a C# class both named `Order`).
2. **`info <nodeId>`** on the most likely candidate to confirm it's the right one (check the file path and signature) before spending a full `get-code` call on it.
3. **`get-code <nodeId>`** to read the actual implementation.
4. For "who else touches this" questions, use **`refs`** (broadest), or the narrower **`callers`** / **`callees`** / **`subtypes`** / **`usages`** depending on exactly what relationship you're asking about.

## Known limitations (so you don't over-trust the output)

- `refs`/`callers`/`callees`/`usages`/`subtypes` are **best-effort name matching**, not full semantic analysis. If a name is ambiguous (e.g. two classes named `Handler`, or an interface and its implementation both declaring `AddAsync`), the relevant edge is **silently skipped** rather than guessed at. Absence of a result doesn't always mean "truly unused" — it can mean "ambiguous, so I didn't claim it."
- `usages` only catches a type named directly as a parameter/field/property type (including one level of generic unwrapping, e.g. `Task<AuthService>` → `AuthService`). It does **not** catch a symbol passed by reference without being typed that way (e.g. `canActivate: [someGuardFunction]` in a route config).
- Relative-path JS/TS imports (`import x from '../../shared/foo'`) are not resolved to the file they point to.
- `search` matches only on the **name** of a symbol, not its body text — it won't find a function by what it *does*, only by what it's *called*.

## Example session

```powershell
& "c:\System\Code base indexing\publish\CodeIndexer.Server.exe" index "C:\MyProject"
cd "C:\MyProject"
& "c:\System\Code base indexing\publish\CodeIndexer.Server.exe" search AuthService
& "c:\System\Code base indexing\publish\CodeIndexer.Server.exe" info <id-from-above>
& "c:\System\Code base indexing\publish\CodeIndexer.Server.exe" usages <id-from-above>
& "c:\System\Code base indexing\publish\CodeIndexer.Server.exe" get-code <id-from-above>
```
