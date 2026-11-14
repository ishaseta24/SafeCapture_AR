using System.Collections.Generic;
using UnityEngine;

// Minimal 5×7 bitmap font for drawing labels onto a Texture2D.
// Each glyph is 7 bytes — one per row top-to-bottom.
// Each byte: bit4=left pixel … bit0=right pixel (5 bits used).
public static class PixelFont
{
    private const int CharW = 5;
    private const int CharH = 7;
    private const int CharGap = 1;
    public  const int GlyphStride = CharW + CharGap; // 6 px per char
    public  const int LineHeight  = CharH + 2;       // 9 px per line incl. padding

    private static readonly Dictionary<char, byte[]> Glyphs = new Dictionary<char, byte[]>
    {
        [' '] = new byte[] { 0, 0, 0, 0, 0, 0, 0 },
        ['-'] = new byte[] { 0, 0, 0, 14, 0, 0, 0 },
        ['_'] = new byte[] { 0, 0, 0, 0, 0, 0, 31 },
        ['0'] = new byte[] { 14, 17, 17, 17, 17, 17, 14 },
        ['1'] = new byte[] {  4, 12,  4,  4,  4,  4, 14 },
        ['2'] = new byte[] { 14, 17,  1,  6,  8, 16, 31 },
        ['3'] = new byte[] { 14, 17,  1,  6,  1, 17, 14 },
        ['4'] = new byte[] {  2, 10, 18, 31,  2,  2,  0 },
        ['5'] = new byte[] { 31, 16, 30,  1,  1, 17, 14 },
        ['6'] = new byte[] {  6,  8, 16, 30, 17, 17, 14 },
        ['7'] = new byte[] { 31,  1,  2,  4,  8,  8,  0 },
        ['8'] = new byte[] { 14, 17, 17, 14, 17, 17, 14 },
        ['9'] = new byte[] { 14, 17, 17, 15,  1,  2, 12 },
        ['a'] = new byte[] {  0, 14,  1, 15, 17, 15,  0 },
        ['b'] = new byte[] { 16, 16, 30, 17, 17, 30,  0 },
        ['c'] = new byte[] {  0, 14, 16, 16, 16, 14,  0 },
        ['d'] = new byte[] {  1,  1, 15, 17, 17, 15,  0 },
        ['e'] = new byte[] {  0, 14, 17, 31, 16, 14,  0 },
        ['f'] = new byte[] {  0,  6,  8, 28,  8,  8,  0 },
        ['g'] = new byte[] {  0, 14, 17, 17, 15,  1, 14 },
        ['h'] = new byte[] { 16, 16, 30, 17, 17, 17,  0 },
        ['i'] = new byte[] {  0,  4,  0,  4,  4, 14,  0 },
        ['j'] = new byte[] {  0,  2,  0,  2,  2,  2, 12 },
        ['k'] = new byte[] { 16, 18, 20, 24, 20, 18,  0 },
        ['l'] = new byte[] { 12,  4,  4,  4,  4, 14,  0 },
        ['m'] = new byte[] {  0, 26, 21, 21, 21, 21,  0 },
        ['n'] = new byte[] {  0,  0, 30, 17, 17, 17,  0 },
        ['o'] = new byte[] {  0, 14, 17, 17, 17, 14,  0 },
        ['p'] = new byte[] {  0, 30, 17, 17, 30, 16, 16 },
        ['q'] = new byte[] {  0, 15, 17, 17, 15,  1,  1 },
        ['r'] = new byte[] {  0,  0, 28, 18, 16, 16,  0 },
        ['s'] = new byte[] {  0, 14, 16, 14,  1, 14,  0 },
        ['t'] = new byte[] {  0,  8, 28,  8,  8,  6,  0 },
        ['u'] = new byte[] {  0, 17, 17, 17, 17, 14,  0 },
        ['v'] = new byte[] {  0, 17, 17, 17, 10,  4,  0 },
        ['w'] = new byte[] {  0, 17, 17, 21, 21, 10,  0 },
        ['x'] = new byte[] {  0, 17, 10,  4, 10, 17,  0 },
        ['y'] = new byte[] {  0, 17, 17, 15,  1, 14,  0 },
        ['z'] = new byte[] {  0, 31,  2,  4,  8, 31,  0 },
    };

    // Draw text with a filled background rectangle.
    // (x, ty) = bottom-left corner in TEXTURE coords (bottom-up).
    // scale = pixel multiplier (2 = each glyph pixel becomes 2×2 screen pixels).
    // Returns the pixel width of the drawn string.
    public static int DrawStringBg(Texture2D tex, string text, int x, int ty,
                                    Color textColor, Color bgColor, int scale = 1)
    {
        scale = Mathf.Max(1, scale);
        int sw = CharW  * scale;
        int sh = CharH  * scale;
        int sg = CharGap * scale;
        int totalW = text.Length * (sw + sg);

        // Background rect
        for (int bx = x - scale; bx < x + totalW + scale; bx++)
            for (int by = ty - scale; by < ty + sh + scale; by++)
                if (bx >= 0 && bx < tex.width && by >= 0 && by < tex.height)
                    tex.SetPixel(bx, by, bgColor);

        // Characters
        for (int ci = 0; ci < text.Length; ci++)
        {
            char c = char.ToLower(text[ci]);
            if (!Glyphs.TryGetValue(c, out byte[] rows)) continue;
            int cx = x + ci * (sw + sg);
            for (int row = 0; row < CharH; row++)
            {
                int bits = rows[row];
                // row 0 = top of glyph = highest tex Y
                int pyBase = ty + (CharH - 1 - row) * scale;
                for (int col = 0; col < CharW; col++)
                {
                    if ((bits & (1 << (CharW - 1 - col))) == 0) continue;
                    int pxBase = cx + col * scale;
                    for (int sy = 0; sy < scale; sy++)
                    for (int sx = 0; sx < scale; sx++)
                    {
                        int px = pxBase + sx, py = pyBase + sy;
                        if (px >= 0 && px < tex.width && py >= 0 && py < tex.height)
                            tex.SetPixel(px, py, textColor);
                    }
                }
            }
        }
        return totalW;
    }
}
