// readmd browser front-end: live reload, diagrams, search, TOC, multi-file nav.
// mermaid is loaded as a global (window.mermaid) via a <script> tag in the shell, because the
// bundled build is UMD/IIFE, not an ES module.
const mermaid = window.mermaid;

const content = document.getElementById("readmd-content");
const tocEl = document.getElementById("readmd-toc");
const statusEl = document.getElementById("readmd-status");
const statusText = document.getElementById("readmd-status-text");

let currentTheme = document.documentElement.getAttribute("data-theme") || "dark";

// A richer, more colorful mermaid theme (mermaid.live-ish) using the `base` theme + variables.
function mermaidConfig(theme) {
  const dark = theme === "dark";
  const vars = dark
    ? {
        darkMode: true, background: "#0d1117",
        primaryColor: "#1f2740", primaryTextColor: "#e6edf3", primaryBorderColor: "#7c8cf8",
        lineColor: "#8b9bf4", secondaryColor: "#2a2150", tertiaryColor: "#13202f",
        nodeBorder: "#7c8cf8", clusterBkg: "#161b2e", clusterBorder: "#3a4668",
        titleColor: "#c9d4ff", edgeLabelBackground: "#0d1117",
        actorBkg: "#1f2740", actorBorder: "#7c8cf8", actorTextColor: "#e6edf3",
        signalColor: "#a9b6f6", signalTextColor: "#cdd6f4", labelBoxBkgColor: "#1f2740",
        noteBkgColor: "#2a2150", noteTextColor: "#e6edf3", noteBorderColor: "#7c8cf8",
        sectionBkgColor: "#161b2e", altSectionBkgColor: "#1b2236", sectionBkgColor2: "#13202f",
        gridColor: "#6b78c0", todayLineColor: "#f08c8c",
        taskBkgColor: "#283255", taskTextColor: "#e6edf3", taskTextLightColor: "#e6edf3",
        taskTextOutsideColor: "#e6edf3", taskTextDarkColor: "#0d1117", taskBorderColor: "#7c8cf8",
        activeTaskBkgColor: "#3b4a9e", activeTaskBorderColor: "#a9b6f6",
        doneTaskBkgColor: "#2a3350", doneTaskBorderColor: "#5c6aa8",
        critBkgColor: "#5a2740", critBorderColor: "#f08cb0", doneTaskBkgColor2: "#2a3350",
        textColor: "#e6edf3",
        pieTitleTextColor: "#e6edf3", pieSectionTextColor: "#e6edf3", pieLegendTextColor: "#e6edf3",
        pieStrokeColor: "#0d1117", pieOuterStrokeColor: "#3a4668",
        pie1: "#5b6cd6", pie2: "#9a6cf0", pie3: "#4aa3d6", pie4: "#56b06a",
        pie5: "#e0a458", pie6: "#d6678c", pie7: "#7c8cf8", pie8: "#a371f7",
        attributeBackgroundColorOdd: "#161b2e", attributeBackgroundColorEven: "#1b2236",
        fontFamily: '"Segoe UI", system-ui, sans-serif',
      }
    : {
        darkMode: false, background: "#ffffff",
        primaryColor: "#eef1ff", primaryTextColor: "#1f2330", primaryBorderColor: "#6b7cff",
        lineColor: "#6b7cff", secondaryColor: "#f3edff", tertiaryColor: "#f6f8fa",
        nodeBorder: "#6b7cff", clusterBkg: "#f4f6ff", clusterBorder: "#c2ccff",
        titleColor: "#3b4a9e", edgeLabelBackground: "#ffffff",
        actorBkg: "#eef1ff", actorBorder: "#6b7cff", actorTextColor: "#1f2330",
        signalColor: "#5562d6", signalTextColor: "#1f2330", labelBoxBkgColor: "#eef1ff",
        noteBkgColor: "#f3edff", noteTextColor: "#1f2330", noteBorderColor: "#6b7cff",
        sectionBkgColor: "#eef1ff", altSectionBkgColor: "#f6f8fa", sectionBkgColor2: "#e6ebff",
        gridColor: "#c2ccff", todayLineColor: "#d6336c",
        taskBkgColor: "#dfe4ff", taskTextColor: "#1f2330", taskTextLightColor: "#1f2330",
        taskTextOutsideColor: "#1f2330", taskTextDarkColor: "#1f2330", taskBorderColor: "#6b7cff",
        activeTaskBkgColor: "#9fb0ff", activeTaskBorderColor: "#5562d6",
        doneTaskBkgColor: "#d4dbff", doneTaskBorderColor: "#9aa6e0",
        critBkgColor: "#ffd6e2", critBorderColor: "#d6336c", doneTaskBkgColor2: "#d4dbff",
        textColor: "#1f2330",
        pieTitleTextColor: "#1f2330", pieSectionTextColor: "#1f2330", pieLegendTextColor: "#1f2330",
        pieStrokeColor: "#ffffff", pieOuterStrokeColor: "#c2ccff",
        pie1: "#6b7cff", pie2: "#8250df", pie3: "#0969da", pie4: "#1a7f37",
        pie5: "#bf8700", pie6: "#cf222e", pie7: "#6b7cff", pie8: "#8250df",
        attributeBackgroundColorOdd: "#eef1ff", attributeBackgroundColorEven: "#f6f8fa",
        fontFamily: '"Segoe UI", system-ui, sans-serif',
      };
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
}

