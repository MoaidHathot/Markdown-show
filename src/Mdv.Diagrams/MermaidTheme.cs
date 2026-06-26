namespace Mdv.Diagrams;

/// <summary>
/// Shared mermaid theme configuration so terminal (Playwright) and browser renders look identical:
/// a richer, more colorful "base" theme with indigo/violet accents (mermaid.live-like).
/// </summary>
internal static class MermaidTheme
{
    public static string ConfigJson(bool dark) => dark ? DarkJson : LightJson;

    private const string DarkJson = """
        {
          "startOnLoad": false,
          "theme": "base",
          "securityLevel": "loose",
          "flowchart": { "curve": "basis", "htmlLabels": true, "padding": 12 },
          "sequence": { "useMaxWidth": true, "mirrorActors": false },
          "themeVariables": {
            "darkMode": true, "background": "#0d1117",
            "primaryColor": "#1f2740", "primaryTextColor": "#e6edf3", "primaryBorderColor": "#7c8cf8",
            "lineColor": "#8b9bf4", "secondaryColor": "#2a2150", "tertiaryColor": "#13202f",
            "nodeBorder": "#7c8cf8", "clusterBkg": "#161b2e", "clusterBorder": "#3a4668",
            "titleColor": "#c9d4ff", "edgeLabelBackground": "#0d1117",
            "actorBkg": "#1f2740", "actorBorder": "#7c8cf8", "actorTextColor": "#e6edf3",
            "signalColor": "#a9b6f6", "signalTextColor": "#cdd6f4", "labelBoxBkgColor": "#1f2740",
            "noteBkgColor": "#2a2150", "noteTextColor": "#e6edf3", "noteBorderColor": "#7c8cf8",
            "sectionBkgColor": "#161b2e", "altSectionBkgColor": "#1b2236", "sectionBkgColor2": "#13202f",
            "gridColor": "#6b78c0", "todayLineColor": "#f08c8c",
            "taskBkgColor": "#283255", "taskTextColor": "#e6edf3", "taskTextLightColor": "#e6edf3",
            "taskTextOutsideColor": "#e6edf3", "taskTextDarkColor": "#0d1117", "taskBorderColor": "#7c8cf8",
            "activeTaskBkgColor": "#3b4a9e", "activeTaskBorderColor": "#a9b6f6",
            "doneTaskBkgColor": "#2a3350", "doneTaskBorderColor": "#5c6aa8",
            "critBkgColor": "#5a2740", "critBorderColor": "#f08cb0", "doneTaskBkgColor2": "#2a3350",
            "textColor": "#e6edf3",
            "pieTitleTextColor": "#e6edf3", "pieSectionTextColor": "#e6edf3", "pieLegendTextColor": "#e6edf3",
            "pieStrokeColor": "#0d1117", "pieOuterStrokeColor": "#3a4668",
            "pie1": "#5b6cd6", "pie2": "#9a6cf0", "pie3": "#4aa3d6", "pie4": "#56b06a",
            "pie5": "#e0a458", "pie6": "#d6678c", "pie7": "#7c8cf8", "pie8": "#a371f7",
            "attributeBackgroundColorOdd": "#161b2e", "attributeBackgroundColorEven": "#1b2236",
            "fontFamily": "Segoe UI, system-ui, sans-serif"
          }
        }
        """;

    private const string LightJson = """
        {
          "startOnLoad": false,
          "theme": "base",
          "securityLevel": "loose",
          "flowchart": { "curve": "basis", "htmlLabels": true, "padding": 12 },
          "sequence": { "useMaxWidth": true, "mirrorActors": false },
          "themeVariables": {
            "darkMode": false, "background": "#ffffff",
            "primaryColor": "#eef1ff", "primaryTextColor": "#1f2330", "primaryBorderColor": "#6b7cff",
            "lineColor": "#6b7cff", "secondaryColor": "#f3edff", "tertiaryColor": "#f6f8fa",
            "nodeBorder": "#6b7cff", "clusterBkg": "#f4f6ff", "clusterBorder": "#c2ccff",
            "titleColor": "#3b4a9e", "edgeLabelBackground": "#ffffff",
            "actorBkg": "#eef1ff", "actorBorder": "#6b7cff", "actorTextColor": "#1f2330",
            "signalColor": "#5562d6", "signalTextColor": "#1f2330", "labelBoxBkgColor": "#eef1ff",
            "noteBkgColor": "#f3edff", "noteTextColor": "#1f2330", "noteBorderColor": "#6b7cff",
            "sectionBkgColor": "#eef1ff", "altSectionBkgColor": "#f6f8fa", "sectionBkgColor2": "#e6ebff",
            "gridColor": "#c2ccff", "todayLineColor": "#d6336c",
            "taskBkgColor": "#dfe4ff", "taskTextColor": "#1f2330", "taskTextLightColor": "#1f2330",
            "taskTextOutsideColor": "#1f2330", "taskTextDarkColor": "#1f2330", "taskBorderColor": "#6b7cff",
            "activeTaskBkgColor": "#9fb0ff", "activeTaskBorderColor": "#5562d6",
            "doneTaskBkgColor": "#d4dbff", "doneTaskBorderColor": "#9aa6e0",
            "critBkgColor": "#ffd6e2", "critBorderColor": "#d6336c", "doneTaskBkgColor2": "#d4dbff",
            "textColor": "#1f2330",
            "pieTitleTextColor": "#1f2330", "pieSectionTextColor": "#1f2330", "pieLegendTextColor": "#1f2330",
            "pieStrokeColor": "#ffffff", "pieOuterStrokeColor": "#c2ccff",
            "pie1": "#6b7cff", "pie2": "#8250df", "pie3": "#0969da", "pie4": "#1a7f37",
            "pie5": "#bf8700", "pie6": "#cf222e", "pie7": "#6b7cff", "pie8": "#8250df",
            "attributeBackgroundColorOdd": "#eef1ff", "attributeBackgroundColorEven": "#f6f8fa",
            "fontFamily": "Segoe UI, system-ui, sans-serif"
          }
        }
        """;
}
