# readmd — terminal Markdown viewer

A **terminal-first, feature-rich Markdown viewer** with an optional browser mode. It renders Markdown right in your terminal — including **mermaid** and **D2** diagrams (as real images via Sixel), syntax-highlighted code, tables, GitHub-style alerts, math, and an Azure DevOps `[[_TOC_]]` table of contents — and live-reloads as you edit. When you want full fidelity, the same document opens in your browser with one keypress.

```bash
dnx readmd report.md                 # view in the terminal (TUI)
dnx readmd report.md --browser       # view in the browser (full fidelity)
```

> Requires the **.NET 10 SDK** (for `dnx`). `d2` on your `PATH` is needed for D2 diagrams; the headless browser used for mermaid is downloaded automatically on first use.

## Features

- **Runs in the terminal** as a full TUI (alternate screen, scrolling, overlays).
- **One-shot launch** via `dnx readmd <file>` (or `npx`-style global install).
- **GitHub-Flavored Markdown**: headings, **bold/italic/strikethrough**, inline code, links, tables, lists, blockquotes, hard breaks, emoji shortcodes.
- **GitHub alerts** (`[!NOTE]`, `[!TIP]`, `[!IMPORTANT]`, `[!WARNING]`, `[!CAUTION]`) with colored icons and titles.
- **Task lists** (`- [x]` / `- [ ]`) rendered as ☑ / ☐, **footnotes** with linked markers, and **definition lists**.
- **Tables** honor column alignment (left/center/right) and **wrap** wide cells instead of truncating; long URLs wrap rather than clipping.
- **YAML front matter** is recognized and stripped (it won't leak into the rendered document).
- **Live reload** on file change. The browser preserves scroll position (DOM-morphing instead of full reload); the terminal repaints in place and caches diagrams so they don't flicker.
- **Search & navigation**: `/` to search, `n`/`N` to jump, `t` for a table-of-contents overlay, `?` for keybindings.
- **Azure DevOps `[[_TOC_]]`** marker support, plus a sticky TOC sidebar in the browser.
- **Mermaid & D2 diagrams**: rendered to images inline in the terminal (via Sixel), and natively in the browser. Render failures are reported inline with an actionable message.
- **Math** via KaTeX (browser) / a Unicode approximation (terminal), **code** via TextMate (terminal) / highlight.js (browser).
- **Multi-file wiki navigation**: follow local `.md` links with back/forward history (sandboxed to the document's directory). Opening an external link asks for confirmation first.
- **Browser mode** (`--browser`) for pixel-perfect rendering, served locally (loopback only) from an embedded web server.

## Usage

```
readmd <file> [options]

Options:
  -b, --browser        Open in the browser instead of the terminal.
  -p, --port <port>    Port for browser mode (0 = pick a free port; 0–65535).
      --no-open        In browser mode, start the server but don't launch a browser.
      --best-effort    Terminal mode: skip the headless-browser download; mermaid
                       diagrams open in the browser instead of rendering inline.
      --theme <theme>  Color theme: dark, light, or auto (auto honors COLORFGBG,
                       else defaults to dark).
      --background <b>  'solid' paints a solid themed background (overrides terminal
       (--bg)          transparency, like OpenCode); 'terminal' shows the terminal
                       background through. Toggle live with ].
      --d2-path <p>    Explicit path to the d2 executable.
  -v, --version        Print the version and exit.
  -h, --help           Show help.
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
| `m` | Mark mode (select + copy) | `q` (or `Ctrl+C`) | Quit |
| `?` | Keybindings overlay | | |

Mouse-wheel scrolling captures the mouse, which disables the terminal's native click-drag text
selection. Press `m` to enter **mark mode** (the status bar shows `[SELECT]`): **drag** with the left
button to select text, then **right-click** to copy the selection to the clipboard. `Esc` clears the
selection; press `m` again to leave mark mode and restore wheel scrolling. Copy uses the Win32
clipboard on Windows and OSC 52 elsewhere (Windows Terminal, WezTerm, kitty, iTerm2, …).

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

`readmd` is a single .NET 10 tool composed of five projects:

| Project | Responsibility |
| --- | --- |
| **Readmd.Core** | Markdig pipeline, `[[_TOC_]]` extension, TOC/diagram extraction, file watching, link resolution, diagram cache contracts. |
| **Readmd.Diagrams** | Renders mermaid (headless Chromium via Playwright, with mermaid bundled) and D2 (`d2` → SVG → PNG via SkiaSharp), cached by content hash. |
| **Readmd.Web** | Kestrel server: HTML rendering, server-rendered D2 SVG, SSE live-reload with `idiomorph`, multi-file SPA navigation, embedded assets. |
| **Readmd.Terminal** | Hand-rolled TUI: ANSI/truecolor renderer, Markdown→styled-line layout, TextMate highlighting, Sixel image output, search/TOC/links/history. |
| **Readmd.Cli** | `System.CommandLine` entry point; packaged as the `readmd` .NET tool. |

The Markdig pipeline and diagram cache are shared by both front-ends, so the terminal and browser views stay consistent and diagrams are only rendered once per content hash.

## Terminal image support

Inline diagrams in the terminal use the **Sixel** graphics protocol. Supported terminals include Windows Terminal (≥ 1.22), WezTerm, xterm, mintty, foot, and others. In terminals without graphics support, run with `--best-effort` and press `o` to view diagrams in the browser.

## Platform support

`readmd` targets .NET 10 and is cross-platform (Windows, macOS, Linux). A few terminal niceties are currently Windows-only: **mouse-wheel scrolling, click-to-follow-link, and the `m` select-mode** rely on the Windows console API. On macOS/Linux the viewer is fully keyboard-driven (all keybindings still work). The browser front-end is identical on every platform.

> The first mermaid diagram triggers a one-time headless-browser (Chromium) download via Playwright, which can be large and may take a moment. Use `--best-effort` to skip it (mermaid then opens in your browser on demand). D2 requires the `d2` binary on your `PATH` (or `--d2-path`); if it's missing, the diagram area shows an actionable message.

## Building from source

```bash
git clone https://github.com/MoaidHathot/readmd
cd readmd
dotnet build Readmd.slnx
dotnet test tests/Readmd.Tests/Readmd.Tests.csproj                   # run the test suite
dotnet run --project src/Readmd.Cli -- samples/demo.md            # terminal
dotnet run --project src/Readmd.Cli -- samples/demo.md --browser  # browser

# Pack & install the tool locally
dotnet pack src/Readmd.Cli -c Release -o artifacts/nupkg
dotnet tool install --global --add-source artifacts/nupkg readmd
```

`samples/demo.md` exercises every feature (TOC, tables, task lists, alerts, code, mermaid, D2, math, multi-file links). CI (GitHub Actions) builds and tests on Windows, macOS, and Linux, and packs the tool.

`tools/SixelView` is a dev-only diagnostic that decodes the terminal's Sixel/ANSI output back to PNG for visual verification; it is not part of the shipped tool.

## License

MIT