// ---------------- table of contents ----------------
function buildToc() {
  const headings = content.querySelectorAll("h1, h2, h3, h4, h5, h6");
  tocEl.innerHTML = "";
  const entries = [];
  headings.forEach((h) => {
    if (!h.id) h.id = slugify(h.textContent);
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
document.getElementById("readmd-theme-toggle").addEventListener("click", async () => {
  currentTheme = currentTheme === "dark" ? "light" : "dark";
  document.documentElement.setAttribute("data-theme", currentTheme);
  document.getElementById("hljs-theme").href = currentTheme === "dark"
    ? "/_readmd/vendor/github-dark.min.css" : "/_readmd/vendor/github.min.css";
  if (mermaid && mermaid.initialize) {
    mermaid.initialize(mermaidConfig(currentTheme));
  }
  // re-render diagrams for the new theme
  content.querySelectorAll("figure.readmd-diagram-mermaid[data-readmd-done]").forEach((f) => f.removeAttribute("data-readmd-done"));
  content.querySelectorAll(".readmd-d2-slot[data-readmd-done]").forEach((s) => { s.removeAttribute("data-readmd-done"); s.innerHTML = '<div class="readmd-diagram-placeholder">Rendering D2 diagram…</div>'; });
  await renderAll(content);
});

document.getElementById("readmd-back").addEventListener("click", () => history.back());
document.getElementById("readmd-forward").addEventListener("click", () => history.forward());

// ---------------- view toggles (sidebar / toolbar / zen) ----------------
const layout = document.getElementById("readmd-layout");
const toolbar = document.getElementById("readmd-toolbar");
function toggleSidebar() {
  document.body.classList.toggle("readmd-no-sidebar");
  flashStatus(document.body.classList.contains("readmd-no-sidebar") ? "Sidebar hidden" : "Sidebar shown");
}
function toggleToolbar() {
  document.body.classList.toggle("readmd-no-toolbar");
  flashStatus(document.body.classList.contains("readmd-no-toolbar") ? "Toolbar hidden" : "Toolbar shown");
}
function toggleZen() {
  const on = !document.body.classList.contains("readmd-zen");
  document.body.classList.toggle("readmd-zen", on);
  document.body.classList.toggle("readmd-no-sidebar", on);
  document.body.classList.toggle("readmd-no-toolbar", on);
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
(async function init() {
  if (!currentPath) {
    currentPath = document.body.getAttribute("data-readmd-initial-path") || "";
  }
  interceptLinks();
  await afterContentChange(false);
  connectLiveReload();
})();
