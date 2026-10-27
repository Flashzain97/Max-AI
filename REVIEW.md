# Review of the Max project

## Current status (8 October 2026): Max 0.4.0

The supported C# interpreter now builds with the locally installed Microsoft .NET SDK 10.0.401, targeting .NET 10. The downloaded Windows x64 SDK was checked against Microsoft's published SHA512 checksum. `Build-Max.ps1` builds and tests the source before replacing the standalone `max.exe`; its runtime is included. `Setup-Max.ps1` reproduces the local setup without administrator rights or a global PATH change. PowerShell 7.4+ can still compile and run the source directly.

The language keeps its existing words and strengthens their behavior:

- All ordinary runs now check syntax, names, argument counts and known types before executing. `--check` performs those checks without running the program; `--syntax-only` retains the earlier grammar-only check.
- `--strict` requires annotated declarations, parameters and results, named list element types, checks dormant function bodies, and requires non-optional results on all reachable contract paths. `returns nothing` describes an action-only function. Ordinary mode preserves dynamic variables and defers uncertain return completeness to runtime.
- `input`, `integer`, `decimal`, `append` and `remove` provide practical input, explicit conversion and list operations. Conversions reject ambiguous formatting, fractions when an integer is requested, overflow and nonfinite numbers.
- Typed list returns and resorts now constrain their shared aliases immediately. Type constraints for nested list graphs and all function parameters are planned together before application, so validation failures do not leave partially typed aliases. List writes also recheck their index after a right-hand expression removes an element.
- Built-in names cannot be declared or used as parameters. Visible variable/function name collisions are rejected; same-kind lexical shadowing remains supported.
- Errors show a file, line, column, source line and caret. Individual expressions map to their own source positions; function errors retain their call chain. Missing blocks and delimiters identify their opener, and common keyword/type typos receive suggestions.
- Lexer repairs cover CR-only comments, unfinished escapes, escaped physical newlines and adjacent number/name text. Conflicting list type forms are rejected. Existing same-line statements, optional types and trailing list/call commas remain supported.

The 0.4.0 audit adds these repairs:

- Compound assignment captures the original variable or indexed element before evaluating the right-hand expression. A right-hand function call that changes the same variable no longer changes the value used in that calculation. Target, index and value expressions run once, in that order; writes still reject indexes removed while evaluating the value.
- `foreach` captures both its elements and their element rule at entry. Attaching a new constraint to the original list during a loop no longer changes the contract of items already in the snapshot.
- Numeric conversions reject embedded NUL characters instead of allowing the host number parser to ignore a hidden suffix. Nested graph type checking visits distinct list/type pairs rather than repeatedly expanding shared aliases; a 40-level repeated graph is covered by a regression.
- The static checker rejects conflicting known constraints on typed mutable list aliases, including numerical widening and optional element changes. Fresh untyped literals still receive element-by-element validation.
- Direct `== nil` and `!= nil` guards, including their negations and opposite branches, establish temporary facts for optional values. This catches known nil arithmetic and collection use, and wrong optional inner types after a non-nil guard. Facts use binding identity, stay inside the branch, and are discarded by assignments and user function calls. A mutable loop condition being true at entry does not satisfy strict return completeness.
- An optional value that may be nil can use a correctly typed resort even when its non-nil type differs. A non-nil guard still rejects a wrong type; actual values remain checked at runtime.
- Names now handle supplementary Unicode letters and digits, while numeric literals require ASCII digits `0` through `9` with actionable errors for other digits. End-of-file diagnostics retain the true source position after whitespace and comments, escaped strings retain their source span, and supplementary symbols stay intact in errors. Leading zeros preserve the minimum 32-bit integer, and keyword spelling/case suggestions cover condition and branch words.

