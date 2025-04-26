using System.Text;
using StringSquash;

if (args.Length >= 2)
{
    if (args[0] == "--decompress")
    {
        var src = File.ReadAllBytes(args[1]);
        Console.Write(StringSquasher.Unpack(src));
    }
    else
    {
        var src = File.ReadAllText(args[0]);
        File.WriteAllBytes(args[1], StringSquasher.Pack(src));
    }
    return;
}

Console.WriteLine("StringSquash Test CLI");
Console.WriteLine("Use [input.txt] [output.bin] to compress");
Console.WriteLine("Use --decompress [file.bin] to decompress to stdout");
Console.WriteLine("Exits on empty string");
string? ln;
Console.Write(">");
while (!string.IsNullOrWhiteSpace(ln = Console.ReadLine()))
{
    var utf8Bytes = Encoding.UTF8.GetBytes(ln);
    var compressed = StringSquasher.TryPack(ln, out var bytes);
    if (!compressed)
    {
        Console.WriteLine($"Did not compress, UTF8: {bytes.Length}");
    }
    else
    {
        Console.WriteLine($"Compressed to packed: {bytes.Length}, utf8: {utf8Bytes.Length}");
    }
    if (StringSquasher.Unpack(bytes) != ln)
    {
        Console.WriteLine("ERROR MISMATCH");
        Console.WriteLine(StringSquasher.Unpack(bytes));
    }
    Console.Write(">");
}