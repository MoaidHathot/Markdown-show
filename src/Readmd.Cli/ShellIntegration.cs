using System.Text;

/// <summary>
/// Generates shell completion scripts and a man page for the <c>readmd</c> command. The option
/// list is kept here in one place so both outputs stay in sync with the CLI.
/// </summary>
internal static class ShellIntegration
{
    // (flags, value-placeholder-or-null, description). Long flag first.
    private static readonly (string Flags, string? Value, string Desc)[] Options =
    [
        ("--browser -b", null, "Open in the browser instead of the terminal"),
        ("--port -p", "PORT", "Port for browser mode (0 = pick a free port)"),
        ("--no-open", null, "In browser mode, start the server but don't launch a browser"),
        ("--best-effort", null, "Terminal mode: skip the headless-browser download"),
        ("--export -e -o", "PATH", "Export to a self-contained .html or .pdf and exit"),
        ("--print", null, "Render to stdout and exit (text/ANSI)"),
        ("--theme", "THEME", "Color theme: dark, light, auto, or a custom name"),
        ("--background --bg", "MODE", "Background fill: solid or terminal"),
        ("--d2-path", "PATH", "Explicit path to the d2 executable"),
        ("--version -v", null, "Show version information"),
        ("--help -h", null, "Show help and usage information"),
    ];

    private static readonly string[] ThemeValues = ["dark", "light", "auto"];
    private static readonly string[] BackgroundValues = ["solid", "terminal"];

    public static string ForShell(string shell) => shell.Trim().ToLowerInvariant() switch
    {
        "bash" => Bash(),
        "zsh" => Zsh(),
        "pwsh" or "powershell" => Pwsh(),
        "fish" => Fish(),
        _ => throw new ArgumentException($"unknown shell '{shell}' (expected: bash, zsh, pwsh, fish)"),
    };

