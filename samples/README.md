# Sample error logs

These are real failures, produced by building the intentionally broken projects in
[`eval/scenarios/`](../eval/scenarios) with `dotnet build -bl` (or `dotnet test`). Each sample has:

- `<name>.binlog`: the MSBuild binary log, which is the richest input
- `<name>.txt`: the console output exactly as a developer saw it

Pass `--repo eval/scenarios/<name>` so dotnet-diag can map the log's paths back to the source and show
the offending lines.

**Run all of them** (builds once, then analyzes every sample):

```powershell
./samples/run-samples.ps1                     # PowerShell (Windows PowerShell 5.1 or pwsh)
./samples/run-samples.ps1 -Filter package     # only some
./samples/run-samples.ps1 -Installed          # use the installed dotnet-diag tool
# if script execution is disabled on this machine:
powershell -ExecutionPolicy Bypass -File samples/run-samples.ps1
```

```bash
./samples/run-samples.sh                      # bash / zsh
FILTER=package ./samples/run-samples.sh
```

**Run one** (from the repository root):

```powershell
dotnet run --project src/DotnetDiag.Cli -- analyze samples/missing-package.binlog --repo eval/scenarios/missing-package
# as if piped from dotnet test (PowerShell has no '<' redirection):
Get-Content samples/failing-tests.txt | dotnet run --project src/DotnetDiag.Cli -- analyze --repo eval/scenarios/failing-tests
```

```bash
dotnet run --project src/DotnetDiag.Cli -- analyze samples/missing-package.binlog --repo eval/scenarios/missing-package
dotnet run --project src/DotnetDiag.Cli -- analyze --repo eval/scenarios/failing-tests < samples/failing-tests.txt
```

Regenerate them with your own SDK and OS (the logs then contain your temp-folder path):

```bash
dotnet run --project eval/DotnetDiag.Eval -- --generate-samples samples
```

| Sample | What is broken | Recognized | Try |
|---|---|---|---|
| [`failing-tests.txt`](failing-tests.txt) | Two failing tests: integer division bug (assertion) and a NullReferenceException in production code. | 2 failing tests | `dotnet-diag analyze samples/failing-tests.txt --repo eval/scenarios/failing-tests` |
| [`global-json-sdk-missing.txt`](global-json-sdk-missing.txt) | global.json pins an SDK that is not installed; the dotnet host fails before MSBuild runs. 🟡 *known gap* | – | `dotnet-diag analyze samples/global-json-sdk-missing.txt --repo eval/scenarios/global-json-sdk-missing` |
| [`missing-extension-package.binlog`](missing-extension-package.binlog), [`missing-extension-package.txt`](missing-extension-package.txt) | AddHttpClient() is an extension method from Microsoft.Extensions.Http, which is not referenced. | CS1061 | `dotnet-diag analyze samples/missing-extension-package.binlog --repo eval/scenarios/missing-extension-package` |
| [`missing-package.binlog`](missing-package.binlog), [`missing-package.txt`](missing-package.txt) | Code uses Newtonsoft.Json but the project never referenced the package. | CS0246 | `dotnet-diag analyze samples/missing-package.binlog --repo eval/scenarios/missing-package` |
| [`missing-project-reference.binlog`](missing-project-reference.binlog), [`missing-project-reference.txt`](missing-project-reference.txt) | App uses a sibling project's namespace without a ProjectReference. 🟡 *known gap* | CS0246 | `dotnet-diag analyze samples/missing-project-reference.binlog --repo eval/scenarios/missing-project-reference` |
| [`missing-using-linq.binlog`](missing-using-linq.binlog), [`missing-using-linq.txt`](missing-using-linq.txt) | ImplicitUsings is off and System.Linq is not imported, so .Where() is not found. | CS1061 | `dotnet-diag analyze samples/missing-using-linq.binlog --repo eval/scenarios/missing-using-linq` |
| [`package-downgrade.binlog`](package-downgrade.binlog), [`package-downgrade.txt`](package-downgrade.txt) | A direct reference pins Microsoft.Extensions.Logging.Abstractions 8.0.0 below what Microsoft.Extensions.Http 9.0.0 needs (NU1605). | NU1605 | `dotnet-diag analyze samples/package-downgrade.binlog --repo eval/scenarios/package-downgrade` |
| [`syntax-error.binlog`](syntax-error.binlog), [`syntax-error.txt`](syntax-error.txt) | Missing semicolon. | CS1002 | `dotnet-diag analyze samples/syntax-error.binlog --repo eval/scenarios/syntax-error` |
| [`unknown-package.binlog`](unknown-package.binlog), [`unknown-package.txt`](unknown-package.txt) | PackageReference to a package that exists in no configured feed (typo or missing private feed). | NU1101 | `dotnet-diag analyze samples/unknown-package.binlog --repo eval/scenarios/unknown-package` |
| [`unsupported-tfm.binlog`](unsupported-tfm.binlog), [`unsupported-tfm.txt`](unsupported-tfm.txt) | The project targets a .NET version the installed SDK cannot build. | NETSDK1045 | `dotnet-diag analyze samples/unsupported-tfm.binlog --repo eval/scenarios/unsupported-tfm` |
