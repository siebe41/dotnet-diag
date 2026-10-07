# Testing dotnet-diag

You can test whether `dotnet-diag` works in four layers. Each layer answers a harder question than the one before it. Layers 1–3 run automatically in CI on Linux, Windows and macOS. Layer 4 needs people.

| Layer | Question it answers | How to run | Where |
|---|---|---|---|
| 1. Unit tests | Do the parsers, hints, context builder and providers do what they claim? | `dotnet test` | CI (`build` job) |
| 2. Platform & process tests | Does it behave the same on Windows, macOS and Linux, through real pipes and shells? | `dotnet test` (the `Platform/` tests) plus the CI smoke test | CI (`build` job) |
| 3. Usefulness eval | On real broken projects, does it find the cause, and does its suggested fix work? | `dotnet run --project eval/DotnetDiag.Eval` | CI (`eval` job) |
| 4. Field trial | On *our* real failures, does it save developers time? | `dotnet run --project eval/DotnetDiag.Eval -- --review <logs>` | Manual |

---

## 1. Unit tests

```bash
dotnet test
```

These tests run against realistic fixture logs in `tests/DotnetDiag.Tests/Fixtures`: classic console output, terminal-logger output, NuGet restore output, Windows paths, CI logs, and both `dotnet test` formats. A binary log is generated during the test run. They cover:

- **Parser accuracy.** Codes, files, lines and columns, projects, de-duplication, and merging of NuGet dependency-chain lines.
- **Hints.** Package mapping, release-train version alignment, version conflicts, root-cause ranking, and test-failure classification.
- **Context.** The 1000-token budget for every fixture, truncation notes, source snippets, and package references.
- **Providers.** Exact HTTP requests and responses for Anthropic, OpenAI-compatible endpoints and Ollama, using a fake handler. This covers retries, refusals, and error messages.
- **CLI.** Rendering every fixture without Spectre markup errors, exit codes, and the JSON and Markdown formats.

## 2. Platform and process tests

The tests in `tests/DotnetDiag.Tests/Platform/` cover behavior that differs between operating systems. All three CI legs run them:

| Concern | Why it matters | Tests |
|---|---|---|
| File locking | Windows keeps files locked while a handle is open. This caught a real leak in the binlog reader. | `Parsed_binlog_is_not_left_locked`, `Text_log_is_not_left_locked` |
| CRLF line endings | Windows logs, and git `autocrlf` checkouts | `Crlf_logs_parse_exactly_like_lf_logs` (every fixture) |
| Encodings | Windows PowerShell 5.1's `>` writes UTF-16. `pwsh` writes UTF-8 with a BOM. Older tools write ANSI code pages. | `Files_in_any_common_encoding_parse_the_same`, `Ansi_code_page_logs_fall_back_without_losing_ascii` |
| Redirected stdin | On Windows, `Console.In` would decode piped UTF-8 with the OEM code page | `Piped_crlf_utf8_test_output_is_analyzed`, `Piped_utf16_output_from_windows_powershell_is_analyzed` |
| Localized SDKs | Localized compilers quote names as `"x"`, `« x »` or `„x“` | `Localized_compiler_messages_still_map_to_packages` |
| Path mapping | Logs from a Windows build agent (`D:\a\...`) analyzed on a Mac, and mixed separators | `Windows_ci_paths_map_onto_the_local_checkout_on_every_os`, `Terminal_logger_relative_windows_paths_find_their_project` |
| Case sensitivity | Windows and macOS file systems are case-insensitive; Linux is case-sensitive | `Path_casing_follows_the_file_system` (adapts to the file system it runs on) |
| Symlinks and short paths | macOS `/var` is really `/private/var`; Windows uses 8.3 temp paths and junctions | `Checkout_reached_through_a_symlink_is_still_the_workspace` |
| Git | Worktrees, paths with spaces, branch detection, and uncommitted-file detection with a real `git` | `Worktree_style_git_file_reports_its_branch`, `Real_git_repository_reports_branch_and_uncommitted_files` |
| The real executable | Pipes, exit codes, UTF-8 output and console width when the process is launched by a shell | `ProcessEndToEndTests` |

