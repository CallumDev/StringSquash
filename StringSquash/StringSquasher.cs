using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.VisualBasic.CompilerServices;

namespace StringSquash;

public static partial class StringSquasher
{
    // A valid UTF8 string can never start with the following bytes
    // (0x80-0xBF), 0xC1, 0x2, (0xF5-0xFF)
    const byte StartPacked = 0x80; //start tree pack with 7-bit ascii char
    const byte EndPacked = 0xBF; // 6 bits in header byte, final bit added on
    const byte StartSeq = 0xC0; // start tree pack in sequence
    const byte StartUnicode = 0xC1; // start tree pack in unicode
    //use 0xF5-0xF8 to have 2 bits of idx 0 in header byte
    const byte Start6Bit = 0xF5; //A-Za-z0-9, space, escaped other symbols
    //use F9-FC to have 2 bits of idx 0 in header byte
    const byte Start6BitAlt = 0xF9; //A-Za-z0-9 with space changed to -_,. (2 bit extra)
    const byte Start5BitLower = 0xFD; // 5 bit fixed alphabet [A-Z] -_,.
    const byte Start5BitUpper = 0xFE; // 5 bit fixed alphabet [a-z -_,.]
    const byte Start4BitNumbers = 0xFF; // 4 bit fixed alphabet [0-9] -_,.
    
    // Escape codes for symbols or ending padded string
    // Symbols after escapes are 5 bit indexed
    private const uint Esc4Bit = 15;
    private const uint Esc5Bit = 31;
    private const uint Esc6Bit = 63;

    static readonly ushort[] UniTable0 =
    [
        // Lower codepoints
        0x0, 0x1, 0x2, 0x3, 0x4, 0x5, 0x6, 0x7, 0x8, 0x9,
        0xB, 0xC, 0xE, 0xF, 0x10, 0x11, 0x12, 0x13, 0x14,
        0x15, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x1B, 0x1C,
        0x1D, 0x1E, 0x1F, 0x7F
    ];

    static void WriteCodepoint(BitWriter writer, int codepoint)
    {
        int lowAvailable = 256 - UniTable0.Length;
        int singleByte = 0x80 + lowAvailable;
        if (codepoint < singleByte)
        {
            int lowCode = UniTable0.AsSpan().IndexOf((ushort)codepoint);
            if (lowCode != -1)
            {
                writer.PutBool(true);
                writer.PutByte((byte)lowCode);
            }
            else if (codepoint < 0x7F)
            {
                throw new InvalidOperationException();
            }
            else
            {
                var v = UniTable0.Length + (codepoint - 0x80);
                writer.PutBool(true);
                writer.PutByte((byte)v);
            }
        }
        else if (codepoint - singleByte < 0x3FFF)
        {
            writer.PutBool(false);
            writer.PutBool(true);
            writer.PutUInt((uint)(codepoint - singleByte), 14);
        }
        else
        {
            writer.PutBool(false);
            writer.PutBool(false);
            writer.PutUInt((uint)codepoint, 21);
        }
    }

    static int ReadCodepoint(ref BitReader reader)
    {
        int lowAvailable = 256 - UniTable0.Length;
        int singleByte = 0x80 + lowAvailable;
        if (reader.GetBool())
        {
            var b = reader.GetByte();
            if (b < UniTable0.Length)
            {
                return UniTable0[b];
            }
            return 0x80 + (b - UniTable0.Length);
        }
        else
        {
            if (reader.GetBool())
            {
                return singleByte + (int)reader.GetUInt(14);
            }
            else
            {
                return (int)reader.GetUInt(21);
            }
        }
    }

    enum UnicodeTransition
    {
        Delta,
        None,
        Ascii
    }

    static bool IsPackedCodepoint(int code) => code == '\n' || code == '\r' || code == '\t' ||
                                               (code >= 32 && code < 127);
    
