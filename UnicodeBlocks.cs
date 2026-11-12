using System;
using System.Collections.Generic;
using System.Globalization;

namespace GlyphCraft.Core
{
    public static class UnicodeBlocks
    {
        public const string Other = "Other / Symbol area";

        static readonly string Table =
            "0000-007F Basic Latin|0080-00FF Latin-1 Supplement|0100-017F Latin Extended-A|0180-024F Latin Extended-B|" +
            "0250-02AF IPA Extensions|02B0-02FF Spacing Modifier Letters|0300-036F Combining Diacritical Marks|" +
            "0370-03FF Greek and Coptic|0400-04FF Cyrillic|0500-052F Cyrillic Supplement|0530-058F Armenian|" +
            "0590-05FF Hebrew|0600-06FF Arabic|0700-074F Syriac|0750-077F Arabic Supplement|0780-07BF Thaana|" +
            "07C0-07FF NKo|0800-083F Samaritan|0840-085F Mandaic|0860-086F Syriac Supplement|08A0-08FF Arabic Extended-A|" +
            "0900-097F Devanagari|0980-09FF Bengali|0A00-0A7F Gurmukhi|0A80-0AFF Gujarati|0B00-0B7F Oriya|" +
            "0B80-0BFF Tamil|0C00-0C7F Telugu|0C80-0CFF Kannada|0D00-0D7F Malayalam|0D80-0DFF Sinhala|0E00-0E7F Thai|" +
            "0E80-0EFF Lao|0F00-0FFF Tibetan|1000-109F Myanmar|10A0-10FF Georgian|1100-11FF Hangul Jamo|1200-137F Ethiopic|" +
            "1380-139F Ethiopic Supplement|13A0-13FF Cherokee|1400-167F Unified Canadian Aboriginal Syllabics|" +
            "1680-169F Ogham|16A0-16FF Runic|1700-171F Tagalog|1780-17FF Khmer|1800-18AF Mongolian|" +
            "1AB0-1AFF Combining Diacritical Marks Extended|1D00-1D7F Phonetic Extensions|" +
            "1D80-1DBF Phonetic Extensions Supplement|1DC0-1DFF Combining Diacritical Marks Supplement|" +
            "1E00-1EFF Latin Extended Additional|1F00-1FFF Greek Extended|2000-206F General Punctuation|" +
            "2070-209F Superscripts and Subscripts|20A0-20CF Currency Symbols|2100-214F Letterlike Symbols|" +
            "20D0-20FF Combining Diacritical Marks for Symbols|2150-218F Number Forms|2190-21FF Arrows|" +
            "2200-22FF Mathematical Operators|2300-23FF Miscellaneous Technical|2400-243F Control Pictures|" +
            "2440-245F Optical Character Recognition|2460-24FF Enclosed Alphanumerics|2500-257F Box Drawing|" +
            "2580-259F Block Elements|25A0-25FF Geometric Shapes|2600-26FF Miscellaneous Symbols|2700-27BF Dingbats|" +
            "27C0-27EF Miscellaneous Mathematical Symbols-A|27F0-27FF Supplemental Arrows-A|2800-28FF Braille Patterns|" +
            "2900-297F Supplemental Arrows-B|2980-29FF Miscellaneous Mathematical Symbols-B|" +
            "2A00-2AFF Supplemental Mathematical Operators|2B00-2BFF Miscellaneous Symbols and Arrows|" +
            "2C00-2C5F Glagolitic|2C60-2C7F Latin Extended-C|2C80-2CFF Coptic|2D00-2D2F Georgian Supplement|" +
            "2D30-2D7F Tifinagh|2E00-2E7F Supplemental Punctuation|2E80-2EFF CJK Radicals Supplement|" +
            "2F00-2FDF Kangxi Radicals|3000-303F CJK Symbols and Punctuation|3040-309F Hiragana|30A0-30FF Katakana|" +
            "3100-312F Bopomofo|3130-318F Hangul Compatibility Jamo|31F0-31FF Katakana Phonetic Extensions|" +
            "3200-32FF Enclosed CJK Letters and Months|3300-33FF CJK Compatibility|3400-4DBF CJK Extension A|" +
            "4DC0-4DFF Yijing Hexagram Symbols|4E00-9FFF CJK Unified Ideographs|A000-A48F Yi Syllables|" +
            "A720-A7FF Latin Extended-D|AC00-D7AF Hangul Syllables|E000-F8FF Private Use Area|" +
            "F900-FAFF CJK Compatibility Ideographs|FB00-FB4F Alphabetic Presentation Forms|" +
            "FB50-FDFF Arabic Presentation Forms-A|FE00-FE0F Variation Selectors|FE20-FE2F Combining Half Marks|" +
            "FE30-FE4F CJK Compatibility Forms|FE50-FE6F Small Form Variants|FE70-FEFF Arabic Presentation Forms-B|" +
            "FF00-FFEF Halfwidth and Fullwidth Forms|FFF0-FFFF Specials|1D400-1D7FF Mathematical Alphanumeric Symbols|" +
            "1F000-1F02F Mahjong Tiles|1F300-1F5FF Miscellaneous Symbols and Pictographs|1F600-1F64F Emoticons|" +
            "1F680-1F6FF Transport and Map Symbols|1F900-1F9FF Supplemental Symbols and Pictographs|" +
            "20000-2A6DF CJK Extension B";

        sealed class Block { public int Start, End; public string Name; }
        static readonly List<Block> Blocks = Parse();

        static List<Block> Parse()
        {
            var list = new List<Block>();
            foreach (var item in Table.Split('|'))
            {
                int sp = item.IndexOf(' ');
                var range = item.Substring(0, sp).Split('-');
                list.Add(new Block
                {
                    Start = int.Parse(range[0], NumberStyles.HexNumber),
                    End = int.Parse(range[1], NumberStyles.HexNumber),
                    Name = item.Substring(sp + 1)
                });
            }
            return list;
        }

        public static string NameOf(int cp)
        {
            foreach (var b in Blocks)
                if (cp >= b.Start && cp <= b.End) return b.Name;
            return Other;
        }
    }
}
