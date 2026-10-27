# Max language guide

Max is a small programming language built around readable words such as `declare`, `say`, `if`, `do`, and `conclude`. This folder contains Max 0.4.0, a C# interpreter with checks before execution and strict runtime type rules. Use `--strict` to require written types throughout a program.

## Get Max

The public source repository is [Flashzain97/Max-AI](https://github.com/Flashzain97/Max-AI).

For Windows x64, download **Max-0.4.0-win-x64.zip** from the [latest release](https://github.com/Flashzain97/Max-AI/releases/latest), extract the whole ZIP, then double-click `Start-Studio.cmd`. This package includes `max.exe`, its runtime, the source, documentation and examples. You do not need to install .NET to run it.

The repository and **Max-0.4.0-source.zip** contain source code only. The SDK and executable are not included in the source package. To prepare a Windows x64 source checkout, open a terminal in its extracted folder and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Setup-Max.ps1
```

Setup downloads the pinned Microsoft SDK, verifies its SHA512 checksum, builds Max, runs the regression suite and creates `max.exe`. It installs build tools only inside this folder. Alternatively, PowerShell 7.4+ can run the source directly without an SDK:

```powershell
pwsh -NoProfile -File .\Run-Max.ps1 --studio
pwsh -NoProfile -File .\Run-Max.ps1 --strict .\first.max
```

## Write and run in Max Studio

After extracting the Windows release or building the source, double-click `Start-Studio.cmd`. Max opens an editor in your browser, served by `max.exe` on your computer. Keep its terminal window open while you use the editor; press **Ctrl+C** in that window to stop it. No extra packages or account are needed.

Strict checking starts enabled. Use **Check** to check your code without running it, and **Run** to show its output. **Open .max** loads an existing program; **Save .max** downloads your code. Studio also saves a draft in your browser when storage is available. Loading an example or file keeps the previous draft under **Restore previous draft**. Click an error's line link to select its location in your code.

For programs using `input(...)`, enter the answers in Studio's input box before running, with one answer per line. A program that asks for a name and an age needs two lines, such as:

```text
Alex
14
```

Studio normally opens at `http://127.0.0.1:7337/`. You can also start it from a terminal:

```powershell
.\max.exe --studio
.\max.exe --studio --port 0 --no-browser
```

`--port` accepts 0 through 65535. Port 0 chooses a free port; open the address printed in the terminal. `--no-browser` prints the address without opening a browser automatically.

Studio stops a program after 10 seconds so an endless loop cannot keep running there. Code and supplied input may each contain up to 65,536 UTF-16 characters; printed output and error text have the same limit per stream. Closing Studio stops its runners too. Larger programs or output can be run from a terminal. These editor limits do not change Max's language rules.

## Run a program

The Windows release and a completed setup/build provide `max.exe` with its runtime. Open a terminal in that folder and run:

```powershell
.\max.exe --strict .\first.max
.\max.exe --check --strict .\examples\strict.max
.\max.exe --strict .\examples\input.max
.\max.exe --self-test
```

Double-click `Start-Max.cmd` for the first program, with the window kept open afterward. `max.cmd` is an alternative launcher for terminal use. Save your own code as a plain-text `.max` file. You can edit it in Notepad; choose **All files** when saving so it does not become `.max.txt`. Put quotation marks around paths containing spaces.

`--check` checks grammar, names and known types without printing program output or reading input. Ordinary execution does these checks first too. `--strict` additionally requires a written type on declarations, parameters and function results, and checks unused function bodies. Loop variables get their type from the loop. Strict list types must name an element type, such as `list<int>`.

These are conservative checks, not a proof that a program cannot fail. Direct guards such as `if value != nil then` refine an optional value inside that branch. Known conflicting constraints on a typed shared list are rejected before execution. Dynamic values, changing list contents, input, bounds, arithmetic overflow and uncertain control flow still require runtime checks. Use `--syntax-only` to check grammar alone. An error exits with code 1; success exits with code 0.

For source development, `Setup-Max.ps1` installs Microsoft .NET SDK 10.0.401 locally in `.tools/dotnet`; the SDK is not bundled with this release or source checkout. Run `powershell -NoProfile -ExecutionPolicy Bypass -File .\Build-Max.ps1` after changing C# source; it builds, tests and updates `max.exe`. `Setup-Max.ps1` can download that SDK from Microsoft, verify its SHA512 checksum and rebuild on another Windows x64 machine. It needs no administrator access and changes no system-wide PATH. Keep the downloaded SDK local; the standalone executable runs without it. Microsoft's [Windows installation guide](https://learn.microsoft.com/en-us/dotnet/core/install/windows) and [single-file deployment guide](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview) explain these options.

PowerShell 7.4+ is a second way to run the current C# source directly, without an SDK:

```powershell
pwsh -NoProfile -File .\Run-Max.ps1 --strict .\first.max
pwsh -NoProfile -File .\Run-Max.ps1 --self-test
```

Windows PowerShell 5.1 can run setup/build scripts, but cannot run `Run-Max.ps1`. With an installed .NET 10 SDK you can also use `dotnet run --project Max.csproj -- --strict first.max`.

## Your first program

Save this as `hello.max` and run `.\max.exe --strict .\hello.max`:

```text
declare name as string = "Alex"
declare visits as int = 2
say "Hello, " + name
say "Visits: " + text(visits)
```

Output:

```text
Hello, Alex
Visits: 2
```

`declare` creates a variable. `as string` and `as int` state which values it may hold. `say` prints one value and a newline. `text(visits)` explicitly turns a number into text; `+` joins two strings or adds two numbers, but does not silently convert between them.

Write one statement per line and indent the code inside blocks. Newlines are whitespace to the parser, so indentation is for readers; there are no semicolons. Names are case-sensitive; they start with a Unicode letter or `_` and may then contain Unicode letters, digits, or `_`. Number literals use the digits `0` through `9`. `#` and `//` start comments that run to the end of the line.

## Values, types, and `nil`

| Type | Example | Accepts |
| --- | --- | --- |
| `int` | `declare age as int = 14` | Whole numbers from -2,147,483,648 to 2,147,483,647. |
| `float` | `declare price as float = 2.5` | Numbers with or without a decimal point. |
| `string` | `declare name as string = "Max"` | Text in double quotes. |
| `boolean` | `declare ready as boolean = true` | `true`, `false`, or `possible`. |
| `list<int>` | `declare scores as list<int> = [4, 8]` | A list of integers. `list of int` is also valid. |
| `int?` | `declare score as int? = nil` | An integer or `nil`. |
| `list<int?>` | `declare scores as list<int?> = [4, nil]` | A list whose elements can be integers or `nil`. |
| `list<int>?` | `declare scores as list<int>? = nil` | An entire integer list or `nil`. |
| `nothing` | `function greet() returns nothing do say "Hello" conclude` | Only `nil`; useful for functions that perform an action without producing a result. |

`nil` means “no value.” Value types without `?` reject `nil`, including inside lists and function results. The special `nothing` type accepts only `nil`. Use `?` exactly where a normal value type also allows `nil`. For example, `list<int?>` and `list<int>?` make different promises. In ordinary mode, a variable without `as` is dynamic and can hold any value, including `nil`:

```text
declare anything = nil
anything = "now text"
```

In ordinary mode, an untyped `list` may mix element types: `declare mixed as list = [1, "two"]`. Strict mode requires an element type.

Assignment changes an existing variable. A duplicate declaration in the same scope is an error. `+=`, `-=`, `*=`, and `/=` calculate and assign; the result must still fit the variable's type. A compound assignment reads its original value before evaluating the expression on the right. If that expression changes the same variable or list, the calculation still uses the saved value; an indexed write also checks that its selected index still exists.

A **resort** is a fallback captured when the variable is declared. Assigning `nil` then uses that fallback:

```text
declare lives as int = 3, resort 1
lives = nil
say lives
```

This prints `1`. A resort cannot itself be `nil` and must have the declared type. A resort also works for a dynamic variable. An optional expression may supply `nil` and use the resort, even when that expression's non-`nil` type is different; a non-`nil` value must still fit the declared type.

Use a nil guard when working with an optional value:

```text
declare score as int? = nil
if score != nil then
    say score + 1
else
    say "No score yet"
conclude
```

The checker knows `score` is an integer in the first branch and `nil` in the second. A write or a function call can change a captured value, so the checker discards those temporary facts after either operation. Runtime checks still enforce the actual value.

Strings can contain `\n` (newline), `\r`, `\t`, `\"`, and `\\`. A string cannot cross source lines directly; use `\n` when needed.

## Conditions and loops

```text
if score >= 10 then
    say "High"
elif score >= 5 then
    say "Middle"
else
    say "Low"
conclude
```

`elif` means “otherwise, if.” The first true branch runs. `conclude` closes the whole conditional. A condition must be `true`, `false`, or `possible`; Max does not treat numbers or strings as booleans.

`possible` means the truth is unknown. If a condition is `possible`, Max runs the optional `maybe` branch and stops checking later conditions. Branches go in this order: `if`, any `elif` branches, `maybe`, `else`, `conclude`.

```text
if possible then
    say "Yes"
maybe
    say "Unknown"
else
    say "No"
conclude
```

`and`, `or`, and `not` work with all three boolean states. `false and ...` and `true or ...` skip the right side. `not possible` remains `possible`; comparing `possible` with another boolean also produces `possible`.

```text
declare count as int = 0
while count < 3 do
    say count
    count += 1
conclude

for i = 5 to 1, step -2 do
    say i
conclude

foreach item in ["a", "b"] do
    say item
conclude
```

`while` checks its condition before each turn; `possible` stops it. `for` uses an inclusive end when reached and a default step of 1. A descending `for` needs a negative step. `foreach` visits a snapshot of the list's elements. Each selected branch and loop turn has its own local scope, so variables declared inside disappear afterward. Assigning an existing outer variable updates that variable.

## Functions

```text
function add with a as int, b as int returns int do
    return a + b
conclude

say add(2, 3)
```

This prints `5`. `with` introduces parameters, `returns` states the result type, `return` leaves the function, and a call always uses parentheses. `function add(a as int, b as int) returns int do` is an equivalent declaration. Ordinary mode permits omitted parameter and result types; strict mode requires them.

Functions must be defined before they are called. They can call themselves, and a nested function sees variables surrounding its definition. A function without `return` returns `nil`. In strict mode, a non-optional result such as `returns int` requires a return on every reachable path; ordinary mode checks the actual result at runtime. Declare `returns int?` when `nil` is valid. Strict checking uses the whole declared contract: a `boolean` parameter allows `possible`, so a conditional may need a `maybe` return or a final return. The maximum call depth is 128, with a combined evaluation-nesting limit of 512; deeply nested expressions can reach the second limit sooner.

For an action with no result, write:

```text
function greet with name as string returns nothing do
    say "Hello, " + name
conclude
greet("Alex")
```

Built-in functions are available:

| Function | Meaning |
| --- | --- |
| `text(value)` | Convert one value to its printed text form. |
| `length(value)` | Count list elements or Unicode code points in a string. |
| `input(prompt)` | Print a string prompt and read one line of text. End-of-input is an error. |
| `integer(value)` | Explicitly convert an integer, a whole float, or an integer string to a 32-bit integer. Fractional values and overflow are errors. |
| `decimal(value)` | Explicitly convert a number or numeric string to a finite float. |
| `append(items, value)` | Add one element to a list, enforcing its element rule and rejecting cycles. Returns `nil`. |
| `remove(items, index)` | Remove and return one list element. Index `-1` means the last element. |

These names are reserved for the built-ins, including variable and parameter names. A variable and a function cannot share a visible name. For example, use `"Score: " + text(score)` to join a number with a string.

Conversions accept optional signs and use `.` as the decimal separator. They reject surrounding whitespace, hidden NUL characters, thousands separators, booleans, `nil`, infinity and NaN. `integer("14")` succeeds; `integer("14.5")` fails. `decimal("1.25e2")` gives `125`. These conversions stay explicit so errors do not silently change a value's meaning.

## Lists and expressions

Lists start at index `0`. `items[-1]` reads or changes the last element; other negative indexes are errors. Strings can also be indexed, and indexing returns a one-character string. `length("A😀")` is `2`, and `"A😀"[-1]` is `"😀"`.

```text
declare scores as list<int> = [10, 20]
scores[0] += 5
say scores[-1]
```

Lists are shared objects. `declare other = scores` creates another name for the same list. Binding an untyped list to a typed variable, parameter, or return value attaches that element rule to the shared list; aliases must follow it too. Two conflicting element rules on the same list are an error. For example, a `list<int>` cannot be rebound as a `list<float>` or `list<int?>`, even though individual integers are accepted by those element types: all names for that mutable list must preserve the same rule. A fresh literal such as `[1, 2]` can be declared as `list<float>`. Lists cannot directly or indirectly contain themselves.

Operators, from strongest to weakest:

1. Calls and indexing: `add(1, 2)`, `items[0]`.
2. Unary `-`, `not`, `!`.
3. `*`, `/`.
4. `+`, `-`.
5. Comparisons: `==`, `!=`, `<`, `<=`, `>`, `>=`.
6. `and`.
7. `or`.

Parentheses group expressions. Write `a < b and b < c`; a chain such as `a < b < c` is rejected. Equality allows two numbers (`int` and `float` can mix), two booleans, two strings, or two lists; lists compare by identity. You can compare `nil` with any value to test whether a value is missing. Comparing unrelated non-`nil` types is an error. Ordering accepts two numbers or two strings; strings use case-sensitive Unicode ordinal order.

Integer division truncates (`7 / 2` gives `3`); a float operand gives float division (`7 / 2.0` gives `3.5`). Division by zero and arithmetic overflow are errors.

## When something goes wrong

Max reports a file, line, column, source line and caret. Runtime errors point to the innermost failing expression, including an operator or call, and show the function call chain. For example:

```text
example.max, line 2, column 15: Arithmetic error: Cannot divide by zero.
      return 10 / n
                ^
Call stack: divide
```

Read the first line for the immediate problem and the call stack for how execution reached it. `--check` catches definite problems before execution; runtime checks enforce assignments, arguments, results and list writes using actual values. Very deeply nested source is rejected with a language error to protect the parser from exhausting the host's stack.

Run [first.max](first.max), [examples/strict.max](examples/strict.max), [examples/tour.max](examples/tour.max), [examples/functions.max](examples/functions.max), [examples/guessing.max](examples/guessing.max), and [examples/input.max](examples/input.max) for complete programs. [REVIEW.md](REVIEW.md) records implementation decisions and unfinished work. Modules, function values and a formatter remain future work. The separate `Compiler.cs` and Rust VM files are incomplete experiments; the C# interpreter is the supported implementation.
