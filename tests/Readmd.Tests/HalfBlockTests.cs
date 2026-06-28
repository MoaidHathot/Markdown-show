using Readmd.Terminal;
using SkiaSharp;

namespace Readmd.Tests;

public class HalfBlockTests
{
    private static SKBitmap SolidBitmap(int w, int h, SKColor color)
    {
        var bmp = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(color);
        return bmp;
    }

    [Fact]
    public void Encodes_two_pixel_rows_per_text_row()
    {
        using var bmp = SolidBitmap(4, 6, SKColors.Red);
        var lines = HalfBlockEncoder.Encode(bmp, new Rgb(0, 0, 0));
        Assert.Equal(3, lines.Count); // 6 pixel rows / 2 = 3 text rows
    }

    [Fact]
    public void Odd_height_rounds_up_rows()
    {
        using var bmp = SolidBitmap(4, 5, SKColors.Red);
        var lines = HalfBlockEncoder.Encode(bmp, new Rgb(0, 0, 0));
        Assert.Equal(3, lines.Count); // ceil(5/2)
    }

    [Fact]
    public void Output_uses_upper_half_block_and_truecolor()
    {
        using var bmp = SolidBitmap(2, 2, new SKColor(0x12, 0x34, 0x56));
        var lines = HalfBlockEncoder.Encode(bmp, new Rgb(0, 0, 0));
        var line = Assert.Single(lines);
        Assert.Contains("\u2580", line);                 // ▀
        Assert.Contains("\e[38;2;18;52;86m", line);      // fg = #123456
        Assert.EndsWith("\e[0m", line);
    }
}

public class TerminalCapabilitiesTests
{
    [Fact]
    public void Explicit_config_overrides_detection()
    {
        Assert.Equal(GraphicsMode.HalfBlock, TerminalCapabilities.Resolve("half-block"));
        Assert.Equal(GraphicsMode.Sixel, TerminalCapabilities.Resolve("sixel"));
        Assert.Equal(GraphicsMode.None, TerminalCapabilities.Resolve("none"));
    }

    [Fact]
    public void Auto_falls_back_to_detection()
    {
        // "auto" should not throw and must return a concrete mode.
        var mode = TerminalCapabilities.Resolve("auto");
        Assert.True(mode is GraphicsMode.Sixel or GraphicsMode.HalfBlock or GraphicsMode.None);
    }
}
