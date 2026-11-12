using System;
using System.Text;

namespace GlyphCraft.Core
{
    public sealed class KeystrokeInfo
    {
        public string Keys;   // e.g. "Alt + 0065"  (or a short "none" message)
        public string Hint;   // one-line explanation
        public bool HasAltCode;
    }

    public static class Keystroke
    {
        static Encoding _ansi;

        static Encoding Ansi
        {
            get
            {
                if (_ansi == null)
                {
                    try
                    {
                        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                        _ansi = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                    }
                    catch { _ansi = Encoding.Latin1; }
                }
                return _ansi;
            }
        }

        /// <summary>
        /// Windows Alt-codes: hold Alt and type 0 + the Windows-1252 code on the numeric keypad.
        /// For symbol fonts (Wingdings etc.) the code is the low byte of the U+F0xx character.
        /// </summary>
        public static KeystrokeInfo For(int cp, bool symbolFont)
        {
            int code = -1;
            if (symbolFont && cp >= 0xF020 && cp <= 0xF0FF) code = cp - 0xF000;
            else if (cp >= 0x20 && cp < 0x7F) code = cp;
            else if (cp >= 0xA0 && cp <= 0xFF) code = cp;
            else if (cp > 0x7F && cp < 0x10000)
            {
                try
                {
                    var bytes = Ansi.GetBytes(new[] { (char)cp });
                    if (bytes.Length == 1 && bytes[0] >= 0x80) code = bytes[0];
                }
                catch { }
            }

            if (code >= 0)
            {
                return new KeystrokeInfo
                {
                    HasAltCode = true,
                    Keys = "Alt + 0" + code.ToString("D3"),
                    Hint = symbolFont
                        ? "Hold Alt, type the digits on the numeric keypad (Num Lock on), with this font active."
                        : "Hold Alt, type the digits on the numeric keypad (Num Lock on), then release Alt."
                };
            }

            string hex = cp.ToString("X4");
            return new KeystrokeInfo
            {
                HasAltCode = false,
                Keys = "No Alt-code",
                Hint = "In Word: type " + hex + " then press Alt + X.  Or copy the character below."
            };
        }
    }
}
