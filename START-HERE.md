# Start writing Max

Download **Max-0.4.0-win-x64.zip** from the [latest public release](https://github.com/Flashzain97/Max-AI/releases/latest), then extract the whole ZIP. The Windows x64 package includes `max.exe` and its runtime, so no .NET installation is needed to run it.

The [source repository](https://github.com/Flashzain97/Max-AI) and `Max-0.4.0-source.zip` contain the complete language source, examples and documentation. They do not include the executable or SDK. To build a Windows source checkout, open a terminal in the extracted source folder and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Setup-Max.ps1
```

Setup downloads and verifies the pinned SDK locally, builds Max, runs its tests and creates `max.exe`. PowerShell 7.4+ users can instead run `pwsh -NoProfile -File .\Run-Max.ps1 --studio` directly, without an SDK.

Once the Windows release is extracted or setup has finished:

1. Open this folder in File Explorer.
2. Double-click `Start-Studio.cmd` to open Max Studio in your browser.
3. Keep the terminal window open. Leave **Strict** enabled, edit the code and click **Run**.
4. Click **Check** when you want to check the code without running it.
5. Save a draft in the browser, or download a `.max` file to keep your program.

If the browser does not open, visit the address printed in the terminal, normally `http://127.0.0.1:7337/`. Press **Ctrl+C** in that terminal to stop Studio. No extra packages are needed.

For an `input(...)` program, put its answers in the input box before running, **one answer per line**. For a name and age, enter:

```text
Alex
14
```

Studio stops runs after 10 seconds. Code, input and each output stream are limited to about 65,000 characters. You can run larger programs from a terminal.

To try the included score calculator directly, double-click `Start-Max.cmd`. You can also open `first.max` in Notepad, then open a terminal in this folder and run:

```powershell
.\max.exe --strict .\first.max
```

The included program prints:

```text
Player: Alex
Rounds: 4
Total points: 45
You reached the target!
```

Change the player name or the scores, save the file, and run it again.

Max's words have simple jobs:

| Write | Meaning |
| --- | --- |
| `declare score as int = 10` | Create a whole-number variable. |
| `score += 5` | Add five to its current value. |
| `say score` | Print the value. |
| `text(score)` | Explicitly turn a value into text. |
| `if score >= 10 then` | Start a condition. |
| `foreach score in scores do` | Visit each score in a list. |
| `function ... returns int do` | Define a function that produces an integer. |
| `function ... returns nothing do` | Define a function that performs an action. |
| `conclude` | End a block. |

Use written types with `--strict`. A wrong type is an error; Max does not silently turn strings into numbers. Add `?` to a value type only when `nil` should be allowed.

Check a program without running it:

```powershell
.\max.exe --check --strict .\first.max
```

Try keyboard input:

```powershell
.\max.exe --strict .\examples\input.max
```

Enter a name and a whole-number age. Read [README.md](README.md) for all language rules and [REVIEW.md](REVIEW.md) for implementation and validation details.

After changing Max's C# implementation, run `powershell -NoProfile -ExecutionPolicy Bypass -File .\Build-Max.ps1` to rebuild the executable. Editing a `.max` program needs no rebuild.
