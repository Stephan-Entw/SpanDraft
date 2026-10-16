# SpanDraft user manual (German)

The Markdown sources in `docs/` are the prepared German V1 manual. The six PNGs
in `docs/assets/` are generated from SpanDraft's actual Avalonia renderers. Edit
these illustrations in `tools/SpanDraft.HelpAssets`, then regenerate them; do not
maintain separate engineering drawings.

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

The script regenerates the six PNGs, runs `zensical build --clean --strict`, and
checks the offline output. It never installs Python packages. Commit Markdown,
configuration, JavaScript and PNGs; `help/site/`, caches and the virtual environment
are ignored. The HelpAssets project is deliberately outside the product solution.

Open `help/site/index.html` directly in a browser, with networking disabled, and
check navigation, search and images. The manual uses system fonts and a local
`iframe-worker` polyfill; no web server or runtime CDN is needed. The vendored
polyfill is iframe-worker 1.0.4 (MIT), from
https://github.com/squidfunk/iframe-worker; its license is beside the JavaScript.

After a successful help build, normal desktop build/publish copies the site to
`Help/` beside the application. A product-only .NET build does not require Python
or Zensical. If help has not been built, the menu reports that the manual cannot
be opened.
