# SpanDraft offline user manuals

`docs/de/` and `docs/en/` contain the eleven German and English pages. German
remains the reference structure. Shared styles, scripts and templates live in
`shared/`; `zensical.toml` is the common build template, with language-specific
navigation in `nav.de.toml` and `nav.en.toml`.

The header's language links stay on the corresponding page. The explicit page
pairs in `scripts/build-help.cs` must cover both source trees exactly once; update
them when adding or moving a page. The build generates and validates the local
language links and current-language markers without changing the Markdown sources.

Technical PNGs come from SpanDraft's actual Avalonia renderers. Edit them in
`tools/SpanDraft.HelpAssets`, then regenerate; do not maintain separate drawings.
`assets/shared/` contains ten illustrations without language-dependent text.
`assets/de/` and `assets/en/` each contain four labeled examples and three result
diagrams. The transparent PNGs need no background correction in HTML or CSS.

The bundled example projects come from the same tool: `HelpExamples.cs` holds one
mechanical definition per example, which feeds its figures and its project files
`assets/<language>/examples/<id>.spandraft` (fixed identities, localized project
information only). The desktop app embeds these files and opens them for
`spandraft://beispiel/<id>`. `--check` regenerates them in memory and reports
missing, damaged, stale or unexpected files without writing; regenerate after
every change to a definition.

An example section links its file relatively, as
`[Beispieldatei herunterladen](../assets/examples/<id>.spandraft)`; on the website
this stays a download. The offline build turns the link into
`spandraft://beispiel/<id>` (“Beispiel in SpanDraft öffnen”) and adds a hint with the
local file for systems without the protocol handler. It validates that each
example is linked exactly once per language, on its page.

## Build

Install .NET 10 and Python 3.11 or newer. Create a Python virtual environment and
install the pinned generator once:

```sh
python -m venv help/.venv
# Windows PowerShell: help/.venv/Scripts/Activate.ps1
# macOS/Linux: source help/.venv/bin/activate
python -m pip install -r help/requirements.txt
```

The build automatically uses Zensical from `help/.venv` when present, with `PATH`
as a fallback. Activating the environment is not required. From the repository root:

```sh
dotnet run --file scripts/build-help.cs
```

The script regenerates and checks the PNGs, stages complete inputs per language,
runs `zensical build --clean --strict` for each, and validates the resulting local
links, fragments, resources and MathML. It never installs Python packages.
Windows packaging uses `--no-regenerate-assets` with the committed PNGs; it still
checks transparency and builds and validates both complete manuals.

Commit Markdown, configuration, shared resources and PNGs. `help/site/`,
`help/.build/`, generated configuration, caches and the virtual environment are
ignored. The HelpAssets project stays outside the product solution.

Open `help/site/de/index.html` or `help/site/en/index.html` directly in a browser,
with networking disabled, and check navigation, search and images. The build
places technical PNGs once under `help/site/assets/` and adjusts their HTML links;
keep the complete `site/` tree together. Each language retains its own theme and
search resources. Normal text uses bundled IBM Plex Sans; code uses IBM Plex Mono.
The build copies the desktop font sources and their NOTICE/OFL files once to
`site/assets/fonts/`. Both languages and the standalone 404 pages load the same
font definitions through `shared/stylesheets/fonts.css`; the theme's `fonts`
template block replaces its external font loader. The manual uses native MathML
and a local `iframe-worker` polyfill; no web server or runtime CDN is needed. The vendored
polyfill is iframe-worker 1.0.4 (MIT), from
https://github.com/squidfunk/iframe-worker; its license is beside the JavaScript.

After a successful help build, normal desktop build/publish copies the complete
site to `Help/` beside the application. UI language selects `Help/de/index.html`
or `Help/en/index.html`; number formatting does not affect this selection.
A product-only .NET build needs neither Python nor Zensical. If the selected
manual is missing, the menu reports that it cannot be opened.
