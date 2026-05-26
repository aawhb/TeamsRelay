# Contributing to TeamsRelay

Thanks for considering a contribution. This project is small and welcomes both
bug reports and code.

## Reporting bugs

Open a GitHub issue with:

- The TeamsRelay version (`teamsrelay --version`)
- Windows + Teams desktop versions
- The `teamsrelay doctor` output (redact device IDs if you wish)
- A clear repro: what you did, what you expected, what you saw
- The last ~50 lines of `teamsrelay logs` if relevant

## Suggesting features

Open an issue first so we can discuss scope before code is written. Feature work
that lands without an issue may be asked to revise approach during review.

## Development

Prerequisites: .NET 9 SDK, [just](https://github.com/casey/just) (optional but
convenient), KDE Connect paired with a test device.

```powershell
just ci             # restore + build + test
just build          # debug build only
just test           # run xUnit suite
just run -- doctor  # run the CLI against the local source
```

Without `just`, use the equivalents:

```powershell
dotnet build
dotnet test
.\tr.cmd doctor
```

### Branch + PR workflow

1. Fork and create a topic branch (`feature/...`, `fix/...`).
2. Keep commits small and focused; subject lines in imperative mood.
3. Run `just ci` (or `dotnet test`) before pushing.
4. Open a PR with a description of the change, the motivation, and any
   user-visible behaviour difference. Link related issues.

### Style

- C#: file-scoped namespaces, `_camelCase` for private/internal instance fields,
  PascalCase for static/const, `var` where the type is obvious. Existing files
  are the reference — match them.
- No trailing whitespace, LF endings, UTF-8 without BOM (`.editorconfig` enforces).
- Comments only when they explain *why* — never *what* the code does.
- Don't add public API for hypothetical future callers.

### Tests

New behaviour needs a test. Fakes already live in `tests/TeamsRelay.Tests/`
(`FakeProcessLauncher`, `FakeTargetAdapter`, etc.) — prefer extending these over
introducing parallel mocks.

## License

By submitting a contribution you agree it is licensed under [GPL-3.0-only](LICENSE),
the same as the project.
