// readmd browser front-end: live reload, diagrams, search, TOC, multi-file nav.
// mermaid is loaded as a global (window.mermaid) via a <script> tag in the shell, because the
// bundled build is UMD/IIFE, not an ES module.
const mermaid = window.mermaid;

const content = document.getElementById("readmd-content");
const tocEl = document.getElementById("readmd-toc");
const statusEl = document.getElementById("readmd-status");
const statusText = document.getElementById("readmd-status-text");

let currentTheme = document.documentElement.getAttribute("data-theme") || "dark";

// ---------------- persisted UI preferences ----------------
// Theme and chrome toggles persist across reloads/navigations via localStorage (best-effort:
// private-mode failures are ignored). The server still provides the initial theme, but a saved
// user preference wins after the first visit.
const PREFS_KEY = "readmd:prefs";
function loadPrefs() {
  try { return JSON.parse(localStorage.getItem(PREFS_KEY) || "{}") || {}; } catch { return {}; }
}
function savePref(key, value) {
  try {
    const p = loadPrefs();
    p[key] = value;
    localStorage.setItem(PREFS_KEY, JSON.stringify(p));
  } catch { /* storage unavailable */ }
}

// Mermaid theme variables are injected by the server (window.__READMD_MERMAID__) from the same
// C# source the terminal Playwright render uses, so both front-ends share one palette.
function mermaidConfig(theme) {
  const themes = window.__READMD_MERMAID__ || {};
  const vars = themes[theme === "dark" ? "dark" : "light"] || {};
  return {
    startOnLoad: false,
    theme: "base",
    securityLevel: "loose",
    themeVariables: vars,
    flowchart: { curve: "basis", htmlLabels: true, padding: 12 },
    sequence: { useMaxWidth: true, mirrorActors: false },
  };
}

if (mermaid && mermaid.initialize) {
  mermaid.initialize(mermaidConfig(currentTheme));
}

// ---------------- rendering passes ----------------
async function renderMermaid(root) {
  if (!mermaid || !mermaid.render) return;
  const blocks = root.querySelectorAll("figure.readmd-diagram-mermaid pre.mermaid:not([data-readmd-done])");
  for (const pre of blocks) {
    const src = pre.textContent;
    const id = "m" + Math.random().toString(36).slice(2);
    try {
      const { svg } = await mermaid.render(id, src);
      const fig = pre.closest("figure");
      fig.innerHTML = svg;
      fig.setAttribute("data-readmd-done", "1");
    } catch (e) {
      pre.setAttribute("data-readmd-done", "1");
      pre.outerHTML = `<div class="readmd-diagram-error">Mermaid error: ${escapeHtml(String(e))}</div>`;
    }
  }
}

async function renderD2(root) {
  const slots = root.querySelectorAll(".readmd-d2-slot:not([data-readmd-done])");
  for (const slot of slots) {
    const key = slot.getAttribute("data-readmd-key");
    slot.setAttribute("data-readmd-done", "1");
    try {
      const resp = await fetch(`/_readmd/diagram/${key}?theme=${currentTheme}&format=svg`);
      if (!resp.ok) throw new Error(await resp.text());
      slot.innerHTML = await resp.text();
    } catch (e) {
      slot.innerHTML = `<div class="readmd-diagram-error">D2 error: ${escapeHtml(String(e))}</div>`;
    }
  }
}

function renderMath(root) {
  if (window.renderMathInElement) {
    window.renderMathInElement(root, {
      delimiters: [
        { left: "$$", right: "$$", display: true },
        { left: "$", right: "$", display: false },
        { left: "\\(", right: "\\)", display: false },
        { left: "\\[", right: "\\]", display: true },
      ],
      throwOnError: false,
    });
  }
}

function highlight(root) {
  if (!window.hljs) return;
  root.querySelectorAll("pre code").forEach((block) => {
    if (block.closest(".readmd-diagram")) return;
    window.hljs.highlightElement(block);
  });
}

async function renderAll(root) {
  highlight(root);
  renderMath(root);
  await renderMermaid(root);
  await renderD2(root);
  addCopyButtons(root);
}

