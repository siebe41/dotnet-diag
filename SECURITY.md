# Security policy

## Supported versions

Security fixes are made on `main` and go out in the next release. Only the latest commit on `main` and the latest release are supported; there are no backports to older versions.

## Reporting a vulnerability

Report it privately through GitHub: on this repository's **Security** tab, choose **Report a vulnerability** ([private vulnerability reporting](https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability)). Do not open a public issue, pull request or discussion for a security problem.

Include what you ran (command line or desktop app, version or commit, OS), what happened, and what you expected. If the problem needs a log to reproduce, make a minimal one rather than attaching a log from a real machine: binary logs record environment variables, and build output can contain paths, feed URLs and tokens.

The project is maintained on a best-effort basis. Reports are read and answered as time allows; there is no guaranteed response or fix time. When a fix is released, a security advisory describing it is published on the Security tab.

## Scope

`dotnet-diag` reads build and test logs and can send a summary of them to an AI provider. These are the areas where a problem is a security issue:

- **What is sent to a provider.** Only the token-budgeted diagnostic bundle is sent: the summary line, de-duplicated errors, failing tests with user-code stack frames, a few source lines around the top locations, package references and warning counts. `--show-context` on the command line and the **Model context** tab in the desktop app show exactly what is, or would be, sent. Anything sent that is not in that bundle, or that differs from what `--show-context` shows, is in scope.
- **No provider unless selected.** A provider is used only when one is selected with `--provider`, `DOTNET_DIAG_PROVIDER`, the config file or the desktop app's settings. Having `ANTHROPIC_API_KEY` or `OPENAI_API_KEY` set must not, on its own, send anything. `--offline` must send nothing. Any network request outside these rules is in scope.
- **API keys.** Keys are read from flags, environment variables or the config file and sent only to the selected provider's endpoint. A key saved under the desktop app's **Settings** goes into `config.json` on purpose, like one written there by hand. A key typed into the desktop app's **Run options** is used for that session only: it must never be written to disk (`config.json`, `gui.json` or anywhere else) and never appear in the copied command line. A key that leaks into a report, the Markdown or JSON output, the doctor output, a copied command or a file is in scope.
- **Endpoint credentials.** Endpoint URLs can carry `user:password@`. The desktop app's **Doctor** page masks them because it is made to be copied into bug reports. Credentials from an endpoint URL that show up unmasked in output meant for sharing are in scope.
- **Untrusted input.** Binary logs, text logs and piped `dotnet test` output may come from a CI system or another person. Problems a crafted log can cause are in scope: code execution, writing files, crashes or hangs, and lines from files unrelated to the build ending up in the bundle sent to a provider.

Out of scope: what a provider does with a request once it receives it, vulnerabilities in the .NET SDK, MSBuild or NuGet themselves, and the content of AI suggestions (review a suggested change before applying it, as with any other).
