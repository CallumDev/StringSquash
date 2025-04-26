using System.Text;

namespace StringSquash;

public static partial class StringSquasher
{
    // Valid UTF8 cannot start with a continuation byte
    const byte StartPacked = 0x80;
    const byte EndPacked = 0xBF;
    // Unused UTF8 bytes
    const byte StartSeq = 0xC0;
    const byte StartUnicode = 0xC1;

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
                writer.PutUInt(1, 1);
                writer.PutByte((byte)lowCode);
            }
            else if (codepoint < 0x7F)
            {
                throw new InvalidOperationException();
            }
            else
            {
                var v = UniTable0.Length + (codepoint - 0x80);
                writer.PutUInt(1, 1);
                writer.PutByte((byte)v);
            }
        }
        else if (codepoint - singleByte < 0x3FFF)
        {
            writer.PutUInt(0, 1);
            writer.PutUInt(1, 1);
            writer.PutUInt((uint)(codepoint - singleByte), 14);
        }
        else
        {
            writer.PutUInt(0, 1);
            writer.PutUInt(0, 1);
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
        TryPack(str, out var bytes);
        return bytes;
    }

    enum ActiveSet
    {
        Lower,
        Upper,
        Set3
    }
    

    /// <summary>
    /// Packs the string into a compressed byte array, and reports if it is smaller than the equivalent UTF8
    /// </summary>
    /// <param name="str">The string to pack</param>
    /// <param name="bytes">The packed byte array</param>
    /// <returns>If the resulting byte array is smaller than a UTF8 encoding</returns>
    public static bool TryPack(string str, out byte[] bytes)
    {
        if (str.Length == 0)
        {
            bytes = [];
            return false;
        }
        
        var bw = new BitWriter(str.Length * 2);

        // Encoder State
        CodingState state = CodingState.Packed;
        int lastCodepoint = 0;
        ActiveSet set = ActiveSet.Lower;
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
                bw.PutUInt((codepoint & 0x40) != 0 ? 1U : 0, 1);
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
                        bw.PutUInt(0, 1);
                    }
                    else
                    {
                        bw.PutUInt(1, 1);
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
                        bw.PutUInt(0, 1);
                    }
                    else
                    {
                        bw.PutUInt(1, 1);
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