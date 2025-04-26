// See https://aka.ms/new-console-template for more information

using System.Globalization;
using System.Text;

var file = args.Length > 0 ? args[0] : "codes28.txt";
var codes = File.ReadAllLines(file).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();

Console.WriteLine(@$"static void WriteTree{codes.Length}(BitWriter writer, int index)
{{
    var t = tblTree{codes.Length}[index];
    writer.PutUInt(t.Code, t.Length);
}}");

Console.WriteLine($"static readonly CodeTree[] tblTree{codes.Length} = [");
for (int i = 0; i < codes.Length; i++)
{
    var len = codes[i].Length;
    var b = byte.Parse(codes[i], NumberStyles.BinaryNumber);
    var value = ReverseBits(b) >> (8 - len);
    Console.Write($"    new({len}, 0x{value:X2})");
    if (i + 1 < codes.Length)
        Console.WriteLine(",");
    else
        Console.WriteLine();
}
Console.WriteLine("];");

var tree = new PrefixTree(codes);

Console.WriteLine($"static int ReadTree{codes.Length}(ref BitReader reader)");
Console.WriteLine("{");
Console.WriteLine(tree.root.Node(1));
Console.WriteLine("}");


static byte ReverseBits(byte b) 
{
    b = (byte)((b & 0xF0) >> 4 | (b & 0x0F) << 4);
    b = (byte)((b & 0xCC) >> 2 | (b & 0x33) << 2);
    b = (byte)((b & 0xAA) >> 1 | (b & 0x55) << 1);
    return b;
}


public class PrefixTreeNode
{
    public int? Index = null;
    public PrefixTreeNode? Left = null;
    public PrefixTreeNode? Right = null;
    
    public string Node(int tabs)
    {
        if (Index != null)
        {
            return $"{I(tabs)} return {Index.Value};";
        }
        else
        {
            var sb = new StringBuilder();
            var l = I(tabs);
            sb.Append(l).AppendLine("if(reader.GetBool())");
            sb.Append(l).AppendLine("{");
            sb.AppendLine(Right!.Node(tabs + 1));
            sb.Append(l).AppendLine("}");
            sb.Append(l).AppendLine("else");
            sb.Append(l).AppendLine("{");
            sb.AppendLine(Left!.Node(tabs + 1));
            sb.Append(l).AppendLine("}");
            return sb.ToString();
        }
    }

    string I(int tabs)
    {
        return new string(' ', tabs * 4);
    }
}

public class PrefixTree
{
    public PrefixTreeNode root = new PrefixTreeNode();

    public PrefixTree(string[] prefixes)
    {
        for (int i = 0; i < prefixes.Length; i++)
        {
            AddPrefix(prefixes[i], i);
        }
    }

    private void AddPrefix(string prefix, int index)
    {
        PrefixTreeNode current = root;
        foreach (char bit in prefix)
        {
            if (bit == '0')
            {
                if (current.Left == null)
                    current.Left = new PrefixTreeNode();
                current = current.Left;
            }
            else if (bit == '1')
            {
                if (current.Right == null)
                    current.Right = new PrefixTreeNode();
                current = current.Right;
            }
        }
        current.Index = index; // Mark the end of the prefix
    }
    
}