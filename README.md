# GlyphCraft

**Character Map, turbo-charged.** A Windows app for inspecting, copying and exporting glyphs
from installed fonts *and* from font files on any drive or media.

Version 1.0.4 · 12 November 2026

## Features

- Big preview of the selected glyph (starts on **A**, or whatever sits in that place), with optional
  baseline / x-height / cap-height / ascender / descender guides and the advance box.
- Scrollable glyph grid under the preview - fast even with fonts of tens of thousands of glyphs.
  Click, arrow keys, Home/End/PageUp/PageDown. Double-click or Enter adds a glyph to *Characters to copy*.
- Shows the **Alt keystroke** (e.g. `Alt + 0065`) for characters that have one, plus Unicode value,
  decimal, HTML entity, glyph ID and advance width. Characters without an Alt-code show the Word
  `hex + Alt+X` method instead.
- **Right-click the preview or any glyph** for: Copy character, Add to "Characters to copy",
  Copy glyph as image (PNG), **Save glyph as EPS**, Save glyph as SVG.
- EPS files are true vector outlines (Bezier curves kept), 1 em = 1000 pt, CMYK black fill,
  tight bounding box - ready for Illustrator.
- **Full font preview** button: A-Z, a-z, 1234567890, punctuation, then extended punctuation and
  symbols, at an adjustable size.
- Filter the grid by Unicode block, find a character by typing it, its decimal code or `U+20AC`.
- Opens fonts from files: **TTF, OTF, TTC/OTC, WOFF, WOFF2, EOT** (uncompressed). Use *Open font*,
  *Open folder* (scans sub-folders, so a whole USB stick or CD works), or just drag files/folders
  onto the window. You can also pass a font file on the command line.

Not supported: Type 1 (.pfb/.pfm), bitmap fonts (.fon), SVG fonts, WOFF2 collections, and EOT files
using MicroType Express compression.

## Building (GitHub Actions)

1. Put this folder's contents in the root of a GitHub repository (branch `main`).
2. Push. The workflow in `.github/workflows/build.yml` builds a self-contained single-file
   `GlyphCraft.exe` on `windows-latest`.
3. Open the **Actions** tab, open the latest run, and download the **GlyphCraft-win-x64** artifact.

## Credits

GlyphCraft is created by **Sonus Lucis** ([sonuslucis.com](https://sonuslucis.com)) with Claude.ai.
Interface typeface: Montserrat (SIL Open Font License 1.1). See `THIRD-PARTY-NOTICES.txt`.

## Copyright

(c) 2026 Sonus Lucis. All rights reserved.

sonuslucis.com