Max Studio provides a local browser editor through `Start-Studio.cmd` or `max --studio`. Its page is embedded in the executable, with no external packages or hosted editor required. Strict checking starts enabled; the editor offers run/check actions, browser drafts, opening existing `.max` files, downloads and a supplied-input box with one answer per line. Errors have clickable source locations. Loading an example or file keeps the previous draft for recovery. The terminal stays open while Studio is in use, and **Ctrl+C** stops it. The default address is `http://127.0.0.1:7337/`; `--port 0..65535` selects another port, with 0 choosing a free port, and `--no-browser` prints the address without opening it.

Each editor execution runs in a separate child process and stops after 10 seconds. Source code and supplied input are each limited to 65,536 UTF-16 characters; standard output and error text have the same limit per stream. Input writes share the timeout, including when a program never reads supplied answers. A Windows process group stops runners even if the editor is forcibly closed. The service listens on the local loopback address and checks request host/origin. Native runners use UTF-8 input/output pipes so Unicode survives Windows redirection. These editor limits do not apply to ordinary terminal runs, which retain the language's nesting and type checks.

Windows Release testing exposed a real host stack overflow in recursive exception handling that did not occur in the larger-stack PowerShell host. Recursively repeated exception filters were replaced with ordinary catches that attach context after unwinding. Recursive evaluator methods use small dispatcher helpers and optimized frames. The command-line entry point and public interpreter entry points share a consistent 16 MiB execution-thread stack, while language limits and stack checks still reject excessive nesting. Maximum function depth is 128, combined evaluation nesting is 512, and source expressions and runtime list/type nesting are bounded at 256. Internal control-flow exceptions are kept out of diagnostic call chains, and repeated calls are summarized for readability.

Validation: **395 tests passed, 0 failed** through each of the PowerShell source runner, .NET Release assembly and final standalone executable. Release builds produced **0 warnings and 0 errors**. The suite covers the original behavior plus static checks, guarded optional values, mutable aliases, compound evaluation order, numeric conversions, input/EOF, atomic type attachment, shared graphs, syntax diagnostics, safe deep nesting, small-stack public callers and command-line behavior. All six included `.max` files passed strict checks and execution, with supplied input for `examples/input.max`.

Studio passed **12 service checks**, **14 browser checks** in installed Microsoft Edge, and a real-process forced-shutdown check. These cover output/timeout recovery, large unread input, Unicode, clickable errors, all five editor examples, keyboard actions, exact file downloads, imported files, draft recovery, blocked storage, plain-text output, and layouts at 320, 390 and 768 pixels. Desktop and mobile screenshots were inspected. **20,000 deterministic malformed-source mutations** yielded 1,038 accepted programs and 18,962 safe syntax errors, with no host exceptions or invalid source locations. Reproducible checks and logs are under `.tools`: `build-040.log`, `source-tests-040.log`, `portable-tests-040.log`, `verify-studio.cjs`, `verify-studio-ui.cjs`, `verify-studio-shutdown.cjs` and `syntax-validation`.

The preflight checker remains deliberately conservative around dynamic values, optional values outside direct nil guards, literal foreach bindings and changing list contents. It does not execute user functions or input to predict values, and a write or call invalidates temporary optional facts. Runtime checks remain authoritative for actual values, indexing, overflow, uncertain shared-list constraints and input conversion. In strict mode, return checking considers every value admitted by the declared parameter type: `boolean` includes `possible`, so a conditional may require a `maybe` result or a final return. These checks do not constitute a proof of termination or total correctness.

Read [START-HERE.md](START-HERE.md) for the short beginner path and [README.md](README.md) for all rules. `first.max` is a runnable score calculator; `examples/input.max` demonstrates interactive input. Modules, first-class functions and a formatter remain future work. The unfinished compiler/Rust VM continue to be excluded and preserved.

## Earlier strictness pass (25 September 2026)

The supported execution path is the C# lexer, parser and interpreter. `Run-Max.ps1` and `Max.csproj` exclude `Compiler.cs` because that file refers to missing bytecode, task, coroutine and native-function types. The Rust files (`main.rs`, `bytecode.rs`, `vm.rs`) describe an unfinished JSON bytecode VM, but this checkout has no Cargo manifest and its indexing operation is unfinished. `message.txt` is an exact copy of `vm.rs`. These files were preserved for future work; they are not advertised as a runnable Max implementation.

