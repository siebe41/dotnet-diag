# Contributing

Bug reports, diagnosis reports, fixes and documentation changes are all welcome as issues and pull requests. For a larger change, open an issue first so the approach can be agreed before you write it.

## Reporting diagnosis quality

You do not need to write code to help. If dotnet-diag ran but pointed at the wrong cause, suggested a fix that did not work, missed the real cause or flagged a symptom, open a **Missed the root cause** issue. The form follows the field-trial grading in [docs/TESTING.md](docs/TESTING.md#4-field-trial-on-real-failures): what the tool said (`--format markdown`, and the `--show-context` bundle if a provider was involved), what the real cause and fix turned out to be, and how much time it saved or cost compared with reading the raw log. Each miss can become an eval scenario or a test fixture.

Logs from private code can contain source lines, internal paths, feed URLs and environment variables. Do not attach a binlog from a private build; paste trimmed output, or better, a minimal project that reproduces the failure.

## Build and test

You need the .NET SDK from [`global.json`](global.json) (9.0 or later).

```bash
dotnet build
dotnet test
dotnet run --project src/DotnetDiag.Cli -- analyze tests/DotnetDiag.Tests/Fixtures/nuget-restore.txt
dotnet run --project src/DotnetDiag.Gui        # the desktop app
dotnet run --project eval/DotnetDiag.Eval      # usefulness eval: break real projects, diagnose, verify fixes
```

`dotnet test` needs no model or API key. The eval builds real projects with your SDK. [docs/TESTING.md](docs/TESTING.md) describes what each layer covers.

## Pull requests

CI runs the build, the tests, a smoke test of the packed tool and the eval on Linux, Windows and macOS, and scans the git history for secrets. A change has to pass on all three operating systems. If you change behavior that differs between them (paths, line endings, encodings, shells), add a test under `tests/DotnetDiag.Tests/Platform/`.

## What not to commit

- **API keys.** Use environment variables (`DOTNET_DIAG_API_KEY`, `ANTHROPIC_API_KEY`, `OPENAI_API_KEY`) or your own config file outside the repository. Tests use a fake HTTP handler and never need a real key. A key that was committed has to be revoked at the provider: removing it in a later commit leaves it in the history.
- **Binary logs from your own machine.** A `.binlog` records the environment variables of the build, which can include keys and tokens, as well as local paths and feed URLs. `*.binlog` is ignored for this reason. The only committed binlogs are the ones in `samples/`, regenerated from the intentionally broken projects in `eval/scenarios/` as described in [samples/README.md](samples/README.md):

  ```bash
  dotnet run --project eval/DotnetDiag.Eval -- --generate-samples samples
  ```

  Text logs pasted into tests or issues can contain the same things; trim them to what the test needs.

## Roles and how to become a collaborator

Everyone starts at the first step. Moving up is by invitation from the owner, based on merged work and helpful reports over time; it is not something to request. Each step adds permissions; none of them removes the owner's review of changes to `main`.

| Role | What you can do | How you get it |
|---|---|---|
| **Tester** | Use the tool and report bugs, missed root causes and ideas through the issue forms. | Nothing needed: no access or invitation. |
| **Contributor** | Fork the repository and open pull requests. CI on a pull request from a fork may wait for the owner's approval before it runs (GitHub asks for this for first-time contributors). A pull request merges after a review from a code owner (see [`.github/CODEOWNERS`](.github/CODEOWNERS)) and passing checks. | Nothing needed beyond a GitHub account. |
| **Triage** | Label, close, reopen and assign issues and pull requests, and mark duplicates. No push access to the code. | Invitation, usually after several good reports or merged pull requests. |
| **Write** | Push branches to this repository and review pull requests. You still cannot merge to `main` on your own: the branch ruleset and CODEOWNERS require the owner's review. Someone with sustained work in one area may be added to CODEOWNERS for that area, so their review is requested there too. | Rarely, by invitation, after sustained merged work. |
| **Maintain / Admin** | Repository settings, rulesets, releases and secrets. | Stays with the owner. |

There is no contributor licence agreement and no DCO sign-off (`Signed-off-by`) is required. The project is under the [MIT licence](LICENSE), and under GitHub's terms of service a contribution is made under the same licence as the project (inbound = outbound).

## Security issues

Do not report security problems in a public issue or pull request. See [SECURITY.md](SECURITY.md).
