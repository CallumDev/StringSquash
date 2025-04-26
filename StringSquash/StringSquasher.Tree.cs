namespace StringSquash;

static partial class StringSquasher
{
    private static readonly CodeTree[] tblTree28 =
    [
        new(2, 0x00),
        new(3, 0x02),
        new(3, 0x06),
        new(4, 0x01),
        new(4, 0x09),
        new(4, 0x05),
        new(4, 0x0D),
        new(4, 0x03),
        new(5, 0x0B),
        new(5, 0x1B),
        new(6, 0x07),
        new(6, 0x27),
        new(6, 0x17),
        new(7, 0x37),
        new(7, 0x77),
        new(7, 0x0F),
        new(7, 0x4F),
        new(7, 0x2F),
        new(8, 0x6F),
        new(8, 0xEF),
        new(8, 0x1F),
        new(8, 0x9F),
        new(8, 0x5F),
        new(8, 0xDF),
        new(8, 0x3F),
        new(8, 0xBF),
        new(8, 0x7F),
        new(8, 0xFF)
    ];

    private static readonly CodeTree[] tblTree5 =
    [
        new(2, 0x00),
        new(2, 0x02),
        new(2, 0x01),
        new(3, 0x03),
        new(3, 0x07)
    ];

    private static void WriteTree5(BitWriter writer, int index)
    {
        var t = tblTree5[index];
        writer.PutUInt(t.Code, t.Length);
    }

    private static int ReadTree5(ref BitReader reader)
    {
        if (reader.GetBool())
        {
            if (reader.GetBool())
            {
                if (reader.GetBool()) return 4;

                return 3;
            }

            return 2;
        }

        if (reader.GetBool()) return 1;

        return 0;
    }


    private static void WriteTree28(BitWriter writer, int index)
    {
        var t = tblTree28[index];
        writer.PutUInt(t.Code, t.Length);
    }

    private static int ReadTree28(ref BitReader reader)
    {
        if (reader.GetBool())
        {
            if (reader.GetBool())
            {
                if (reader.GetBool())
                {
                    if (reader.GetBool())
                    {
                        if (reader.GetBool())
                        {
                            if (reader.GetBool())
                            {
                                if (reader.GetBool())
                                {
                                    if (reader.GetBool()) return 27;

                                    return 26;
                                }

                                if (reader.GetBool()) return 25;

                                return 24;
                            }

                            if (reader.GetBool())
                            {
                                if (reader.GetBool()) return 23;

                                return 22;
                            }

                            if (reader.GetBool()) return 21;

                            return 20;
                        }

                        if (reader.GetBool())
                        {
                            if (reader.GetBool())
                            {
                                if (reader.GetBool()) return 19;

                                return 18;
                            }

                            return 17;
                        }

                        if (reader.GetBool()) return 16;

                        return 15;
                    }

                    if (reader.GetBool())
                    {
                        if (reader.GetBool())
                        {
                            if (reader.GetBool()) return 14;

                            return 13;
                        }

                        return 12;
                    }

                    if (reader.GetBool()) return 11;

                    return 10;
                }

                if (reader.GetBool())
                {
                    if (reader.GetBool()) return 9;

                    return 8;
                }

                return 7;
            }

            if (reader.GetBool())
            {
                if (reader.GetBool()) return 6;

                return 5;
            }

            if (reader.GetBool()) return 4;

            return 3;
        }

        if (reader.GetBool())
        {
            if (reader.GetBool()) return 2;

            return 1;
        }

        return 0;
    }
}