    static UnicodeTransition WriteDeltaCodepoint(BitWriter bw, int codepoint, int lastCodepoint)
    {
        if (IsPackedCodepoint(codepoint))
        {
            bw.PutUInt(6, 3);
            return UnicodeTransition.Ascii;
        }
        if (codepoint == ' ')
        {
            bw.PutUInt(7, 3);
            bw.PutUInt(0, 2);

            return UnicodeTransition.None;
        }
        if (codepoint == ',')
        {
            bw.PutUInt(7, 3);
            bw.PutUInt(1, 2);

            return UnicodeTransition.None;
        }
        if (codepoint == '.')
        {
            bw.PutUInt(7, 3);
            bw.PutUInt(2, 2);

            return UnicodeTransition.None;
        }
        if (codepoint == '\n')
        {
            bw.PutUInt(7, 3);
            bw.PutUInt(3, 2);
            return UnicodeTransition.None;
        }

        var delta = codepoint - lastCodepoint;
        bool neg = delta < 0;
        if (neg) delta = -delta;

        if (delta <= 127)
        {
            bw.PutUInt(neg ? 3U : 0, 3);
            bw.PutUInt((uint)delta, 7);
        }
        else if (delta <= 128 + 16383)
        {
            bw.PutUInt(neg ? 4U : 1, 3);
            bw.PutUInt((uint)(delta - 128), 14);
        }
        else
        {
            bw.PutUInt(neg ? 5U : 2, 3);
            bw.PutUInt((uint)delta, 21);
        }
        return UnicodeTransition.Delta;
    }

    static int ReadDeltaCodepoint(ref BitReader reader, int lastCodepoint, out bool updateLast)
    {
        updateLast = true;
        var desc = reader.GetUInt(3);
        if (desc <= 5)
        {
            bool neg = false;
            if (desc >= 3)
            {
                neg = true;
                desc -= 3;
            }

            int delta = 0;
            switch (desc)
            {
                case 0:
                    delta = (int)reader.GetUInt(7);
                    break;
                case 1:
                    delta = 128 + (int)reader.GetUInt(14);
                    break;
                case 2:
                    delta = (int)reader.GetUInt(21);
                    break;
            }
            if (neg)
                return lastCodepoint - delta;
            else
                return lastCodepoint + delta;
        }
        else if (desc == 6)
        {
            return -1;
        }
        else
        {
            updateLast = false;
            var special = reader.GetUInt(2);
            return special switch
            {
                0 => ' ',
                1 => ',',
                2 => '.',
                3 => '\n',
                _ => throw new InvalidOperationException()
            };
        }
    }
    
    


    enum CodingState
    {
        DeltaUnicode,
        Packed
    }

    record struct CodeTree(int Length, byte Code);

    private static readonly string[] sequences =
    [
        "\": ",   //0 (12 bit encodings)
        "</",     //1
        "=\"",    //2
        "\":\"", //3
        "://", //4
        //5+ = 15 bit encodings
        "https://", "http://", "www.",
        " the ", " and ", "tion", " with" , "ment"
    ];

    static (int Seq, int Len) GetSequence(string src, int index)
    {
        var x = src.AsSpan().Slice(index);
        for (int i = 0; i < sequences.Length; i++)
        {
            if (x.StartsWith(sequences[i]))
            {
                return (i, sequences[i].Length);
            }
        }
        return (-1, -1);
    }

    private const string SetLetters1 = " etaoinsrlcdhupmbgwfyvkqjxz";
    private const string Set2 = "\"{}_<>:\n\0[]\\;'\t@*/?!^|\r~`";
    private const string Set3 = ",.01925-/34678() =&+$%#";
    
    static int GetCodeIndex(char c)
    {
        const string Syms = $"{Set2}{Set3}";
        if (c >= 127)
            return -1;
        if ((c >= 'A' && c <= 'Z') ||
            (c >= 'a' && c <= 'z') ||
            c == ' ')
        {
            return 0;
        }
        if (c == '\r' || c == '\n' || c == '\t')
            return 1;
        if (c < 32)
            return -1;
        return Syms.IndexOf(c) < Set2.Length ? 1 : 2;
    }

    static void EncodeSequenceIndex(BitWriter bw, bool numbersActive, int index)
    {
        if (index <= 3)
        {
            if (!numbersActive)
            {
                WriteTree28(bw, 0);
                WriteTree5(bw, 2);
            }
            //0-3, 23 in set 3
            WriteTree28(bw, 24 + index);
        }
        else if (index == 4)
        {
            WriteTree28(bw, 0);
            WriteTree5(bw, 1);
            WriteTree28(bw, 26);
        }
        else
        {
            //5+ (26 in Set 2)
            WriteTree28(bw, 0);
            WriteTree5(bw, 1);
            WriteTree28(bw, 27);
            bw.PutUInt((uint)(index - 5), 3);
        }
    }

