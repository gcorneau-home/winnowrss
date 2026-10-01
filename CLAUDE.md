# WinnowRSS

Windows desktop RSS reader (WPF, .NET 10) that filters and summarizes articles with a local LLM (Ollama). Design context and decisions: `docs/DESIGN.md`. User docs: `README.md` and `README.fr.md` (keep both in step).

## Conventions

- All code, comments, identifiers and commit messages in **English**. Conversation with the user is in French.
- **Dumb UI**: `Winnow.App` only displays and forwards interactions. ViewModels hold display state and one-line commands that call services in `Winnow.Core`. Code-behind is limited to view plumbing (WebView2, drag & drop, tree multi-select). No business logic in event handlers.
- `Winnow.Core` has no dependency on WPF or SQLite; it talks to storage through interfaces in `Abstractions/`.
- Data access: `Microsoft.Data.Sqlite` + Dapper, hand-written SQL. Schema changes go in a new numbered migration script, tracked with `PRAGMA user_version`; never edit an applied migration.
- Commit messages: no title line; one change per line, each prefixed by a symbol: `+` added, `-` removed, `*` changed, `!` bug fix. Keep the Co-Authored-By trailer after a blank line.
- The article filter is `IArticleFilter` (`OllamaArticleFilter`). `FilterRules` (trusted feeds, title keywords) apply before the model.
- Summaries: `SummaryService` + `IArticleSummarizer` (`OllamaSummarizer`, streamed), one per article and language in `article_summaries`, with the filter's endpoint and model. Ollama calls share `OllamaHttp` (errors, and one context size so Ollama never reloads the model between filter and summaries). Qwen3 drifts to the article's language: keep the language and format reminder at the end of the request.
- UI text: `Resources/Strings.resx` plus `Strings.fr.resx` and `Strings.es.resx`; every key in all three (a test checks). Languages offered: `Localizer.Languages`; model instructions name languages with `LanguageNames.English`.

## Layout

- `src/Winnow.Core` — models, abstractions, services, feed fetching/parsing, filtering
- `src/Winnow.Data` — SQLite connection, migrations, repositories
- `src/Winnow.App` — WPF views, view models, DI bootstrap
- `tests/Winnow.Core.Tests` — xUnit, in-memory SQLite, feed fixtures (`Fixtures/sample-feed.xml` is invented content: never commit third-party articles)
- `tools/icon/make_icon.py` — single source of the app icon (Python + Pillow); writes `assets/icon/` (SVG, ICO 16–256, PNG). Rerun it after changing the geometry or colors
- `tools/Winnow.FilterBench` — console bench for the interest filter (prompt and input variants live in `BenchVariants.cs`)
- `tests/Winnow.App.UiTests` — FlaUI UI tests: launch the built app on a temp database, serve the feed fixture and a fake Ollama (verdicts, streamed summaries) from localhost, and drive it only through UI Automation patterns (never the real mouse/keyboard — the user works on the same machine). Give new interactive elements an `AutomationProperties.AutomationId`.

## Commands

- Build: `dotnet build WinnowRSS.sln`. If the user's own WinnowRSS (bin\Debug) is running, it locks the Debug output: never close it, build and test with `-c Release` instead (UI tests launch the app from their own configuration).
- Test: `dotnet test WinnowRSS.sln` (everything) or `dotnet test WinnowRSS.sln --filter "Category!=UI"` (skip the UI tests, which open windows)
- Filter bench: `dotnet run --project tools/Winnow.FilterBench -- --help`. Compares model × prompt × input variants against the user's 👍/👎 (read-only on the database); ranks by false rejects of 👍 first. Reports go to `filter-bench-*.md` (git-ignored).
- Real-model tests (a verdict and a summary, skipped by default): set `WINNOWRSS_OLLAMA_TESTS=1` and run `dotnet test tests/Winnow.Core.Tests --filter Category=Ollama` (needs Ollama with `qwen3:8b`)
- Run: `dotnet run --project src/Winnow.App` (the executable is `WinnowRSS.exe`)
- Version: `Directory.Build.props`. CI (`.github/workflows/ci.yml`) builds and runs the non-UI tests; pushing a `v*` tag runs `release.yml`, which publishes a self-contained win-x64 zip to a GitHub release.

Database: `%LOCALAPPDATA%\WinnowRSS\winnow.db`. Set `WINNOWRSS_DB` to another path to run the app on a throwaway database (manual tests, UI automation).
