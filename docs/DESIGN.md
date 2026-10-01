# WinnowRSS design notes

## Goal

A simple RSS client for Windows that can **filter articles by interest** with a language model running locally. A reader follows many feeds but cares about a few topics; the filter sets the rest aside, says why, and never deletes anything on its own.

The project also had a second goal: **no line of code written by hand.** The author set themselves the challenge of building the whole application with [Claude Code](https://claude.com/claude-code), to test how far an AI coding agent can go on a real desktop application. That includes design, code, tests, UI automation, the icon and this documentation. The author's role was to state needs, make decisions, try the app and report problems.

## Requirements and decisions

- **Native Windows UI**, not a web app: WPF on .NET, in C#.
- **Local model only.** Articles never leave the machine. Ollama serves the model over a local HTTP API; Qwen3 8B is the default (good in English and French, fits in 8 GB of video memory with an 8,192-token context, a few seconds per article).
- **Low tolerance for wrong rejections.** A filtered-out article stays in its feed, grayed out (or hidden on demand), with the reason. Nothing goes to the trash automatically.
- **Mixed English and French feeds**, and an interface that switches language live (English, French, and Spanish to prove the localization holds).
- **Explicit criteria first.** The user writes interests and exclusions in plain words; the model applies them. Learning from 👍/👎 comes later, on top of that, not instead of it.

## Architecture

- `Winnow.Core`: models, services, feed fetching and parsing, the filter. No dependency on WPF or SQLite.
- `Winnow.Data`: SQLite through Microsoft.Data.Sqlite and Dapper, hand-written SQL, numbered migration scripts tracked with `PRAGMA user_version`, FTS5 full-text search.
- `Winnow.App`: WPF with MVVM (CommunityToolkit.Mvvm). The UI is deliberately "dumb": view models hold display state and forward commands to services; code-behind is limited to view plumbing (WebView2, drag and drop, tree multi-selection).
- The reading pane is **WebView2** serving the article from a virtual host, with scripts disabled and a strict content security policy, since feed content is untrusted. Archived articles keep their images in the database.
- Themes: light, dark, system, plus **VS Code color themes** imported from Open VSX or a file and mapped onto the app's brushes.

## Filtering pipeline

1. **Rules before the model.** Articles from a *trusted feed* are always kept. A title containing a *keyword* (whole word, any case) is filtered out. Neither needs the model.
2. **One model call per remaining article**, with the criteria in the system prompt and a short input: title, feed description when it adds something, and the first words of the text.
3. **Constrained output**: temperature 0, "thinking" disabled, a JSON schema naming the exclusion and the interest that apply plus a one-sentence reason. Code, not the model, turns that into a verdict: an exclusion rejects; no interest rejects; otherwise the article is kept.
4. The verdict, the deciding criterion or rule, the reason and the model are stored with the article and shown in the UI.
5. Criteria carry a version: editing them can re-filter unread articles.

## Measuring the filter

Published benchmarks say little about one person's interests. Instead, the user rates articles (👍, 👎 or nothing) in their own database, and a console bench (`tools/Winnow.FilterBench`) replays the filter over those ratings with variants of model, prompt and input. Variants are ranked by **false rejections of 👍 articles first**, then by agreement. The bench is read-only on the database.

What the bench showed: the default prompt and Qwen3 8B were good enough; most remaining errors were topics better handled by keywords (games, quizzes, podcasts), which led to the keyword and trusted-feed rules.

## Summaries

On request, from the article's toolbar: the same local model writes a one-sentence gist and 3 to 5 key points in the language chosen (English, French or Spanish), whatever the article's language. The answer is streamed so it appears as it is written, then saved per article and language and shown again when the article is reopened.

Two lessons from trying it on real articles: Qwen3 8B tends to answer in the article's language and to drop the bullet points, so the language and the format are restated at the end of the request, right before the answer. And the filter and the summarizer ask for the same context window (8,192 tokens), since Ollama reloads the model whenever it changes.

## Considered and left for later

- **Learning from 👍/👎**: an embedding per article (local multilingual embedding model) and a small classifier (logistic regression or k-nearest neighbors) to adjust the model's verdict, calling the model only for borderline cases once the classifier is reliable. Risk to manage: a filter bubble, so explicit criteria stay the floor and a small share of rejected articles should still be shown.
- **Full-text extraction** (a Readability port) for feeds that only give an excerpt.

## Existing projects looked at

No maintained open-source native Windows reader combined RSS and LLM filtering at the time. FreshRSS has LLM classification and summary extensions (web, PHP); EntroFeed and Good Reader score or summarize articles (web); llm_aggregator filters and summarizes from the command line (Go); RSS Guard and Fluent Reader are desktop readers without a model. Building from scratch was simpler than adapting one of them, since the reader is the easy part and the value is in the filter.