The CI **smoke test** then installs the packed `.nupkg` with `dotnet tool install`, exactly as a user would. On Windows that means running through the `dotnet-diag.exe` shim. It checks the following:

- `--version` and `explain` work.
- `--exit-code` returns 1 when problems are found.
- Piped input is analyzed.
- The binary log produced by that same CI build parses on that OS.

## 3. Usefulness eval

```bash
dotnet run --project eval/DotnetDiag.Eval                      # offline heuristics
dotnet run --project eval/DotnetDiag.Eval -- --filter package  # a subset
dotnet run --project eval/DotnetDiag.Eval -- --keep            # keep the broken projects to inspect them
dotnet run --project eval/DotnetDiag.Eval -- --report artifacts/eval/report.md
```

Each folder in `eval/scenarios/` contains a small project that is **broken on purpose**, plus a `scenario.json` describing what a useful diagnosis must contain. For every scenario, the harness:

1. Copies the project to a temp folder and runs `git init` there, so source context and the git features work.
2. Runs the real `dotnet build -bl` or `dotnet test` with the installed SDK, on whatever OS it is running on.
3. Diagnoses **both** the binary log and the console output, then scores the result:
   - **detected:** the expected codes, exception types or failing-test counts were found.
   - **root cause first:** the first non-symptom hint is the real cause.
   - **packages:** exactly the right packages were suggested. An empty list means the tool must not invent any.
   - **actionable advice:** the suggestions contain the concrete fix (a command, a `using`, a file and line).
   - **context budget:** the LLM bundle fits the token budget.
4. If `"fix": "auto"`, **runs the tool's own `dotnet add …` suggestions and rebuilds.** This step checks the advice itself: does following it actually fix the build?

Current scenarios:

| Scenario | Failure | Fix verified automatically |
|---|---|---|
| `missing-package` | `CS0246`: Newtonsoft.Json not referenced | ✔ |
| `missing-extension-package` | `CS1061`: `AddHttpClient` without Microsoft.Extensions.Http | ✔ (needs version alignment, see below) |
| `missing-using-linq` | `CS1061`: `.Where` without `using System.Linq` | – |
| `package-downgrade` | `NU1605` downgrade | ✔ |
| `unknown-package` | `NU1101`: package not in any feed | – |
| `syntax-error` | `CS1002` | – |
| `unsupported-tfm` | `NETSDK1045` | – |
| `failing-tests` | Assertion failure plus a `NullReferenceException` in production code | – |
| `missing-project-reference` | 🟡 known gap: suggests no `dotnet add reference` | ✔ once fixed |
| `global-json-sdk-missing` | 🟡 known gap: the host error isn't recognized | – |

**Known gaps** are scenarios we want to pass but that don't yet. They are reported, but they don't fail CI, and the harness says when one starts passing. Use `--strict` to make them fail the run.

The eval has already found bugs that unit tests missed:

- The tool suggested `dotnet add package Microsoft.Extensions.Http` at the latest version next to a 9.0.0 `Microsoft.Extensions.*` reference. Following that advice produced `NU1605`. Suggestions for Microsoft.Extensions.*, EF Core and ASP.NET Core packages now match the release-train version the project already uses.
- A scenario I expected to produce `CS1002` produced `CS1003`. The tool was right and my expectation was wrong.

### With an AI provider

```bash
dotnet run --project eval/DotnetDiag.Eval -- --provider ollama --model qwen2.5-coder:14b --report artifacts/eval/ollama.md
ANTHROPIC_API_KEY=... dotnet run --project eval/DotnetDiag.Eval -- --provider anthropic --report artifacts/eval/claude.md
dotnet run --project eval/DotnetDiag.Eval -- --provider openai --endpoint http://localhost:1234/v1 --model <id>
```