// Adds a hover "Copy" button to every code block (skipping diagram containers).
function addCopyButtons(root) {
  root.querySelectorAll("pre:not([data-readmd-copy])").forEach((pre) => {
    if (pre.closest(".readmd-diagram")) return;
    const code = pre.querySelector("code") || pre;
    pre.setAttribute("data-readmd-copy", "1");
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "readmd-copy-btn";
    btn.textContent = "Copy";
    btn.setAttribute("aria-label", "Copy code to clipboard");
    btn.addEventListener("click", async (e) => {
      e.preventDefault();
      try {
        await navigator.clipboard.writeText(code.innerText.replace(/\n$/, ""));
        btn.textContent = "Copied!";
        btn.classList.add("readmd-copied");
      } catch {
        btn.textContent = "Failed";
      }
      setTimeout(() => { btn.textContent = "Copy"; btn.classList.remove("readmd-copied"); }, 1400);
    });
    pre.appendChild(btn);
  });
}

// Appends a "#" permalink anchor to a heading; clicking copies the full URL with the fragment.
function addHeadingAnchor(h) {
  if (h.querySelector(".readmd-anchor")) return;
  const link = document.createElement("a");
  link.className = "readmd-anchor";
  link.href = "#" + h.id;
  link.textContent = "#";
  link.setAttribute("aria-label", "Link to this section");
  link.addEventListener("click", (e) => {
    e.preventDefault();
    h.scrollIntoView({ behavior: "smooth", block: "start" });
    history.replaceState(history.state, "", "#" + h.id);
    const url = location.origin + location.pathname + location.search + "#" + h.id;
    navigator.clipboard?.writeText(url).then(() => flashStatus("Link copied"), () => {});
  });
  h.appendChild(link);
}

// ---------------- table of contents ----------------
function buildToc() {
  const headings = content.querySelectorAll("h1, h2, h3, h4, h5, h6");
  tocEl.innerHTML = "";
  const entries = [];
  headings.forEach((h) => {
    if (!h.id) h.id = slugify(h.textContent);
    addHeadingAnchor(h);
    const level = Number(h.tagName.substring(1));
    const a = document.createElement("a");
    a.href = "#" + h.id;
    a.textContent = h.textContent;
    a.className = "lvl-" + level;
    a.addEventListener("click", (e) => {
      e.preventDefault();
      h.scrollIntoView({ behavior: "smooth", block: "start" });
      history.replaceState(history.state, "", "#" + h.id);
    });
    tocEl.appendChild(a);
    entries.push({ h, a });
  });
  // also fill inline [[_TOC_]] blocks
  content.querySelectorAll("[data-readmd-toc-inline]").forEach((nav) => {
    nav.innerHTML = "";
    entries.forEach(({ h }) => {
      const level = Number(h.tagName.substring(1));
      const a = document.createElement("a");
      a.href = "#" + h.id;
      a.textContent = h.textContent;
      a.className = "lvl-" + level;
      a.addEventListener("click", (e) => { e.preventDefault(); h.scrollIntoView({ behavior: "smooth" }); });
      nav.appendChild(a);
    });
  });
  return entries;
}

let tocEntries = [];
function setupScrollSpy() {
  const observer = new IntersectionObserver((obs) => {
    obs.forEach((entry) => {
      if (entry.isIntersecting) {
        const id = entry.target.id;
        tocEntries.forEach(({ h, a }) => a.classList.toggle("active", h.id === id));
      }
    });
  }, { rootMargin: "0px 0px -80% 0px", threshold: 0 });
  tocEntries.forEach(({ h }) => observer.observe(h));
}

// ---------------- search ----------------
const searchInput = document.getElementById("readmd-search");
const searchCount = document.getElementById("readmd-search-count");
let hits = [];
let hitIndex = -1;

function clearSearch() {
  content.querySelectorAll("mark.readmd-hit").forEach((m) => {
    const parent = m.parentNode;
    parent.replaceChild(document.createTextNode(m.textContent), m);
    parent.normalize();
  });
  hits = [];
  hitIndex = -1;
  searchCount.textContent = "";
}

