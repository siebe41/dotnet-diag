# Contributing

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

## Security issues

Do not report security problems in a public issue or pull request. See [SECURITY.md](SECURITY.md).