This pass tightened the interpreter's rules:

- Typed values are non-nullable by default. `?` explicitly permits `nil`, including at a specific level in nested list types. A typed function with no value now fails unless its result type permits `nil`. Resorts still replace assigned `nil` before type validation.
- Text concatenation requires two strings; `text(value)` converts explicitly. `length(value)` counts list items or Unicode code points in strings. Printed booleans use the same lowercase spelling as source code.
- Equality rejects unrelated non-`nil` types. Ordering accepts two numbers or two strings; strings use ordinal order rather than length. The parser rejects chained comparisons and misplaced or duplicate `maybe`/`else` branches.
- Runtime errors now carry the innermost failing statement's line and column and a function call chain. String indexing and length operate on Unicode code points. The minimum 32-bit integer literal works.
- A `foreach` item receives a fresh binding each turn, so strict typing works for typed lists and the binding does not leak after the loop.

These are intentional compatibility changes from the first interpreter revision. Older programs that rely on implicit text conversion, string-length ordering, or `nil` in a non-optional type need updating. [README.md](README.md) and [examples/strict.max](examples/strict.max) show the current rules.

Validation: **106 regression tests passed, 0 failed** with `pwsh -File ./Run-Max.ps1 --self-test`. All four example programs and the syntax check also passed. This machine has no .NET SDK, so the SDK build path remains unverified here.

## Earlier interpreter assessment

The supplied project contained eleven C# files implementing a lexer, recursive-descent parser, syntax tree, type model and tree-walking interpreter. It was more than just invented syntax: declarations, arithmetic, typed lists, conditionals, three-valued logic and loops were implemented. However, there was no project file, entry point or automated regression suite in this folder, and several implementation bugs contradicted the examples in the conversation.

This revision keeps the basic architecture and vocabulary. Lexer.cs, Parser.cs and Interpreter.cs were refactored substantially so the new function feature and existing features share consistent parsing, evaluation, scope and type rules. This is a substantial revision to review, rather than a minimal patch.

## Every original source file

| File | Responsibility | Assessment and action |
| --- | --- | --- |
| Token.cs | A token's kind and source text | Added line and column fields with defaults, preserving the original two-argument constructor usage. |
| Interfaces.cs | AST interfaces and token/operator enums | Retained the AST contracts; added function-related token kinds. |
| Literals.cs | Literal and variable-reference nodes | Existing separation was sound; retained. |
| Expressions.cs | Arithmetic, comparison, logical and index nodes | Existing nodes were sufficient; retained. Calls are added in Functions.cs. |
| Statements.cs | Declarations, assignments, printing and control flow | Existing structures were sufficient; retained. Function and return nodes are added separately. |
| MaxType.cs | Type descriptors and type-name registry | Retained. Nested list parsing and recursive runtime enforcement repaired in parser/interpreter. |
| MaxExceptions.cs | Language-specific error categories | Retained and used for language failures instead of leaking common host indexing/arithmetic exceptions. |
| Scope.cs | Parent-linked variable environments | Retained parent-linked structure; added function bindings for lexical lookup. |
| Lexer.cs | Source text to tokens | Refactored to track source positions, reject unknown characters, validate number ranges, and support comments, escapes and underscores. |
| Parser.cs | Tokens to syntax tree | Refactored expression precedence, block closure and type parsing; added functions, return and calls. |
| Interpreter.cs | Syntax tree to runtime behavior | Refactored evaluation and helpers; repaired boolean logic, scopes, arithmetic, indexing and type enforcement; added lexical function execution. |

New support files in that revision: Functions.cs defines function AST/runtime objects; GlobalUsings.cs supplies shared imports; Program.cs connects lexer, parser and interpreter into a command-line application; Max.csproj defines a .NET 8/C# 12 executable; Run-Max.ps1 supports compilation with PowerShell's bundled compiler; SelfTests.cs supplies executable regression tests. This pass adds SourceMap.cs for runtime locations.

## Concrete original defects repaired