function runSearch(query) {
  clearSearch();
  if (!query || query.length < 2) return;
  const walker = document.createTreeWalker(content, NodeFilter.SHOW_TEXT, {
    acceptNode(node) {
      if (!node.nodeValue.trim()) return NodeFilter.FILTER_REJECT;
      if (node.parentElement.closest("script,style,mark")) return NodeFilter.FILTER_REJECT;
      return NodeFilter.FILTER_ACCEPT;
    },
  });
  const lower = query.toLowerCase();
  const targets = [];
  let n;
  while ((n = walker.nextNode())) {
    if (n.nodeValue.toLowerCase().includes(lower)) targets.push(n);
  }
  for (const node of targets) {
    const text = node.nodeValue;
    const frag = document.createDocumentFragment();
    let i = 0, idx;
    const l = text.toLowerCase();
    while ((idx = l.indexOf(lower, i)) !== -1) {
      if (idx > i) frag.appendChild(document.createTextNode(text.slice(i, idx)));
      const mark = document.createElement("mark");
      mark.className = "readmd-hit";
      mark.textContent = text.slice(idx, idx + query.length);
      frag.appendChild(mark);
      hits.push(mark);
      i = idx + query.length;
    }
    if (i < text.length) frag.appendChild(document.createTextNode(text.slice(i)));
    node.parentNode.replaceChild(frag, node);
  }
  if (hits.length) { hitIndex = 0; focusHit(); }
  searchCount.textContent = hits.length ? `${hitIndex + 1}/${hits.length}` : "0 results";
}

function focusHit() {
  hits.forEach((m, i) => m.classList.toggle("current", i === hitIndex));
  if (hitIndex >= 0 && hits[hitIndex]) {
    hits[hitIndex].scrollIntoView({ behavior: "smooth", block: "center" });
    searchCount.textContent = `${hitIndex + 1}/${hits.length}`;
  }
}
function nextHit(dir) {
  if (!hits.length) return;
  hitIndex = (hitIndex + dir + hits.length) % hits.length;
  focusHit();
}

let searchTimer;
searchInput.addEventListener("input", () => {
  clearTimeout(searchTimer);
  searchTimer = setTimeout(() => runSearch(searchInput.value), 150);
});
searchInput.addEventListener("keydown", (e) => {
  if (e.key === "Enter") { e.preventDefault(); nextHit(e.shiftKey ? -1 : 1); }
  if (e.key === "Escape") { searchInput.value = ""; clearSearch(); searchInput.blur(); }
});
document.getElementById("readmd-search-next").addEventListener("click", () => nextHit(1));
document.getElementById("readmd-search-prev").addEventListener("click", () => nextHit(-1));

// ---------------- navigation (multi-file wiki) ----------------
async function navigate(path, push = true) {
  showStatus("Loading…");
  try {
    const resp = await fetch(`/_readmd/doc?path=${encodeURIComponent(path)}`);
    if (!resp.ok) throw new Error(await resp.text());
    const data = await resp.json();
    morphContent(data.html);
    document.title = data.title;
    document.getElementById("readmd-doc-title").textContent = data.title;
    if (push) history.pushState({ path }, "", `/?path=${encodeURIComponent(path)}`);
    currentPath = path;
    await afterContentChange(true);
    window.scrollTo({ top: 0 });
    hideStatus();
  } catch (e) {
    showStatus("Failed to load: " + e.message, true);
  }
}

function interceptLinks() {
  content.addEventListener("click", (e) => {
    const a = e.target.closest("a");
    if (!a) return;
    const href = a.getAttribute("href");
    if (!href) return;
    if (href.startsWith("#")) return; // anchor handled by browser/scroll
    const local = a.getAttribute("data-readmd-local");
    if (local) {
      e.preventDefault();
      navigate(local);
    }
    // external links keep default behavior (open normally)
  });
}

window.addEventListener("popstate", (e) => {
  const path = e.state?.path || new URLSearchParams(location.search).get("path");
  if (path) navigate(path, false);
});

// ---------------- live reload via SSE ----------------
let currentPath = new URLSearchParams(location.search).get("path") || "";
function connectLiveReload() {
  const es = new EventSource("/_readmd/events");
  es.addEventListener("reload", async (ev) => {
    try {
      const payload = JSON.parse(ev.data);
      if (payload.path && currentPath && normalize(payload.path) !== normalize(currentPath)) return;
      const resp = await fetch(`/_readmd/doc?path=${encodeURIComponent(currentPath || payload.path)}`);
      if (!resp.ok) return;
      const data = await resp.json();
      morphContent(data.html);
      document.title = data.title;
      document.getElementById("readmd-doc-title").textContent = data.title;
      await afterContentChange(false);
      flashStatus("Reloaded");
    } catch { /* ignore transient */ }
  });
  es.onerror = () => { /* browser auto-reconnects */ };
}