    /// <summary>
    /// Packs the string into a compressed byte array
    /// </summary>
    /// <param name="str">The string to compress</param>
    /// <returns>A byte array able to be decompressed with Unpack</returns>
    public static byte[] Pack(string str)
    {
        TryPack(str, out var bytes, out _);
        return bytes;
    }

    enum ActiveSet
    {
        Lower,
        Upper,
        Set3
    }

    const string SetSixBit = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 ";
    const string SetSuppChar = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";
    
    [Flags]
    enum FixedCodingSet
    {
        None,
        UpperLetters = (1 << 0),
        LowerLetters = (1 << 1),
        Numbers = (1 << 2),
        MainSetMask = (UpperLetters | LowerLetters | Numbers),
        Space = (1 << 3),
        Underscore = (1 << 4),
        Comma = (1 << 5),
        Dot = (1 << 6),
        Dash = (1 << 7),
        Supplemental = (1 << 8),
        SymbolsMask = (Space | Underscore | Comma | Dot | Dash | Supplemental),
        Invalid = (1 << 10),
    }
    static void UpdateFixedSet(ref FixedCodingSet set, ref int supCount, ref int nsCount, int codepoint)
    {
        if (codepoint >= 'A' && codepoint <= 'Z')
        {
            set |= FixedCodingSet.UpperLetters;
        }
        else if (codepoint >= 'a' && codepoint <= 'z')
        {
            set |= FixedCodingSet.LowerLetters;
        }
        else if (codepoint >= '0' && codepoint <= '9')
        {
            set |= FixedCodingSet.Numbers;
        }
        else if (codepoint == ' ')
        {
            set |= FixedCodingSet.Space;
        }
        else if (codepoint == '_')
        {
            set |= FixedCodingSet.Underscore;
            nsCount++;
        }
        else if (codepoint == ',')
        {
            set |= FixedCodingSet.Comma;
            nsCount++;
        }
        else if (codepoint == '.')
        {
            set |= FixedCodingSet.Dot;
            nsCount++;
        }
        else if (codepoint == '-')
        {
            set |= FixedCodingSet.Dash;
            nsCount++;
        }
        else if (SetSuppChar.Contains((char)codepoint))
        {
            set |= FixedCodingSet.Supplemental;
            supCount++;
        }
        else
        {
            set |= FixedCodingSet.Invalid;
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Fixed4BitLength(int strlen, int suppCount) =>
        1 + (((strlen * 4) + (suppCount * 5) + 7) >> 3);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Fixed5BitLength(int strlen, int suppCount) =>
        1 + (((strlen * 5) + (suppCount * 5) + 7) >> 3);

    static int Fixed6BitLength(int strlen, int suppCount, int pad = 0) =>
        1 + ((((strlen - 1) * 6 + 4 + (suppCount * 5) + pad) + 7) >> 3);

    static void Write4Bits(BitWriter bw, char startChar, byte marker, string str)
    {
        bw.Clear();
        bw.PutByte(marker);
        for (int i = 0; i < str.Length; i++)
        {
            switch (str[i])
            {
                case ' ':
                    bw.PutUInt(0, 4);
                    break;
                case ',':
                    bw.PutUInt(1, 4);
                    break;
                case '.':
                    bw.PutUInt(2, 4);
                    break;
                case '_':
                    bw.PutUInt(3, 4);
                    break;
                case '-':
                    bw.PutUInt(4, 4);
                    break;
                default:
                    var idxS = SetSuppChar.IndexOf(str[i]);
                    if (idxS != -1)
                    {
                        bw.PutUInt(Esc4Bit, 4);
                        bw.PutUInt((uint)idxS, 5);
                    }
                    else
                    {
                        bw.PutUInt(5 + (uint)(str[i] - startChar), 4);
                    }
                    break;
            }
        }
        if (bw.Padding() >= 4)
        {
            bw.PutUInt(Esc4Bit, 4);
        }
    }
    
    static void Write5Bits(BitWriter bw, char startChar, byte marker, string str)
    {
        bw.Clear();
        bw.PutByte(marker);
        for (int i = 0; i < str.Length; i++)
        {
            switch (str[i])
            {
                case ' ':
                    bw.PutUInt(0, 5);
                    break;
                case ',':
                    bw.PutUInt(1, 5);
                    break;
                case '.':
                    bw.PutUInt(2, 5);
                    break;
                case '_':
                    bw.PutUInt(3, 5);
                    break;
                case '-':
                    bw.PutUInt(4, 5);
                    break;
                default:
                    var idxS = SetSuppChar.IndexOf(str[i]);
                    if (idxS != -1)
                    {
                        bw.PutUInt(Esc5Bit, 5);
                        bw.PutUInt((uint)idxS, 5);
                    }
                    else
                    {
                        bw.PutUInt(5 + (uint)(str[i] - startChar), 5);
                    }
                    break;
            }
        }
        if (bw.Padding() >= 5)
        {
            bw.PutUInt(Esc5Bit, 5);
        }
    }
    
    
    static void Read4Bits(ref BitReader br, char startChar, StringBuilder sb)
    {
        while (br.BitsLeft >= 4)
        {
            var v = br.GetUInt(4);
            if (v == Esc4Bit)
            {
                if (br.BitsLeft >= 5)
                {
                    sb.Append(SetSuppChar[(int)br.GetUInt(5)]);
                }
            }
            else
            {
                sb.Append(v switch
                {
                    0 => ' ',
                    1 => ',',
                    2 => '.',
                    3 => '_',
                    4 => '-',
                    _ => (char)(startChar + (v - 5))
                });
            }
        }
    }

    static void Read5Bits(ref BitReader br, char startChar, StringBuilder sb)
    {
        while (br.BitsLeft >= 5)
        {
            var v = br.GetUInt(5);
            if (v == Esc5Bit)
            {
                if (br.BitsLeft >= 5)
                {
                    sb.Append(SetSuppChar[(int)br.GetUInt(5)]);
                }
            }
            else
            {
                sb.Append(v switch
                {
                    0 => ' ',
                    1 => ',',
                    2 => '.',
                    3 => '_',
                    4 => '-',
                    _ => (char)(startChar + (v - 5))
                });
            }
        }
    }
    

    /// <summary>
    /// Packs the string into a compressed byte array, and reports if it is smaller than the equivalent UTF8
    /// </summary>
    /// <param name="str">The string to pack</param>
    /// <param name="bytes">The packed byte array</param>
    /// <param name="squashMethod">The method used to pack the array</param>
    /// <returns>If the resulting byte array is smaller than a UTF8 encoding</returns>
    public static bool TryPack(string str, out byte[] bytes, out SquashMethod squashMethod)
    {
        if (str.Length == 0)
        {
            bytes = [];
            squashMethod = SquashMethod.None;
            return false;
        }
        
        var bw = new BitWriter(str.Length * 2);

        // Encoder State
        CodingState state = CodingState.Packed;
        int lastCodepoint = 0;
        ActiveSet set = ActiveSet.Lower;
        FixedCodingSet fixedSets = FixedCodingSet.None;
        int fixedSuppCount = 0;
        int fixedNonSpaceCount = 0;
        squashMethod = SquashMethod.TreeSquash;
        int startIndex = 1;
        //
        int CheckRep(int i)
        {
            int repCount = 0;
            for (int j = i + 1; j < str.Length; j++)
            {
                if (str[j] == str[i])
                {
                    repCount++;
                    if (repCount == 30)
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }
            if (repCount >= 3)
            {
                WriteTree28(bw,0);
                WriteTree5(bw, 4);
                WriteTree28(bw, repCount - 3);
                return repCount;
            }
            return 0;
        }
        // Encode first byte
        {
            var (seq, seqLen) = GetSequence(str, 0);
            var codepoint = char.ConvertToUtf32(str, 0);
            UpdateFixedSet(ref fixedSets, ref fixedNonSpaceCount, ref fixedSuppCount, codepoint);
            if (seq != -1)
            {
                bw.PutByte(StartSeq);
                WriteTree28(bw, seq);
                startIndex = seqLen;
            }
            else if (codepoint >= 127)
            {
                bw.PutByte(StartUnicode);
                lastCodepoint = codepoint;
                if (char.IsHighSurrogate(str[0]))
                    startIndex++;
                WriteCodepoint(bw, codepoint);
                state = CodingState.DeltaUnicode;
            }
            else
            {
                bw.PutByte((byte)(StartPacked + (codepoint & 0x3F)));
                bw.PutBool((codepoint & 0x40) != 0);
                var rep = CheckRep(0);
                if (rep > 0)
                {
                    startIndex += rep;
                }

                set = IsAsciiDigit(str[0]) ? ActiveSet.Set3 : ActiveSet.Lower;
            }
        }
        
        // Encode the rest of the string!
        
        for (int i = startIndex; i < str.Length; i++)
        {
            var codepoint = char.ConvertToUtf32(str, i);
            UpdateFixedSet(ref fixedSets, ref fixedNonSpaceCount, ref fixedSuppCount, codepoint);
            void WritePacked()
            {
                if (str.Length > i + 1 && str[i] == '\r' && str[i + 1] == '\n')
                {
                    WriteTree28(bw, 0);
                    WriteTree5(bw, 1);
                    WriteTree28(bw, Set2.IndexOf('\0'));
                    i++;
                    return;
                }
                var idx = GetCodeIndex(str[i]);
                if (str[i] == ' ')
                {
                    // Avoid a set transition on space characters
                    if (set == ActiveSet.Set3)
                    {
                        // Space exists in the numbers set also
                        WriteTree28(bw, 1 + Set3.IndexOf(' '));
                    }
                    else
                    {
                        WriteTree28(bw, 1 + SetLetters1.IndexOf(' '));
                    }
                }
                else if (idx == 0)
                {
                    if (char.IsUpper(str[i]))
                    {
                        if (set == ActiveSet.Upper)
                        {
                            WriteTree28(bw, 1 + SetLetters1.IndexOf(char.ToLowerInvariant(str[i])));
                        }
                        else if(i + 2 < str.Length && 
                                char.IsAsciiLetterUpper(str[i + 1]) &&
                                char.IsAsciiLetterUpper(str[i + 2]))
                        {
                            WriteTree28(bw, 0);
                            WriteTree5(bw, 0);
                            WriteTree28(bw, 0);
                            WriteTree28(bw, 1 + SetLetters1.IndexOf(char.ToLowerInvariant(str[i])));
                            set = ActiveSet.Upper;
                        }
                        else
                        {
                            WriteTree28(bw, 0);
                            WriteTree5(bw, set == ActiveSet.Set3 ? 2 : 0);
                            WriteTree28(bw, 1 + SetLetters1.IndexOf(char.ToLowerInvariant(str[i])));
                            set = ActiveSet.Lower;
                        }
                    }
                    else
                    {
                        if (set != ActiveSet.Lower)
                        {
                            WriteTree28(bw, 0);
                            WriteTree5(bw, 0);
                        }
                        WriteTree28(bw, 1 + SetLetters1.IndexOf(str[i]));
                        set = ActiveSet.Lower;
                    }
                }
                else if (idx == 1)
                {
                    WriteTree28(bw, 0);
                    WriteTree5(bw, 1);
                    WriteTree28(bw, Set2.IndexOf(str[i]));
                }
                else if (idx == 2)
                {
                    if (set != ActiveSet.Set3)
                    {
                        WriteTree28(bw, 0);
                        WriteTree5(bw, 2);
                        WriteTree28(bw, Set3.IndexOf(str[i]));
                        if (IsAsciiDigit(str[i]))
                        {
                            set = ActiveSet.Set3;
                        }
                    }
                    else
                    {
                        WriteTree28(bw, 1 + Set3.IndexOf(str[i]));
                    }
                }
                else if (codepoint < 32)
                {
                    WriteTree28(bw, 0);
                    WriteTree5(bw, 3);
                    if (i + 1 >= str.Length || IsPackedCodepoint(str[i + 1]))
                    {
                        bw.PutBool(false);
                    }
                    else
                    {
                        bw.PutBool(true);
                        state = CodingState.DeltaUnicode;
                    }
                    WriteCodepoint(bw, codepoint);
                    lastCodepoint = codepoint;
                }
                else
                {
                    throw new InvalidOperationException();
                }
                var rep = CheckRep(i);
                if (rep > 0)
                {
                    i += rep;
                }
            }
            
            if (codepoint >= 127 || state == CodingState.DeltaUnicode)
            {
                if (state == CodingState.Packed)
                {
                    WriteTree28(bw, 0);
                    WriteTree5(bw, 3);
                    if (i + 1 >= str.Length || IsPackedCodepoint(str[i + 1]))
                    {
                        bw.PutBool(false);
                    }
                    else
                    {
                        bw.PutBool(true);
                        state = CodingState.DeltaUnicode;
                    }
                    WriteCodepoint(bw, codepoint);
                    lastCodepoint = codepoint;
                }
                else
                {
                    var tr = WriteDeltaCodepoint(bw, codepoint, lastCodepoint);
                    if (tr == UnicodeTransition.Ascii)
                    {
                        state = CodingState.Packed;
                        WritePacked();
                    }
                    else if (tr == UnicodeTransition.Delta)
                    {
                        lastCodepoint = codepoint;
                    }
                }
            }
            else // Packed mode
            {
                var (seq, seqLen) = GetSequence(str, i);
                if (seq != -1)
                {
                    EncodeSequenceIndex(bw, set == ActiveSet.Set3, seq);
                    i += (seqLen - 1);
                }
                else
                {
                    WritePacked();
                }
            }
            if (char.IsHighSurrogate(str[i]))
                i++;
        }
        // Pad out final bytes
        if (state == CodingState.DeltaUnicode && bw.Padding() >= 3)
        {
            bw.PutUInt(6, 3);
            state = CodingState.Packed;
        }
        if (state == CodingState.Packed && bw.Padding() > 2)
        {
            WriteTree28(bw, 0);
            WriteTree5(bw, 1);
            if (bw.Padding() >= 2)
            {
                WriteTree28(bw, 25);
            }
        }

        if ((fixedSets & FixedCodingSet.Invalid) != FixedCodingSet.Invalid)
        {
            switch ((fixedSets & FixedCodingSet.MainSetMask))
            {
                case 0:
                case FixedCodingSet.LowerLetters:
                {
                    if (Fixed5BitLength(str.Length, fixedSuppCount) < bw.ByteLength)
                    {
                        Write5Bits(bw, 'a', Start5BitLower, str);
                        squashMethod = SquashMethod.Fixed5;
                    }
                    break;
                }
                case FixedCodingSet.UpperLetters:
                {
                    if (Fixed5BitLength(str.Length, fixedSuppCount) < bw.ByteLength)
                    {
                        Write5Bits(bw, 'A', Start5BitUpper, str);
                        squashMethod = SquashMethod.Fixed5;
                    }
                    break;
                }
                case FixedCodingSet.Numbers:
                {
                    if (Fixed4BitLength(str.Length, fixedSuppCount) < bw.ByteLength)
                    {
                        Write4Bits(bw, '0', Start4BitNumbers, str);
                        squashMethod = SquashMethod.Fixed4;
                    }
                    break;
                }
                default:
                {
                    var symbols = (fixedSets & FixedCodingSet.SymbolsMask);
                    if ((symbols == FixedCodingSet.Dash ||
                              symbols == FixedCodingSet.Dot ||
                              symbols == FixedCodingSet.Underscore ||
                              symbols == FixedCodingSet.Comma)
                             && Fixed6BitLength(str.Length, 0, 2) < bw.ByteLength)
                    {
                        var spIdx = (uint)SetSixBit.IndexOf(' ');
                        (char special, uint ident) = symbols switch
                        {
                            FixedCodingSet.Dash => ('-', 0U),
                            FixedCodingSet.Dot => ('.', 1U),
                            FixedCodingSet.Underscore => ('_', 2U),
                            FixedCodingSet.Comma => (',', 3U),
                            _ => throw new InvalidOperationException(),
                        };
                        bw.Clear();
                        var firstIndex = str[0] == special ? spIdx : (uint)SetSixBit.IndexOf(str[0]);
                        bw.PutByte((byte)(Start6BitAlt + (firstIndex & 0x3)));
                        bw.PutUInt(ident, 2);
                        bw.PutUInt((uint)(firstIndex >> 2), 4);
                        for (int i = 1; i < str.Length; i++)
                        {
                            bw.PutUInt(str[i] == special ? spIdx : (uint)SetSixBit.IndexOf(str[i]), 6);
                        }
                        if (bw.Padding() >= 6)
                        {
                            bw.PutUInt(63U, 6);
                        }
                        squashMethod = SquashMethod.Fixed6;
                    }
                    else if (Fixed6BitLength(str.Length, fixedSuppCount + fixedNonSpaceCount) < bw.ByteLength)
                    {
                        // We've tried fixed tree encoding, but we have a smaller
                        // 6-bit ascii version.
                        bw.Clear();

                        var firstIndex = SetSixBit.IndexOf(str[0]);
                        if (firstIndex == -1)
                        {
                            bw.PutByte((byte)(Start6Bit + (Esc6Bit & 0x3)));
                            bw.PutUInt((uint)(Esc6Bit >> 2), 4);
                            bw.PutUInt((uint)(SetSuppChar.IndexOf(str[0])), 5);
                        }
                        else
                        {
                            bw.PutByte((byte)(Start6Bit + (firstIndex & 0x3)));
                            bw.PutUInt((uint)(firstIndex >> 2), 4);
                        }
                        
                        for (int i = 1; i < str.Length; i++)
                        {
                            var chIdx = SetSixBit.IndexOf(str[i]);
                            if (chIdx == -1)
                            {
                                bw.PutUInt(Esc6Bit, 6);
                                bw.PutUInt((uint)SetSuppChar.IndexOf(str[i]), 5);
                            }
                            else
                            {
                                bw.PutUInt((uint)chIdx, 6);
                            }
                        }
                        if (bw.Padding() >= 6)
                        {
                            bw.PutUInt(Esc6Bit, 6);
                        }
                        squashMethod = SquashMethod.Fixed6;
                    }
                    break;
                }
            }
        }
        if (bw.ByteLength < str.Length)
        {
            // We know we are smaller than the smallest UTF-8 (1 byte/char),
            // no need to scan string again.
            bytes = bw.GetCopy();
            return true;
        }
        var plainUtf8 = Encoding.UTF8.GetBytes(str);
        if (bw.ByteLength >= plainUtf8.Length)
        {
            bytes = plainUtf8;
            squashMethod = SquashMethod.None;
            return false;
        }
        bytes = bw.GetCopy();
        return true;
    }

    static bool IsAsciiDigit(char c)
    {
        return c >= '0' && c <= '9';
    }
    
    /// <summary>
    /// Unpacks a StringSquasher packed array to a string
    /// </summary>
    /// <param name="data">The array to unpack</param>
    /// <returns>The unpacked string</returns>
    public static string Unpack(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            return "";
        if (data.Length == 1)
            return Encoding.UTF8.GetString(data);
        
        var reader = new BitReader(data, 8);
        CodingState state = CodingState.Packed;
        ActiveSet set = ActiveSet.Lower;
        var sb = new StringBuilder(data.Length / 2);
        int lastCodepoint = 0;
        
        // Parse header
        if (data[0] >= StartPacked && data[0] <= EndPacked)
        {
            var ch = (data[0] - StartPacked);
            if (reader.GetBool())
                ch |= 0x40;
            sb.Append((char)ch);
            if (IsAsciiDigit((char)ch))
                set = ActiveSet.Set3;
        }
        else if (data[0] == StartSeq)
        {
            int sym = ReadTree28(ref reader);
            sb.Append(sequences[sym]);
        }
        else if (data[0] == StartUnicode)
        {
            state = CodingState.DeltaUnicode;
            lastCodepoint = ReadCodepoint(ref reader);
            sb.Append(char.ConvertFromUtf32(lastCodepoint));
        }
        else if (data[0] >= Start6Bit &&
                 data[0] < Start5BitLower)
        {
            var spIdx = (uint)SetSixBit.IndexOf(' ');
            char special = ' ';
            uint lowerBits = (uint)(data[0] - Start6Bit);
            if (data[0] >= Start6BitAlt)
            {
                special = reader.GetUInt(2) switch
                {
                    0 => '-',
                    1 => '.',
                    2 => '_',
                    3 => ',',
                    _ => throw new InvalidOperationException(),
                };
                lowerBits = (uint)(data[0] - Start6BitAlt);
            }
            var idx0 = reader.GetUInt(4) << 2 | lowerBits;
            if (idx0 == Esc6Bit)
            {
                sb.Append(SetSuppChar[(int)reader.GetUInt(5)]);
            }
            else
            {
                sb.Append(idx0 == spIdx ? special : SetSixBit[(int)idx0]);
            }
            while (reader.BitsLeft >= 6)
            {
                var idx = reader.GetUInt(6);
                if (idx == Esc6Bit)
                {
                    if (reader.BitsLeft >= 5)
                    {
                        sb.Append(SetSuppChar[(int)reader.GetUInt(5)]);
                    }
                }
                else
                {
                    sb.Append(idx == spIdx ? special : SetSixBit[(int)idx]);
                }
            }
            return sb.ToString();
        }
        else if (data[0] == Start5BitLower)
        {
            Read5Bits(ref reader, 'a', sb);
            return sb.ToString();
        }
        else if (data[0] == Start5BitUpper)
        {
            Read5Bits(ref reader, 'A', sb);
            return sb.ToString();
        }
        else if (data[0] == Start4BitNumbers)
        {
            Read4Bits(ref reader, '0', sb);
            return sb.ToString();
        }
        else
        {
            return Encoding.UTF8.GetString(data);
        }
        while (reader.BitsLeft > 2)
        {
            if (state == CodingState.DeltaUnicode)
            {
                if (reader.BitsLeft < 3)
                    break;
                var newCodepoint = ReadDeltaCodepoint(ref reader, lastCodepoint, out var update);
                if (update)
                {
                    lastCodepoint = newCodepoint;
                }
                if (newCodepoint != -1)
                {
                    sb.Append(char.ConvertFromUtf32(newCodepoint));
                }
                else
                {
                    state = CodingState.Packed;
                }
            }
            else
            {
                if (reader.BitsLeft < 2)
                    break;
                var code0 = ReadTree28(ref reader);
                if (code0 == 0)
                {
                    // We should switch
                    if (reader.BitsLeft < 2)
                    {
                        break;
                    }
                    var switchTo = ReadTree5(ref reader);
                    if (switchTo == 0)
                    {
                        var sym = ReadTree28(ref reader);
                        if (sym == 0)
                        {
                            set = ActiveSet.Upper;
                        }
                        else
                        {
                            if (set != ActiveSet.Lower)
                            {
                                // Transition to lower case
                                sb.Append(SetLetters1[sym - 1]);
                            }
                            else
                            {
                                // Temporary upper case
                                sb.Append(char.ToUpperInvariant(SetLetters1[sym - 1]));
                            }
                            set = ActiveSet.Lower;
                        }
                    }
                    else if (switchTo == 1) // Set2 Symbols
                    {
                        if (reader.BitsLeft < 2)
                        {
                            break;
                        }
                        var idx = ReadTree28(ref reader);
                        if (idx == 25)
                        {
                            break;
                        }
                        if (idx == 26)
                        {
                            sb.Append(sequences[4]);
                        }
                        else if (idx == 27)
                        {
                            var seqIdx = 5 + reader.GetUInt(3);
                            sb.Append(sequences[seqIdx]);
                        }
                        else
                        {
                            var sym = Set2[idx];
                            if (sym == '\0')
                                sb.Append("\r\n");
                            else
                                sb.Append(sym);
                        }
                    }
                    else if (switchTo == 2 && set != ActiveSet.Set3)
                    {
                        var sym = ReadTree28(ref reader);
                        if (sym >= 24)
                        {
                            sb.Append(sequences[sym - 24]);
                        }
                        else
                        {
                            sb.Append(Set3[sym]);
                            if (IsAsciiDigit(sb[^1]))
                                set = ActiveSet.Set3;
                        }
                    }
                    else if (switchTo == 2 && set == ActiveSet.Set3)
                    {
                        sb.Append(char.ToUpperInvariant(SetLetters1[ReadTree28(ref reader) - 1]));
                        set = ActiveSet.Lower;
                    }
                    else if (switchTo == 3)
                    {
                        if (reader.GetBool())
                            state = CodingState.DeltaUnicode;
                        lastCodepoint = ReadCodepoint(ref reader);
                        sb.Append(char.ConvertFromUtf32(lastCodepoint));
                    }
                    else if (switchTo == 4)
                    {
                        var count = 3 + ReadTree28(ref reader);
                        var c = sb[^1];
                        while (count > 0)
                        {
                            sb.Append(c);
                            count--;
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException();
                    }
                }
                else
                {
                    if (set == ActiveSet.Lower)
                    {
                        sb.Append(SetLetters1[code0 - 1]);
                    }
                    else if (set == ActiveSet.Upper)
                    {
                        sb.Append(char.ToUpperInvariant(SetLetters1[code0 - 1]));
                    }
                    else if (code0 >= 24)
                    {
                        sb.Append(sequences[code0 - 24]);
                    }
                    else
                    {
                        sb.Append(Set3[code0 - 1]);
                    }
                }
            }
        }
        
        return sb.ToString();
    }
}