Each AI answer is checked for the scenario's `aiKeywords`, which are the facts a correct answer has to mention. The answers are saved in the report for human review. Comparing scorecards across models (and against `off`) shows whether a model adds anything beyond the offline heuristics.

The AI check is a keyword check, not a quality judgment. Read the answers in the report before trusting a high score.

### Adding a scenario

1. Create `eval/scenarios/<name>/` containing the smallest project that reproduces the failure.
2. Add `scenario.json`:

   ```jsonc
   {
     "description": "What is broken and why",
     "command": "build",                     // or "test"
     "target": "src/App/App.csproj",          // relative to the scenario folder
     "inputs": ["binlog", "text"],            // optional; "text" only for dotnet test or host errors
     "expect": {
       "codes": ["CS0246"],
       "topHint": "CS0246",
       "missingPackages": ["Newtonsoft.Json"], // exact set; [] = must not suggest any
       "versionConflicts": [],
       "testFailures": 2,                       // for "test" scenarios
       "hintCodes": ["NullReferenceException"],
       "suggestionContains": ["dotnet add src/App/App.csproj package Newtonsoft.Json"],
       "aiKeywords": ["Newtonsoft.Json"]
     },
     "fix": "auto",                             // apply the tool's dotnet add commands and rebuild
     "knownGap": "optional: why this does not pass yet"
   }
   ```

3. Run `dotnet run --project eval/DotnetDiag.Eval -- --filter <name> --keep` and check that it fails for the reason you intended.

**Rule of thumb:** whenever dotnet-diag gets a real failure wrong, turn that failure into a scenario before you fix it. Use a fixture log in `tests/DotnetDiag.Tests/Fixtures` when the failure can't be reproduced as a small project.

## 4. Field trial on real failures

The scenarios are failures we wrote ourselves, so they can only show that the tool *can* be useful. Whether it *is* useful depends on how it does on your team's real failures:

1. **Collect** 30–50 recent failing builds and test runs from CI history. Use binary logs where possible (`dotnet build -bl`, or the CI artifacts), otherwise the console logs. Include the boring ones; they are most of the volume.
2. **Generate the review sheet**, offline and optionally with each model you are considering:

   ```bash
   dotnet run --project eval/DotnetDiag.Eval -- --review ./field-logs --repo ~/src/our-repo --report artifacts/eval/field-offline.md
   dotnet run --project eval/DotnetDiag.Eval -- --review ./field-logs --repo ~/src/our-repo --provider anthropic --report artifacts/eval/field-claude.md
   ```

   This writes a Markdown table and a CSV. Each row has the top hint and the suggested fix, plus empty grading columns.
3. **Grade blind.** For each log, the developer who fixed the original failure fills in:
   - **Root cause?** `Y`, `P` (partial) or `N`
   - **Fix worked?** `Y`, `N` or `-` (none suggested)
   - **Minutes saved**, compared with reading the raw log (negative if the tool misled them)
4. **Decide** using the targets below, and turn every `N` into a scenario or fixture (layer 3).

## Targets

These are proposed bars for calling the tool "useful". The eval prints the first five metrics as a scorecard.

| Metric | Source | Target |
|---|---|---|
| Detection | Eval | ≥ 95% |
| Root cause ranked first | Eval | ≥ 90% |
| Correct package advice, none invented | Eval | 100% (a wrong package wastes more time than none) |
| Suggested fix actually works | Eval, `fix: auto` scenarios | ≥ 80% |
| AI answer on target | Eval with `--provider` | ≥ 80%, and above the offline baseline |
| Root cause correct (`Y`, or `P` counted as half) | Field trial | ≥ 70% |
| Median minutes saved per failure | Field trial | > 0, with no `N` that cost more than 10 minutes |

Current offline baseline on Linux (all non-gap scenarios pass): detection 94%, root cause 100%, packages 100%, actionable advice 83%, fix works 3/4 (the miss is the known project-reference gap).
