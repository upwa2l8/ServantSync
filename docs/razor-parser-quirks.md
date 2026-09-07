# Razor parser quirks

> **Audience**: anyone editing `.razor` files under `Components/**`.
> Read this before opening a PR that touches a Razor file. The
> .NET 9 Razor SDK has a known parser quirk that produces misleading
> cascading errors and crashes the CI Release build — this doc is
> the one-stop reference for not hitting it.

## TL;DR

The single most important rule:

> **Never start a switch arm with a bare relational pattern** (e.g.
> `< 0 =>`, `<= 10 =>`, `> 5 and < 10 =>`). Wrap it in a `_ when`
> guard clause so the arm's first token is not `<`.

```csharp
// ❌ BREAKS the .NET 9 Razor SDK parser in Release builds
return d switch
{
    < 0 => $"{Math.Abs(d)}d overdue",   // Razor sees `< 0` as the start of an HTML tag
    0 => "today",
    var v => $"{v}d left"
};

// ✅ SAFE: `_ when` puts a non-`<` token first, locking the parser in C# mode
return d switch
{
    _ when d < 0 => $"{Math.Abs(d)}d overdue",
    0 => "today",
    var v => $"{v}d left"
};
```

A `dotnet build -c Release` (matching the CI Docker build) catches
this locally before you push. **The Debug build silently tolerates
the parser quirk and lets the bug ship.** That is the single most
important reason this footgun is dangerous.

---

## The bug we hit

`Components/Pages/Organizations/Training/DueSoon.razor:466` shipped
in Round-FR-6 (commit `6721e66`) with this in the `DaysLabel`
helper:

```csharp
private static string DaysLabel(TrainingDueSoonRow r)
{
    var d = r.DaysDelta ?? 0;
    return d switch
    {
        < 0 => $"{Math.Abs(d)}d overdue",   // ← this line broke the build
        0 => "today",
        var v => $"{v}d left"
    };
}
```

The .NET 9 Razor SDK parser saw the bare `< 0` at the start of a
switch arm and interpreted it as the opening of an HTML tag
(`< 0 …>`). It exited C# parse mode, then re-parsed the entire
`@code { … }` block as markup. This cascaded into 30+ errors all
pointing at the `@code {` token (line 267) — the user-visible cause
was the switch arm on line 466 but the parser had lost track of
brace balance, so the error messages were all misleading.

The Debug build (`dotnet build -c Debug`) tolerates the parser
quirk. The Release build (which is what `docker build` runs in CI
via `Dockerfile:46`) crashes. Local dev runs Debug by default, so
the bug shipped through `6721e66` → `3a7c607` → `bde3401` →
`695725c` and finally blew up on the FR-7 deploy.

Fixed in `e8d3911` with a single-line change:

```diff
- < 0 => $"{Math.Abs(d)}d overdue",
+ _ when d < 0 => $"{Math.Abs(d)}d overdue",
```

The build went green. Per-slot volunteer interest (FR-7) rolled out
to Azure immediately after.

---

## Why the `_ when` guard works

The Razor parser's HTML-tag state machine is triggered by a bare
`<` token at the start of a statement or expression in C# context.
Inside `_ when d < 0`, the `<` is mid-expression — preceded by `d`,
a non-whitespace identifier that the parser is already committed
to reading as C#.

The same protection applies to:
- `>= 0 and < 10 =>` — start with `>=` (a non-`<` token).
- `not 100 and < 1000 =>` — start with `not`.
- A pure-relational arm written with a discard: `_ when d < 0 =>`.

In every case, **as long as the switch arm's first token is not a
bare `<`, `<=`, `>`, or `>=`, the parser stays in C# mode**.

The pattern is also already used elsewhere in the codebase
correctly:

- `Components/Pages/Teams/Detail.razor:147`
  ```csharp
  (var u, var t) when u < t => "L",
  ```
  This is the canonical "see, this is what good looks like" example.

---

## What's safe vs. what's not

| Pattern | Verdict | Why |
|---|---|---|
| `_ when d < 0 => …` | ✅ SAFE | `<` is mid-expression, parser locked in C# |
| `< 0 => …` | 🐛 BROKEN | `<` at start of arm triggers HTML-tag state machine |
| `<= 0 => …` | 🐛 BROKEN | Same — starts with `<` |
| `> 5 => …` | ⚠️ SUSPECT | `>` is not a Razor token, but matches no known-good pattern; verify with Release build |
| `>= 0 and < 10 => …` | ✅ SAFE | First token is `>=` |
| `case < 0:` in a switch **statement** | 🐛 BROKEN | Same parser quirk as switch-expression arms |
| `if (bytes < 1024) …` in `@code` | ✅ SAFE | `<` is inside a parenthesized `if (…);` parser tracks balanced parens |
| `a.StartUtc < toUtc` in a LINQ chain | ✅ SAFE | Mid-expression, locked in C# mode by `&&` / `where` |
| `List<int> _rows = new();` in `@code` | ✅ SAFE | Once in `@code { … }`, Razor commits to C# mode |
| `=> _x < 5 ? "a" : "b"` (expression body) | ✅ SAFE | `<` is mid-expression after `_x` |
| `=> _x ?? 0` (null-coalescing) | ✅ SAFE | `?` is not a Razor transition |
| `$"hello {x}"` (string interpolation) | ✅ SAFE | Strings are handled by the C# lexer in `@code` |
| `$"literal {{brace}}"` (escaped literal `{`) | ✅ SAFE | `{{` / `}}` are the C# escapes; Razor delegates |
| `@@media print { … }` (CSS `@`-rule) | ✅ SAFE | `@@` is the Razor escape for literal `@` |
| `@MyMethod<int>()` (generic in markup) | 🐛 BROKEN | Razor sees `<int>` as an HTML tag; must wrap: `@(MyMethod<int>())` |
| `@onclick="e => x < 5"` (attribute lambda) | ⚠️ SUSPECT | Works in .NET 9 SDK but fragile; wrap in parens defensively |

