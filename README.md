# Markdown-show (`mdv`)

A **terminal-first, feature-rich Markdown viewer** with an optional browser mode. It renders Markdown right in your terminal — including **mermaid** and **D2** diagrams (as real images via Sixel), syntax-highlighted code, tables, GitHub-style alerts, math, and an Azure DevOps `[[_TOC_]]` table of contents — and live-reloads as you edit. When you want full fidelity, the same document opens in your browser with one keypress.

```bash
dnx mdv report.md                 # view in the terminal (TUI)
dnx mdv report.md --browser       # view in the browser (full fidelity)
```

> Requires the **.NET 10 SDK** (for `dnx`). `d2` on your `PATH` is needed for D2 diagrams; the headless browser used for mermaid is downloaded automatically on first use.

## Features

- **Runs in the terminal** as a full TUI (alternate screen, scrolling, overlays).
- **One-shot launch** via `dnx mdv <file>` (or `npx`-style global install).
- **Live reload** on file change. The browser preserves scroll position (DOM-morphing instead of full reload); the terminal repaints in place and caches diagrams so they don't flicker.
- **Search & navigation**: `/` to search, `n`/`N` to jump, `t` for a table-of-contents overlay.
- **Azure DevOps `[[_TOC_]]`** marker support, plus a sticky TOC sidebar in the browser.
- **Mermaid & D2 diagrams**: rendered to images inline in the terminal, and natively in the browser.
- **Math** via KaTeX, **code** via TextMate (terminal) / highlight.js (browser).
- **Multi-file wiki navigation**: follow local `.md` links with back/forward history (sandboxed to the document's directory).
- **Browser mode** (`--browser`) for pixel-perfect rendering, served locally from an embedded web server.

## Usage

```
mdv <file> [options]

Options:
  -b, --browser        Open in the browser instead of the terminal.
  -p, --port <port>    Port for browser mode (0 = pick a free port).
      --no-open        In browser mode, start the server but don't launch a browser.
      --best-effort    Terminal mode: skip the headless-browser download; mermaid
                       diagrams open in the browser instead of rendering inline.
      --theme <theme>  Color theme: dark, light, or auto.
      --background <b>  'solid' paints a solid themed background (overrides terminal
       (--bg)          transparency, like OpenCode); 'terminal' shows the terminal
                       background through. Toggle live with ].
      --d2-path <p>    Explicit path to the d2 executable.
```

### Terminal keybindings

Vim-style motions are supported throughout, plus the mouse wheel scrolls.

| Key | Action | Key | Action |
| --- | --- | --- | --- |
| `j` / `k` (or arrows) | Scroll one line | mouse wheel | Scroll |
| `Ctrl` + mouse wheel | Zoom diagrams in / out | `/` | Search |
| `Ctrl+e` / `Ctrl+y` | Scroll one line (vim) | `n` / `N` | Next / previous match |
| `Ctrl+d` / `Ctrl+u` | Half page | `n` / `N` | Next / previous match |
| `Ctrl+f` / `Ctrl+b` | Full page | `t` | Table-of-contents overlay |
| `Space` / `b` | Page down / up | `Enter`, `1`–`5` | Follow a visible link |
| `gg` / `G` | Top / bottom | `←` / `→` (or `Backspace`) | Back / forward (history) |
| `Home` / `End` | Top / bottom | `[` | Toggle light / dark theme |
| `o` | Open in browser | `]` | Toggle solid / transparent background |
| `m` | Select-text mode (mouse) | `q` (or `Ctrl+C`) | Quit |
| `?` | Keybindings overlay | | |

Mouse-wheel scrolling captures the mouse, which disables the terminal's native click-drag text
selection. Press `m` to toggle **select mode** (the status bar shows `[SELECT]`): the wheel stops
scrolling and you can drag to select/copy text; press `m` again to restore wheel scrolling.

Theme and background can also be set at launch with `--theme` and `--background`.

### Browser keybindings

The browser view uses the same vim-style motions, plus toggles to hide chrome:

| Key | Action | Key | Action |
| --- | --- | --- | --- |
| `j` / `k` (or arrows) | Scroll | `/` or `Ctrl+F` | Search |
| `Ctrl+d` / `Ctrl+u` | Half page | `n` / `N` | Next / previous match |
| `Ctrl+f` / `Ctrl+b` | Full page | `t` | Toggle sidebar (TOC) |
| `Space` / `PageUp` | Page down / up | `s` | Toggle toolbar |
| `gg` / `G` | Top / bottom | `z` | Zen mode (hide all chrome) |
| `Alt+←` / `Alt+→` | Back / forward | `[` | Toggle light / dark theme |

## How it works

`mdv` is a single .NET 10 tool composed of five projects:

| Project | Responsibility |
| --- | --- |
| **Mdv.Core** | Markdig pipeline, `[[_TOC_]]` extension, TOC/diagram extraction, file watching, link resolution, diagram cache contracts. |
| **Mdv.Diagrams** | Renders mermaid (headless Chromium via Playwright, with mermaid bundled) and D2 (`d2` → SVG → PNG via SkiaSharp), cached by content hash. |
| **Mdv.Web** | Kestrel server: HTML rendering, server-rendered D2 SVG, SSE live-reload with `idiomorph`, multi-file SPA navigation, embedded assets. |
| **Mdv.Terminal** | Hand-rolled TUI: ANSI/truecolor renderer, Markdown→styled-line layout, TextMate highlighting, Sixel image output, search/TOC/links/history. |
| **Mdv.Cli** | `System.CommandLine` entry point; packaged as the `mdv` .NET tool. |

The Markdig pipeline and diagram cache are shared by both front-ends, so the terminal and browser views stay consistent and diagrams are only rendered once per content hash.

## Terminal image support

Inline diagrams in the terminal use the **Sixel** graphics protocol. Supported terminals include Windows Terminal (≥ 1.22), WezTerm, xterm, mintty, foot, and others. In terminals without graphics support, run with `--best-effort` and press `o` to view diagrams in the browser.

## Building from source

```bash
git clone <repo>
cd Markdown-show
dotnet build Mdv.slnx
dotnet run --project src/Mdv.Cli -- samples/demo.md            # terminal
dotnet run --project src/Mdv.Cli -- samples/demo.md --browser  # browser

# Pack & install the tool locally
dotnet pack src/Mdv.Cli -c Release -o artifacts/nupkg
dotnet tool install --global --add-source artifacts/nupkg mdv
```

`samples/demo.md` exercises every feature (TOC, tables, task lists, alerts, code, mermaid, D2, math, multi-file links).

## License

MIT
