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
| `search` | name/fragment `[--all]` | Ranked name search. Prints `id  [language]  kind  name` per hit — nothing more. Hides `Import`/`Field` hits by default since they're usually reference-site noise, not the declaration you want; pass `--all` to include them. |
| `info` | nodeId | Prints kind, qualified name, file:line, signature, and doc comment for one node. |
| `get-code` | nodeId | Prints the full source body of one node. |
| `children` | nodeId | Lists the direct members (methods/fields/nested types) declared inside this node — a cheap alternative to `get-code` when you just want a class's shape, not its full body. Prints `id  kind  name  (line N)` — no path or qualified-name repeated per row, since every child is always in the same file as its container. |
| `tree` | `[path]` `[--depth N]` `[--full]` | Directory tree of indexed files, optionally scoped to a subfolder. Capped at depth 3 by default — deeper folders print `... N more entries not shown` inline rather than dumping everything (large repos with vendored/third-party trees can otherwise produce thousands of irrelevant lines before anything relevant shows up). Use `--depth N` to raise the cap or `--full` for the old unlimited behavior. |
| `outline` | — | Namespace/class/method outline (organized by code scope, not folders). |
| `locate` | fragment | Find files by name or path fragment. |
| `refs` | nodeId | Every node that references this one, via **any** relationship. |
| `callers` | nodeId | Methods that call this method. Only valid on a `Method` node — calling it on any other kind prints an explanatory error instead of a silent empty result. |
| `callees` | nodeId | Methods this method calls. Same `Method`-only restriction as `callers`. |
| `subtypes` | nodeId | Types that `extends`/`implements` this one. Only valid on a `Class`/`Interface`/`Struct` node. |
| `usages` | nodeId | Parameters/fields/properties typed as this one — e.g. constructor dependency injection. Only valid on a `Class`/`Interface`/`Struct`/`Enum` node. |

`refs`/`callers`/`callees`/`subtypes`/`usages` results can legitimately span multiple files (unlike `children`, which is always single-file), so the path isn't dropped — but it's printed once as a group header per file rather than repeated on every row:
```
C:\path\AuthService.cs
  <id>  Method  ...GetUserRoleNameAsync  (line 582)
  <id>  Method  ...BuildAccessClaims     (line 572)
C:\path\OtherFile.cs
  <id>  Method  ...SomeOtherCaller       (line 12)
```
| `update` | — | Incremental re-index: only re-parses changed/added files, drops deleted ones. Much faster than `index` after small edits. |
| `verify` | — | Reports which files are stale vs. the index, without changing anything. |
| `install-hooks` | — | Installs git hooks (post-commit/post-merge/post-checkout) that run `update` automatically in the background. |

## Recommended workflow for answering a question about the code

0. For orientation on an unfamiliar/large project, **`tree`** (default depth-3) or **`tree <subfolder>`** to scope to the area you care about — don't reach for `--full` unless you actually need every leaf file, since large repos can bury the relevant folders under thousands of vendored/third-party files otherwise.
1. **`search <term>`** to find candidate symbols. Use the `[language]` and `kind` columns to disambiguate same-named hits across files/languages (e.g. a TS interface vs. a C# class both named `Order`). Add `--all` if you're specifically hunting for imports or field declarations, since those are hidden by default.
2. **`info <nodeId>`** on the most likely candidate to confirm it's the right one (check the file path and signature) before spending a full `get-code` call on it.
3. **`children <nodeId>`** when you just need a class/namespace's shape (its member list) rather than the full implementation — much cheaper than `get-code`.
4. **`get-code <nodeId>`** to read the actual implementation.
5. For "who else touches this" questions, use **`refs`** (broadest), or the narrower **`callers`** / **`callees`** / **`subtypes`** / **`usages`** depending on exactly what relationship you're asking about — but first check the node's `kind` (from `search`/`info`) matches what the command expects, or it'll just tell you so and do nothing.

## Known limitations (so you don't over-trust the output)

- `refs`/`callers`/`callees`/`usages`/`subtypes` are **best-effort name matching**, not full semantic analysis. If a name is ambiguous (e.g. two classes named `Handler`, or an interface and its implementation both declaring `AddAsync`), the relevant edge is **skipped** rather than guessed at. This is no longer fully silent: if the command would otherwise print `(none found)`, it now also checks whether any node recorded a matching ambiguity note and, if so, prints something like `(none found — but 2 ambiguous resolution(s) elsewhere in the project may involve 'Handler', not asserted as edges)` followed by the specific skipped attempts. This is a name-text match against diagnostic notes, not a resolved edge — treat it as a hint to go look, not a guarantee the skip concerns your exact node.
- `usages` only catches a type named directly as a parameter/field/property type (including one level of generic unwrapping, e.g. `Task<AuthService>` → `AuthService`). It does **not** catch a symbol passed by reference without being typed that way (e.g. `canActivate: [someGuardFunction]` in a route config).
- Relative-path JS/TS imports (`import x from '../../shared/foo'`) are not resolved to the file they point to.
- `search` matches only on the **name** of a symbol, not its body text — it won't find a function by what it *does*, only by what it's *called*.
- `tree <path>` requires an **exact (case-insensitive) directory name** at each path segment — it's not fuzzy/fragment matching like `locate`. `tree src` won't match a folder named `Source`, and `tree comms` won't match `MFDP.CommsHub.Api`. If scoping returns `(no indexed folder matches '...')`, try `tree` unscoped or `locate` first to find the exact folder name.
- The default `--depth 3` cap only limits how deep **folders** recurse — every file already reached within that depth is still listed in full, and files themselves are never counted against the depth (only subfolders are). A folder full of thousands of files at an already-visible depth will still print every one of them; `--depth` doesn't cap file *count*, only folder *nesting*.

## Example session

```powershell
& "c:\System\Code base indexing\publish\CodeIndexer.Server.exe" index "C:\MyProject"
cd "C:\MyProject"
& "c:\System\Code base indexing\publish\CodeIndexer.Server.exe" search AuthService
& "c:\System\Code base indexing\publish\CodeIndexer.Server.exe" info <id-from-above>
& "c:\System\Code base indexing\publish\CodeIndexer.Server.exe" children <id-from-above>
& "c:\System\Code base indexing\publish\CodeIndexer.Server.exe" usages <id-from-above>
& "c:\System\Code base indexing\publish\CodeIndexer.Server.exe" get-code <id-from-above>
```
