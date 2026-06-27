namespace Mdv.Tests;

public class TerminalRenderingTests
{
    [Fact]
    public void Headings_render_with_text()
    {
        var text = Render.Text("# Title\n\nBody paragraph.");
        Assert.Contains("Title", text);
        Assert.Contains("Body paragraph.", text);
    }

    [Theory]
    [InlineData("NOTE", "Note")]
    [InlineData("TIP", "Tip")]
    [InlineData("WARNING", "Warning")]
    [InlineData("IMPORTANT", "Important")]
    [InlineData("CAUTION", "Caution")]
    public void GitHub_alerts_show_a_typed_title(string kind, string title)
    {
        var md = $"> [!{kind}]\n> Alert body here.";
        var text = Render.Text(md);
        Assert.Contains(title, text);
        Assert.Contains("Alert body here.", text);
        // The literal marker must not leak into the output.
        Assert.DoesNotContain($"[!{kind}]", text);
    }

    [Fact]
    public void Task_list_checkboxes_are_rendered()
    {
        var text = Render.Text("- [x] done\n- [ ] todo");
        Assert.Contains("☑", text);   // checked
        Assert.Contains("☐", text);   // unchecked
        Assert.Contains("done", text);
        Assert.Contains("todo", text);
    }

    [Fact]
    public void Front_matter_is_stripped()
    {
        var md = "---\ntitle: Hidden\nauthor: nobody\n---\n\n# Visible\n\nContent.";
        var text = Render.Text(md);
        Assert.Contains("Visible", text);
        Assert.Contains("Content.", text);
        Assert.DoesNotContain("title: Hidden", text);
        Assert.DoesNotContain("author: nobody", text);
    }

    [Fact]
    public void Footnotes_render_marker_and_definition()
    {
        var md = "A claim.[^1]\n\n[^1]: The footnote text.";
        var text = Render.Text(md);
        Assert.Contains("¹", text);                 // superscript reference
        Assert.Contains("Footnotes", text);          // section separator
        Assert.Contains("The footnote text.", text); // definition body
    }

    [Fact]
    public void Definition_list_marks_definitions()
    {
        var md = "Term\n:   The definition.";
        var text = Render.Text(md);
        Assert.Contains("Term", text);
        Assert.Contains(": The definition.", text);  // ':'-prefixed definition line
    }

    [Fact]
    public void Table_alignment_right_pads_left()
    {
        // The right column header is wider than its data, so a right-aligned value gets left padding.
        var md = "| Left | Number |\n|:-----|-------:|\n| a | 7 |";
        var lines = Render.Lines(md, width: 60);
        var dataRow = lines.First(l => l.Contains("7") && l.Contains("a"));
        int idxA = dataRow.IndexOf('a');
        int idx7 = dataRow.IndexOf('7');
        Assert.True(idx7 > idxA);
        // "Number" is 6 wide; "7" right-aligned means several spaces precede it within the cell.
        Assert.Contains("     7", dataRow);
    }

    [Fact]
    public void Table_alignment_center_balances_padding()
    {
        var md = "| H |\n|:-:|\n| centered |";
        var lines = Render.Lines(md, width: 60);
        // The header "H" centered under a wide "centered" cell has padding on both sides.
        var headerRow = lines.First(l => l.Contains("H") && l.Contains("│"));
        int idxH = headerRow.IndexOf('H');
        int bar = headerRow.IndexOf('│');
        Assert.True(idxH - bar > 1, "centered header should have left padding");
    }

    [Fact]
    public void Wide_table_cell_wraps_to_multiple_lines()
    {
        var longText = string.Join(" ", Enumerable.Repeat("word", 30));
        var md = $"| Col |\n|-----|\n| {longText} |";
        var lines = Render.Lines(md, width: 40);
        int contentRows = lines.Count(l => l.Contains("word"));
        Assert.True(contentRows >= 2, $"expected wrapped cell across >=2 rows, got {contentRows}");
    }

    [Fact]
    public void Long_url_is_wrapped_not_clipped()
    {
        var url = "https://example.com/" + string.Concat(Enumerable.Repeat("segment/", 30)) + "end";
        var lines = Render.Lines($"See {url} here.", width: 40);
        // The URL spans more than one line and every line fits the width.
        Assert.All(lines, l => Assert.True(l.Length <= 40, $"line exceeds width: '{l}' ({l.Length})"));
        // The end of the URL still appears somewhere (not truncated away).
        Assert.Contains(lines, l => l.Contains("end"));
    }

    [Fact]
    public void Strikethrough_and_emphasis_text_survive()
    {
        var text = Render.Text("This is **bold**, *italic*, and ~~struck~~.");
        Assert.Contains("bold", text);
        Assert.Contains("italic", text);
        Assert.Contains("struck", text);
    }

    [Fact]
    public void Mermaid_fence_becomes_a_diagram_anchor()
    {
        var md = "```mermaid\ngraph TD; A-->B;\n```";
        var text = Render.Text(md);
        Assert.Contains("Mermaid", text);   // the caption label
    }
}