function morphContent(html) {
  // Idiomorph diffs the new HTML into the live DOM, preserving scroll position and
  // untouched nodes (already-rendered diagrams) instead of replacing innerHTML.
  const next = document.createElement("article");
  next.id = "readmd-content";
  next.innerHTML = html;
  window.Idiomorph.morph(content, next, { morphStyle: "innerHTML" });
}

// ---------------- shared post-render ----------------
async function afterContentChange(resetScroll) {
  await renderAll(content);
  tocEntries = buildToc();
  setupScrollSpy();
  if (searchInput.value) runSearch(searchInput.value);
}

// ---------------- theme ----------------
async function setTheme(theme, { persist = true, rerender = true } = {}) {
  currentTheme = theme;
  document.documentElement.setAttribute("data-theme", currentTheme);
  document.getElementById("hljs-theme").href = currentTheme === "dark"
    ? "/_readmd/vendor/github-dark.min.css" : "/_readmd/vendor/github.min.css";
  if (mermaid && mermaid.initialize) {
    mermaid.initialize(mermaidConfig(currentTheme));
  }
  if (persist) savePref("theme", currentTheme);
  if (rerender) {
    // re-render diagrams for the new theme
    content.querySelectorAll("figure.readmd-diagram-mermaid[data-readmd-done]").forEach((f) => f.removeAttribute("data-readmd-done"));
    content.querySelectorAll(".readmd-d2-slot[data-readmd-done]").forEach((s) => { s.removeAttribute("data-readmd-done"); s.innerHTML = '<div class="readmd-diagram-placeholder">Rendering D2 diagram…</div>'; });
    await renderAll(content);
  }
}
document.getElementById("readmd-theme-toggle").addEventListener("click", () =>
  setTheme(currentTheme === "dark" ? "light" : "dark"));

document.getElementById("readmd-back").addEventListener("click", () => history.back());
document.getElementById("readmd-forward").addEventListener("click", () => history.forward());

// ---------------- export (HTML / PDF) ----------------
const exportBtn = document.getElementById("readmd-export");
const exportMenu = document.getElementById("readmd-export-menu");
function toggleExportMenu(force) {
  const show = force ?? exportMenu.classList.contains("readmd-hidden");
  exportMenu.classList.toggle("readmd-hidden", !show);
  exportBtn.setAttribute("aria-expanded", String(show));
  if (show) exportMenu.querySelector("button")?.focus();
}
function doExport(format) {
  toggleExportMenu(false);
  const params = new URLSearchParams({ format });
  if (currentPath) params.set("path", currentPath);
  if (format === "pdf") showStatus("Preparing PDF…");
  // Trigger a download by navigating a hidden iframe; the attachment Content-Disposition makes the
  // browser save it without leaving the page. An error (e.g. PDF tooling missing) flashes a status.
  let frame = document.getElementById("readmd-download-frame");
  if (!frame) {
    frame = document.createElement("iframe");
    frame.id = "readmd-download-frame";
    frame.style.display = "none";
    document.body.appendChild(frame);
  }
  frame.src = `/_readmd/export?${params.toString()}`;
  if (format === "pdf") setTimeout(hideStatus, 4000);
}
exportBtn?.addEventListener("click", (e) => { e.stopPropagation(); toggleExportMenu(); });
exportMenu?.addEventListener("click", (e) => {
  const item = e.target.closest("button[data-format]");
  if (item) doExport(item.getAttribute("data-format"));
});
document.addEventListener("click", (e) => {
  if (!exportMenu.classList.contains("readmd-hidden") && !e.target.closest("#readmd-export-wrap")) toggleExportMenu(false);
});
exportMenu?.addEventListener("keydown", (e) => { if (e.key === "Escape") { e.preventDefault(); toggleExportMenu(false); exportBtn.focus(); } });

// ---------------- view toggles (sidebar / toolbar / zen) ----------------
const layout = document.getElementById("readmd-layout");
const toolbar = document.getElementById("readmd-toolbar");
function toggleSidebar() {
  const hidden = document.body.classList.toggle("readmd-no-sidebar");
  savePref("noSidebar", hidden);
  flashStatus(hidden ? "Sidebar hidden" : "Sidebar shown");
}
function toggleToolbar() {
  const hidden = document.body.classList.toggle("readmd-no-toolbar");
  savePref("noToolbar", hidden);
  flashStatus(hidden ? "Toolbar hidden" : "Toolbar shown");
}
function toggleZen() {
  const on = !document.body.classList.contains("readmd-zen");
  document.body.classList.toggle("readmd-zen", on);
  document.body.classList.toggle("readmd-no-sidebar", on);
  document.body.classList.toggle("readmd-no-toolbar", on);
  savePref("zen", on);
  flashStatus(on ? "Zen mode (z to exit)" : "Zen mode off");
}

