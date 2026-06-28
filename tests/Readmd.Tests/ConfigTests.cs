using Readmd.Core;
using Readmd.Terminal;

namespace Readmd.Tests;

public class ConfigTests
{
    [Fact]
    public void Parse_reads_scalars_and_themes_and_keys()
    {
        var json = """
        {
          "theme": "mytheme",
          "background": "solid",
          "d2Path": "/usr/local/bin/d2",
          "themes": { "mytheme": { "dark": true, "background": "#101010", "h1": "#ff0000" } },
          "keys": { "quit": "x", "search": "s" }
        }
        """;
        var cfg = ReadmdConfig.Parse(json);
        Assert.Equal("mytheme", cfg.Theme);
        Assert.Equal("solid", cfg.Background);
        Assert.Equal("/usr/local/bin/d2", cfg.D2Path);
        Assert.NotNull(cfg.Themes);
        Assert.True(cfg.Themes!.ContainsKey("mytheme"));
        Assert.Equal("#ff0000", cfg.Themes["mytheme"].H1);
        Assert.Equal("x", cfg.Keys!["quit"]);
    }

    [Fact]
    public void Parse_tolerates_comments_and_trailing_commas()
    {
        var json = "{ \"theme\": \"dark\", /* note */ \"background\": \"terminal\", }";
        var cfg = ReadmdConfig.Parse(json);
        Assert.Equal("dark", cfg.Theme);
        Assert.Equal("terminal", cfg.Background);
    }

    [Fact]
    public void MergedWith_overrides_only_non_null_values()
    {
        var baseCfg = ReadmdConfig.Parse("""{ "theme": "dark", "d2Path": "/a" }""");
        var over = ReadmdConfig.Parse("""{ "theme": "light" }""");
        var merged = baseCfg.MergedWith(over);
        Assert.Equal("light", merged.Theme);     // overridden
        Assert.Equal("/a", merged.D2Path);       // preserved from base
    }

    [Fact]
    public void Empty_or_blank_json_yields_empty_config()
    {
        Assert.Same(ReadmdConfig.Empty, ReadmdConfig.Parse(""));
        Assert.Same(ReadmdConfig.Empty, ReadmdConfig.Parse("   "));
    }

    [Fact]
    public void Color_theme_fills_unset_colors_from_base()
    {
        var ct = new ColorTheme { Dark = true, H1 = "#abcdef" };
        var theme = TerminalTheme.FromColorTheme(ct);
        Assert.True(theme.IsDark);
        Assert.Equal(Rgb.FromHex("#abcdef"), theme.H1);
        // Unset color falls back to the built-in dark value.
        Assert.Equal(TerminalTheme.Dark.Text, theme.Text);
    }

    [Fact]
    public void FindProjectConfig_walks_up_to_dot_readmd_json()
    {
        var root = Path.Combine(Path.GetTempPath(), "readmd-cfg-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "a", "b");
        Directory.CreateDirectory(nested);
        var cfgPath = Path.Combine(root, ".readmd.json");
        File.WriteAllText(cfgPath, """{ "theme": "light" }""");
        var docPath = Path.Combine(nested, "doc.md");
        File.WriteAllText(docPath, "# hi");
        try
        {
            var found = ConfigLoader.FindProjectConfig(docPath);
            Assert.Equal(cfgPath, found);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}

public class KeyMapTests
{
    [Fact]
    public void Default_bindings_resolve_common_actions()
    {
        var km = KeyMap.Default;
        Assert.Equal(EditorAction.Quit, km.Resolve('q'));
        Assert.Equal(EditorAction.ScrollDown, km.Resolve('j'));
        Assert.Equal(EditorAction.Search, km.Resolve('/'));
        Assert.Equal(EditorAction.None, km.Resolve('z'));
    }

    [Fact]
    public void Config_remaps_action_to_new_key_and_frees_old_key()
    {
        var km = KeyMap.FromConfig(new Dictionary<string, string> { ["quit"] = "x" });
        Assert.Equal(EditorAction.Quit, km.Resolve('x'));
        Assert.Equal(EditorAction.None, km.Resolve('q')); // old default no longer maps to quit
    }

    [Fact]
    public void Config_supports_space_keyword()
    {
        var km = KeyMap.FromConfig(new Dictionary<string, string> { ["pagedown"] = "space" });
        Assert.Equal(EditorAction.PageDown, km.Resolve(' '));
    }

    [Fact]
    public void Unknown_action_names_are_ignored()
    {
        var km = KeyMap.FromConfig(new Dictionary<string, string> { ["frobnicate"] = "z" });
        Assert.Equal(EditorAction.None, km.Resolve('z'));
        Assert.Equal(EditorAction.Quit, km.Resolve('q')); // defaults intact
    }
}