    private static IEnumerable<string> AllFlags() =>
        Options.SelectMany(o => o.Flags.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string Bash()
    {
        var flags = string.Join(' ', AllFlags());
        return $$"""
            # readmd bash completion. Add to ~/.bashrc:  source <(readmd completions bash)
            _readmd() {
              local cur prev
              cur="${COMP_WORDS[COMP_CWORD]}"
              prev="${COMP_WORDS[COMP_CWORD-1]}"
              case "$prev" in
                --theme) COMPREPLY=( $(compgen -W "{{string.Join(' ', ThemeValues)}}" -- "$cur") ); return;;
                --background|--bg) COMPREPLY=( $(compgen -W "{{string.Join(' ', BackgroundValues)}}" -- "$cur") ); return;;
                --export|-e|-o|--d2-path) COMPREPLY=( $(compgen -f -- "$cur") ); return;;
              esac
              if [[ "$cur" == -* ]]; then
                COMPREPLY=( $(compgen -W "{{flags}}" -- "$cur") )
              else
                COMPREPLY=( $(compgen -f -- "$cur") )
              fi
            }
            complete -F _readmd readmd

            """;
    }

    private static string Zsh()
    {
        var sb = new StringBuilder();
        sb.AppendLine("#compdef readmd");
        sb.AppendLine("# readmd zsh completion. Add to your fpath, or:  source <(readmd completions zsh)");
        sb.AppendLine("_readmd() {");
        sb.AppendLine("  _arguments \\");
        foreach (var (flags, value, desc) in Options)
        {
            var parts = flags.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var spec = parts.Length > 1 ? "{" + string.Join(",", parts) + "}" : parts[0];
            var safeDesc = desc.Replace("'", "");
            if (value is not null)
                sb.AppendLine($"    {spec}'[{safeDesc}]:{value}:_files' \\");
            else
                sb.AppendLine($"    {spec}'[{safeDesc}]' \\");
        }
        sb.AppendLine("    '*:file:_files'");
        sb.AppendLine("}");
        sb.AppendLine("_readmd \"$@\"");
        return sb.ToString();
    }

    private static string Fish()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# readmd fish completion. Save to ~/.config/fish/completions/readmd.fish");
        foreach (var (flags, value, desc) in Options)
        {
            var parts = flags.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var line = new StringBuilder("complete -c readmd");
            foreach (var p in parts)
            {
                if (p.StartsWith("--")) line.Append($" -l {p[2..]}");
                else if (p.StartsWith('-')) line.Append($" -s {p[1..]}");
            }
            if (value is null) line.Append(" -f");
            line.Append($" -d '{desc.Replace("'", "")}'");
            sb.AppendLine(line.ToString());
        }
        return sb.ToString();
    }

    private static string Pwsh()
    {
        var flagList = string.Join(", ", AllFlags().Select(f => $"'{f}'"));
        return $$"""
            # readmd PowerShell completion. Add to your $PROFILE:  readmd completions pwsh | Out-String | Invoke-Expression
            Register-ArgumentCompleter -Native -CommandName readmd -ScriptBlock {
                param($wordToComplete, $commandAst, $cursorPosition)
                $flags = @({{flagList}})
                $flags | Where-Object { $_ -like "$wordToComplete*" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterName', $_)
                }
            }

            """;
    }

    /// <summary>Generates a troff/groff man page (section 1) for readmd.</summary>
    public static string ManPage(string version)
    {
        // Use just the semantic version for the header (drop any +buildmetadata).
        var shortVersion = version.Split('+')[0];
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var sb = new StringBuilder();
        sb.AppendLine($".TH READMD 1 \"{date}\" \"readmd {shortVersion}\" \"User Commands\"");
        sb.AppendLine(".SH NAME");
        sb.AppendLine("readmd \\- terminal-first Markdown viewer");
        sb.AppendLine(".SH SYNOPSIS");
        sb.AppendLine(".B readmd");
        sb.AppendLine("[\\fIOPTIONS\\fR] \\fIFILE\\fR");
        sb.AppendLine(".br");
        sb.AppendLine(".B readmd -");
        sb.AppendLine("(read Markdown from standard input)");
        sb.AppendLine(".SH DESCRIPTION");
        sb.AppendLine("readmd renders Markdown in the terminal \\(em including mermaid, D2, Graphviz and");
        sb.AppendLine("PlantUML diagrams (as images via Sixel or half-block), syntax-highlighted code,");
        sb.AppendLine("tables, GitHub alerts, math, and an Azure DevOps [[_TOC_]] \\(em and live-reloads as");
        sb.AppendLine("you edit. With \\fB--browser\\fR the same document opens in your browser.");
        sb.AppendLine(".SH OPTIONS");
        foreach (var (flags, value, desc) in Options)
        {
            var rendered = flags.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(f => value is not null ? $"\\fB{f}\\fR \\fI{value}\\fR" : $"\\fB{f}\\fR");
            sb.AppendLine(".TP");
            sb.AppendLine(string.Join(", ", rendered));
            sb.AppendLine(desc + ".");
        }
        sb.AppendLine(".SH FILES");
        sb.AppendLine(".TP");
        sb.AppendLine("\\fI~/.config/readmd/config.json\\fR");
        sb.AppendLine("User configuration (themes, key bindings, defaults).");
        sb.AppendLine(".TP");
        sb.AppendLine("\\fI.readmd.json\\fR");
        sb.AppendLine("Project configuration, found by walking up from the document.");
        sb.AppendLine(".SH ENVIRONMENT");
        sb.AppendLine(".TP");
        sb.AppendLine("\\fBREADMD_GRAPHICS\\fR");
        sb.AppendLine("Inline image mode: sixel, half-block, none, or auto.");
        sb.AppendLine(".SH SEE ALSO");
        sb.AppendLine("Project home: https://github.com/MoaidHathot/readmd");
        return sb.ToString();
    }
}