// Hook up toolbar buttons that may exist in the shell.
document.getElementById("readmd-toggle-sidebar")?.addEventListener("click", toggleSidebar);
document.getElementById("readmd-toggle-zen")?.addEventListener("click", toggleZen);

// ---------------- help overlay (which-key style) ----------------
const helpOverlay = document.getElementById("readmd-help-overlay");
const HELP_SECTIONS = [
  { title: "Move", items: [
    ["j / k", "down / up a line"],
    ["Ctrl+e / Ctrl+y", "scroll a line"],
    ["Ctrl+d / Ctrl+u", "half page"],
    ["Ctrl+f / Ctrl+b", "full page"],
    ["Space / PageUp", "page down / up"],
    ["gg / G", "top / bottom"],
  ]},
  { title: "Find & navigate", items: [
    ["/ or Ctrl+F", "search"],
    ["n / N", "next / prev match"],
    ["Alt+← / Alt+→", "back / forward"],
  ]},
  { title: "View", items: [
    ["t", "toggle sidebar (TOC)"],
    ["s", "toggle toolbar"],
    ["z", "zen mode"],
    ["[", "toggle theme"],
    ["r", "reload (refresh)"],
    ["?", "this help"],
  ]},
];
function buildHelp() {
  const body = document.getElementById("readmd-help-body");
  if (body.childElementCount) return; // build once
  for (const section of HELP_SECTIONS) {
    const group = document.createElement("div");
    group.className = "readmd-help-group";
    const h = document.createElement("h4");
    h.textContent = section.title;
    group.appendChild(h);
    for (const [keys, desc] of section.items) {
      const row = document.createElement("div");
      row.className = "readmd-help-row";
      const k = document.createElement("span");
      k.innerHTML = keys.split(" / ").map((part) =>
        part.split("+").map((p) => `<kbd>${escapeHtml(p)}</kbd>`).join("+")
      ).join(" / ");
      const d = document.createElement("span");
      d.className = "desc";
      d.textContent = desc;
      row.appendChild(k); row.appendChild(d);
      group.appendChild(row);
    }
    body.appendChild(group);
  }
}
let helpLastFocus = null;
function toggleHelp(force) {
  buildHelp();
  const show = force ?? helpOverlay.classList.contains("readmd-hidden");
  helpOverlay.classList.toggle("readmd-hidden", !show);
  if (show) {
    helpLastFocus = document.activeElement;
    document.getElementById("readmd-help-close")?.focus();
  } else if (helpLastFocus && helpLastFocus.focus) {
    helpLastFocus.focus();
    helpLastFocus = null;
  }
}
// Trap focus inside the help dialog while it's open.
helpOverlay?.addEventListener("keydown", (e) => {
  if (e.key !== "Tab" || helpOverlay.classList.contains("readmd-hidden")) return;
  const focusable = helpOverlay.querySelectorAll("button, [href], input, [tabindex]:not([tabindex='-1'])");
  if (focusable.length === 0) return;
  const first = focusable[0], last = focusable[focusable.length - 1];
  if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
  else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
});
helpOverlay?.addEventListener("click", (e) => { if (e.target === helpOverlay) toggleHelp(false); });
document.getElementById("readmd-help-close")?.addEventListener("click", () => toggleHelp(false));

// ---------------- terminal-like keybindings ----------------
let gPending = 0;
function scrollByLines(n) { window.scrollBy({ top: n * 40, behavior: "instant" in window ? "instant" : "auto" }); }
function pageHeight() { return window.innerHeight - 40; }