- A valid `elif ... then` was rejected because its `then` test was inverted.
- Logical `and` tested the left operand twice when checking for false; both logical operators evaluated the right operand even when unnecessary.
- Boolean equality reused conjunction logic, making `false == false` false.
- Parentheses parsed only arithmetic, so grouped comparisons and logic failed.
- Unary `not` consumed an entire expression, producing surprising precedence.
- For and foreach entered two scopes but exited one, leaking scope after each loop.
- While/branch scope cleanup did not use `finally`; runtime failures could leave the interpreter in a local scope. Return makes proper cleanup even more necessary.
- Else did not create the same local scope as the other branches.
- Repeated declarations in for/foreach bodies reused one scope across iterations.
- Several loops accepted end-of-file without the required `conclude`.
- Truncated declarations and loops could dereference a missing token.
- Nested angle-bracket list types consumed the closing bracket in the wrong order.
- Typed list literals were evaluated repeatedly during checking and reconstruction. A function with side effects would run multiple times per element.
- List type checks were shallow for non-literal values and missing for indexed writes.
- Compound variable assignments validated the operand rather than consistently validating the result.
- Resorts could carry an invalid type until a later assignment.
- String arithmetic could concatenate for non-plus operators, and the symmetric concatenation branch checked the wrong operand.
- Separate nil objects compared unequal; integer and float equality disagreed for equal numeric values.
- Indexing leaked host exceptions, and string indexing returned a C# char rather than a Max string.
- Last-element indexing was supported for reads but not writes.
- Integer loop counters could overflow and continue past the intended end.
- Unrecognized source characters were printed and skipped, allowing malformed source to keep executing.

## Deliberate behavior changes to review with Mr General

1. Inner declarations may shadow outer variables. This makes parameters such as `name` or `x` practical and keeps recursion predictable. Redeclaration in the same scope remains an error.
2. Every selected branch and loop iteration gets a local scope, restored on normal completion, error or return.
3. `not` now binds tightly, as documented in the precedence table. Parenthesize comparisons when negating them.
4. Typed lists enforce constraints through aliases, function arguments/results, nested lists, whole-list assignments and indexed writes. Conflicting element constraints on a shared list are rejected.
5. Integer overflow and floating-point division by zero are language errors rather than wrapped values or infinity.
6. Unknown characters are errors. Comments have explicit syntax; informal words such as the chat's standalone `Blow` are not language statements.
7. `function`, `with`, `returns` and `return` are now reserved keywords.
8. `foreach` snapshots the element references at loop entry.
9. Self-containing list assignments are rejected to prevent recursive formatting/type traversal.

That earlier revision preserved string ordering by length and nullable declared types. The current strictness pass changed both; integer division, declaration-time resorts, three-valued logic, and inclusive for-loop endpoints remain.

## Validation

Run `pwsh -File ./Run-Max.ps1 --self-test`. The suite tests real source through lexer, parser and interpreter, including original examples, recursive functions, lexical scope, early returns from every block kind, typing and aliases, missing block terminators, expression precedence, indexing, integer boundaries and diagnostic source locations.

At the earlier revision, **79 tests passed, 0 failed**. The current result is reported above. The installed machine has no .NET SDK, so `dotnet build` itself was not run. No external test packages are required.

## Useful next work, in order

1. Add optional static name and type checks behind `--check`; it currently checks grammar only.
2. Track locations for individual expressions as well as statements so runtime errors can identify the exact operator or call.
3. Add input, numeric conversion and basic list operations with tests and precise failure behavior.
4. Add break/continue, modules, highlighting and a formatter when programs need them.
5. Decide whether the bytecode compiler and Rust VM should become a separate supported route. They need missing definitions, a build manifest, parity tests and completion of unfinished operations.
6. Consider function values and escaping closures; nested named functions work now, but functions cannot yet be passed or returned as values.

This revision deliberately does not claim production readiness or "best language" status. Its concrete contribution is a runnable, documented implementation with functions and regression protection, while keeping the author's design recognizable.

