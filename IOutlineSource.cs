namespace GlyphCraft.Core
{
    /// <summary>Anything that can hand back glyph outlines (TrueType/OpenType, Type 1, ...).</summary>
    public interface IOutlineSource
    {
        double UnitsPerEm { get; }
        int GlyphCount { get; }
        GlyphPath Get(int glyph);
    }
}
