# Contributing

Thanks for your interest! WinnowRSS is a small personal project, so please open an issue to discuss a change before sending a large pull request. Bug reports with steps to reproduce are very welcome.

## Building and testing

- Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download).
- `dotnet build WinnowRSS.sln`, then `dotnet test WinnowRSS.sln --filter "Category!=UI"`.
- The UI tests (`tests/Winnow.App.UiTests`, FlaUI) start the app and drive it through UI Automation, so they open windows: run them with `dotnet test WinnowRSS.sln` when you can leave the desktop alone for a couple of minutes.
- The tests against a real model (a verdict and a summary) are skipped unless `WINNOWRSS_OLLAMA_TESTS=1` is set (needs Ollama with `qwen3:8b`).
- `WINNOWRSS_DB` points the app to another database, handy for trying things without touching your own.

## Conventions

- Code, comments and identifiers in English.
- `Winnow.Core` holds the logic and has no dependency on WPF or SQLite; the WPF app only displays and forwards (view models call services, no business logic in code-behind).
- Schema changes go in a new numbered migration script in `src/Winnow.Data/Migrations`; never edit an applied one.
- User-facing text goes in `src/Winnow.App/Resources/Strings.resx` (English) and in each translation, `Strings.fr.resx` and `Strings.es.resx`; a test checks that every translation has the same keys and placeholders. To add a language, add a `Strings.<code>.resx` and an entry in `Localizer.Languages`.
- Give new interactive elements an `AutomationProperties.AutomationId` so UI tests can find them.
- Commit messages: one change per line, prefixed with `+` (added), `-` (removed), `*` (changed) or `!` (fixed).

More context in [CLAUDE.md](CLAUDE.md), the instructions used by Claude Code on this project, and in [docs/DESIGN.md](docs/DESIGN.md).
