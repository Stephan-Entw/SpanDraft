# SpanDraft user manual (German)

The Markdown sources in `docs/` are the German manual. The generated PNG assets
in `docs/assets/` come from SpanDraft's actual Avalonia renderers. Edit
these technical illustrations in `tools/SpanDraft.HelpAssets`, then regenerate
them; do not maintain separate engineering drawings. Conceptual explanations use
semantic HTML and local CSS in the manual.

## Build

Install .NET 10 and Python 3.11 or newer. Create and activate a Python virtual
environment, then install the pinned generator once:

```sh
python -m venv help/.venv
# Windows PowerShell: help/.venv/Scripts/Activate.ps1
# macOS/Linux: source help/.venv/bin/activate
python -m pip install -r help/requirements.txt
```

From the repository root:

```sh
dotnet run --file scripts/build-help.cs
```

The script regenerates the PNG assets, runs `zensical build --clean --strict`, and
checks the explicit PNG list and offline output. It never installs Python packages.
Commit Markdown, configuration, CSS, JavaScript and PNGs; `help/site/`, caches and the
virtual environment are ignored. The HelpAssets project is deliberately outside
the product solution.

Open `help/site/index.html` directly in a browser, with networking disabled, and
check navigation, search and images. The manual uses system fonts and a local
`iframe-worker` polyfill; no web server or runtime CDN is needed. Central
formulas use native MathML and need no additional runtime. The vendored
polyfill is iframe-worker 1.0.4 (MIT), from
https://github.com/squidfunk/iframe-worker; its license is beside the JavaScript.

After a successful help build, normal desktop build/publish copies the site to
`Help/` beside the application. A product-only .NET build does not require Python
or Zensical. If help has not been built, the menu reports that the manual cannot
be opened.