The two ⚠️ SUSPECT entries are theoretically safe per the .NET 9
SDK source, but warrant defensive rephrasing when they're touched
because the surface area is small and the cost of a future SDK
regression breaking them is high.

---

## Code-review checklist

For every PR that touches a `.razor` file under `Components/**`:

- [ ] **No switch arm starts with a bare `<` / `<=` / `>`.** If it
      does, wrap the relational pattern in a `_ when` guard clause:
      `_ when x < 0 => …` instead of `< 0 => …`. Same applies to
      `case < 0:` in switch statements (the same parser quirk hits
      both forms).
- [ ] **No generic method call in markup starts a Razor expression
      with `@Method<Type>(...)`.** If it does, wrap in explicit
      parens: `@(Method<Type>(...))`.
- [ ] **Any inline attribute lambda with a `<` operator is wrapped
      in parentheses:** `@onclick="() => (x < 5) ? A : B"`, not
      `@onclick="() => x < 5 ? A : B"`. The bare form works in
      .NET 9, but the parenthesized form survives future parser
      changes.
- [ ] **No literal `{` / `}` characters inside `$"…"` strings in
      `@code` blocks.** C# escapes these as `{{` / `}}`; the
      Razor parser delegates to the C# lexer for string content
      in `@code`, so unescaped braces break the C# compile (not
      the Razor parse), but a quick grep is cheap insurance.
- [ ] **`@code` blocks end with a matching `}` before the closing
      directive.** A mismatched brace balance inside a switch arm
      or other pattern can leak out of the `@code` block and
      produce the cascade of misleading errors we saw with
      DueSoon.razor.
- [ ] **Pre-push: `dotnet build -c Release --nologo` is clean.**
      Debug builds tolerate the parser quirk that Release does
      not. Running Release locally catches this class of bug in
      seconds, not in CI.

If any of these surface in a PR, request changes and link this
doc from the review comment so the contributor can self-serve the
fix.

---

## Pre-push verification

```bash
# Matches the parser path used by the CI Docker build step
# (Dockerfile:46 — `dotnet publish -c Release`). A Razor parser-quirk
# bug that breaks `dotnet build -c Release` will break the CI publish
# step too. A Debug build that passes locally MIGHT fail in CI.
dotnet build ServantSync.csproj -c Release --nologo
```

For full publish parity (asset stripping, appsettings.Development.json
removal, `.pdb` deletion per `Dockerfile:46`), use:

```bash
dotnet publish ServantSync.csproj -c Release -o ./obj/publish-test
```

This is a few seconds slower than `build` but exercises the same
output pipeline as the CI Docker step. Use it when the change touches
any of: `wwwroot/`, `appsettings.*.json`, the project file, or a
new asset-copy rule.

For belt-and-suspenders, also run the test suite — it surfaces a
different class of bug but is fast (~25s on this machine):

```bash
dotnet test tests/ServantSync.Tests/ServantSync.Tests.csproj -c Debug --nologo
```

A 5-second Release build + 25-second test run before every push
catches both the parser-quirk class and the test-fixture-drift
class of bug (the latter was the `AssignmentServiceTests.Now`
hard-coded-pin issue fixed in `2757eb4`).

---

## Audit history

| Date | Commit | What was found |
|---|---|---|
| 2026-07-09 | `2757eb4` | Unrelated: fixed an `AssignmentServiceTests.Now` hard-coded-pin drift (the pin slipped into the past as wall-clock advanced). Wall-clock-anchored `_nowPinned` cache replaces the literal. |
| 2026-07-09 | `e8d3911` | **The fix.** Single-line change in `Components/Pages/Organizations/Training/DueSoon.razor:466`: `< 0 => …` → `_ when d < 0 => …`. Unblocked the FR-7 Release publish. |
| 2026-07-09 | (audit) | 50 .razor files swept across 8 pattern classes (bare `<` in switch arms / `case` statements, generics in markup, attribute lambdas with `<`, `$"…"` interpolation, `@*` block comments, null-coalescing / null-conditional, ternary in expression bodies, `@@` escapes). **0 LIKELY-BROKEN sites remain.** 2 low-priority SUSPECT sites (`Components/Pages/ServiceSlots/Detail.razor:506-507` and `Components/Pages/Home.razor:282`) are theoretically safe (parenthesized / mid-expression) and unchanged. |

The pre-existing safe pattern in
`Components/Pages/Teams/Detail.razor:147` —
`(var u, var t) when u < t => "L"` — was already using the `when`
guard form before this doc existed. That site is the "see, this is
what good looks like" example for the checklist above.
