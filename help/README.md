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
`assets/shared/` contains eight illustrations without language-dependent text.
`assets/de/` and `assets/en/` each contain two labeled examples and three result
diagrams. The transparent PNGs need no background correction in HTML or CSS.

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
search resources. The manual uses system fonts, native MathML and a local
`iframe-worker` polyfill; no web server or runtime CDN is needed. The vendored
polyfill is iframe-worker 1.0.4 (MIT), from
https://github.com/squidfunk/iframe-worker; its license is beside the JavaScript.

After a successful help build, normal desktop build/publish copies the complete
site to `Help/` beside the application. UI language selects `Help/de/index.html`
or `Help/en/index.html`; number formatting does not affect this selection.
A product-only .NET build needs neither Python nor Zensical. If the selected
manual is missing, the menu reports that it cannot be opened.