document.addEventListener("keydown", (e) => {
  const typing = document.activeElement === searchInput;

  // Close help on Esc (works even while typing).
  if (e.key === "Escape" && !helpOverlay.classList.contains("readmd-hidden")) { e.preventDefault(); toggleHelp(false); return; }

  // Ctrl+F always focuses search (browser-native find is replaced by ours).
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "f") { e.preventDefault(); searchInput.focus(); searchInput.select(); return; }
  if (e.altKey && e.key === "ArrowLeft") { e.preventDefault(); history.back(); return; }
  if (e.altKey && e.key === "ArrowRight") { e.preventDefault(); history.forward(); return; }

  if (typing) return; // don't hijack keys while typing in the search box

  // Ctrl combos (vim-style paging).
  if (e.ctrlKey && !e.altKey && !e.metaKey) {
    const k = e.key.toLowerCase();
    if (k === "d") { e.preventDefault(); window.scrollBy({ top: pageHeight() / 2 }); return; }
    if (k === "u") { e.preventDefault(); window.scrollBy({ top: -pageHeight() / 2 }); return; }
    if (k === "f") { e.preventDefault(); window.scrollBy({ top: pageHeight() }); return; }
    if (k === "b") { e.preventDefault(); window.scrollBy({ top: -pageHeight() }); return; }
    if (k === "e") { e.preventDefault(); scrollByLines(1); return; }
    if (k === "y") { e.preventDefault(); scrollByLines(-1); return; }
    return;
  }
  if (e.metaKey || e.altKey) return;

  switch (e.key) {
    case "j": case "ArrowDown": e.preventDefault(); scrollByLines(1); break;
    case "k": case "ArrowUp": e.preventDefault(); scrollByLines(-1); break;
    case " ": case "PageDown": e.preventDefault(); window.scrollBy({ top: pageHeight() }); break;
    case "PageUp": e.preventDefault(); window.scrollBy({ top: -pageHeight() }); break;
    case "G": e.preventDefault(); window.scrollTo({ top: document.body.scrollHeight }); break;
    case "g":
      if (gPending && Date.now() - gPending < 700) { e.preventDefault(); window.scrollTo({ top: 0 }); gPending = 0; }
      else gPending = Date.now();
      break;
    case "Home": e.preventDefault(); window.scrollTo({ top: 0 }); break;
    case "End": e.preventDefault(); window.scrollTo({ top: document.body.scrollHeight }); break;
    case "/": e.preventDefault(); searchInput.focus(); searchInput.select(); break;
    case "n": nextHit(1); break;
    case "N": nextHit(-1); break;
    case "t": e.preventDefault(); toggleSidebar(); break;
    case "s": e.preventDefault(); toggleToolbar(); break;
    case "z": e.preventDefault(); toggleZen(); break;
    case "[": e.preventDefault(); document.getElementById("readmd-theme-toggle").click(); break;
    case "e": e.preventDefault(); toggleExportMenu(true); break;
    case "r": e.preventDefault(); location.reload(); break;
    case "?": e.preventDefault(); toggleHelp(); break;
  }
  if (e.key !== "g") gPending = 0;
});

// ---------------- helpers ----------------
function slugify(t) {
  return t.toLowerCase().trim().replace(/[^\w\s-]/g, "").replace(/\s+/g, "-");
}
function escapeHtml(s) {
  return s.replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}
function normalize(p) { return (p || "").replace(/\\/g, "/").toLowerCase(); }
let statusHideTimer;
function showStatus(msg, isError) {
  statusText.textContent = msg;
  statusEl.classList.remove("readmd-hidden");
  statusEl.style.borderColor = isError ? "#da3633" : "var(--border)";
}
function hideStatus() { statusEl.classList.add("readmd-hidden"); }
function flashStatus(msg) {
  showStatus(msg, false);
  clearTimeout(statusHideTimer);
  statusHideTimer = setTimeout(hideStatus, 1200);
}

// ---------------- boot ----------------
function applySavedPrefs() {
  const p = loadPrefs();
  // Theme: the <head> inline script already set data-theme to avoid a flash; sync the rest here.
  const docTheme = document.documentElement.getAttribute("data-theme") || "dark";
  setTheme(docTheme, { persist: false, rerender: false });
  // Chrome toggles.
  if (p.zen) {
    document.body.classList.add("readmd-zen", "readmd-no-sidebar", "readmd-no-toolbar");
  } else {
    if (p.noSidebar) document.body.classList.add("readmd-no-sidebar");
    if (p.noToolbar) document.body.classList.add("readmd-no-toolbar");
  }
}
(async function init() {
  if (!currentPath) {
    currentPath = document.body.getAttribute("data-readmd-initial-path") || "";
  }
  applySavedPrefs();
  interceptLinks();
  await afterContentChange(false);
  connectLiveReload();
})();
