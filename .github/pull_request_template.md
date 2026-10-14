## What and why

<!-- What this changes and why. For a behaviour change, what it did before and what it does now. -->

Closes #

## Checklist

- [ ] `dotnet build` passes (warnings are errors)
- [ ] `dotnet test` passes, with tests added or updated for changed behaviour
- [ ] If diagnosis logic changed (parsers, hints, context): `dotnet run --project eval/DotnetDiag.Eval` passes outside the known gaps
- [ ] README or `docs/` updated if commands, options or behaviour changed
- [ ] No API keys or tokens in the code, tests, samples or commit history
- [ ] No binlogs or logs from a real machine; new samples are regenerated from `eval/scenarios` as described in `samples/README.md`
