using System.Text;

namespace StringSquash.Tests;

public class Tests
{

    [Test]
    public void TestNumberTransition()
    {
        var src = "v0.1#/abcdefghijklmnopqrstuvwxyz";
        var bytes = StringSquasher.Pack(src);
        var unpacked = StringSquasher.Unpack(bytes);
        Assert.That(unpacked, Is.EqualTo(src));
        Assert.Less(bytes.Length, Encoding.UTF8.GetByteCount(src));
    }

    [Test]
    public void TestEmbeddedByte()
    {
        var src = "Abidjan 01, Cote d\u0012Ivoire";
        var bytes = StringSquasher.Pack(src);
        var unpacked = StringSquasher.Unpack(bytes);
        Assert.That(unpacked, Is.EqualTo(src));
        Assert.Less(bytes.Length, Encoding.UTF8.GetByteCount(src));
    }

    [Test]
    public void TestAllCaps()
    {
        var src = "ALICE IN WONDERLAND 12345";
        var bytes = StringSquasher.Pack(src);
        var unpacked = StringSquasher.Unpack(bytes);
        Assert.That(unpacked, Is.EqualTo(src));
        Assert.Less(bytes.Length, Encoding.UTF8.GetByteCount(src));
    }

    [Test]
    public void TestUpperLowerTransition()
    {
        var src = "AEIOUAEIOUabcdefgAEIOUAEIOU";
        var bytes = StringSquasher.Pack(src);
        var unpacked = StringSquasher.Unpack(bytes);
        Assert.That(unpacked, Is.EqualTo(src));
        Assert.Less(bytes.Length, Encoding.UTF8.GetByteCount(src));
    }

    [Test]
    public void TestNumberUpperTransition()
    {
        // current set is number until E in ebook
        var src = "August 12, 2006 [EBook #19033]";
        var bytes = StringSquasher.Pack(src);
        var unpacked = StringSquasher.Unpack(bytes);
        Assert.That(unpacked, Is.EqualTo(src));
        Assert.Less(bytes.Length, Encoding.UTF8.GetByteCount(src));
    }

    [Test]
    public void TestUpperLowerTransition2()
    {
        // has upper->lower transition with 
        // some symbols in-between
        var src = "WONDERLAND ***\n\n\n\n\nProduced";
        var bytes = StringSquasher.Pack(src);
        var unpacked = StringSquasher.Unpack(bytes);
        Assert.That(unpacked, Is.EqualTo(src));
        Assert.Less(bytes.Length, Encoding.UTF8.GetByteCount(src));
    }

    [Test]
    public void TestNumberLowerTransition()
    {
        var src = "1916,\n\n                   by SAM'L";
        var bytes = StringSquasher.Pack(src);
        var unpacked = StringSquasher.Unpack(bytes);
        Assert.That(unpacked, Is.EqualTo(src));
        Assert.Less(bytes.Length, Encoding.UTF8.GetByteCount(src));
    }

    [Test]
    public void TestSequenceTransitions()
    {
        // ' with' is a sequence, make sure the transition occurs correctly from upper case to lower
        var src = "practically ANYTHING with public domain eBooks";
        var bytes = StringSquasher.Pack(src);
        var unpacked = StringSquasher.Unpack(bytes);
        Assert.That(unpacked, Is.EqualTo(src));
        Assert.Less(bytes.Length, Encoding.UTF8.GetByteCount(src));
    }
    


    [Test]
    public void TestUnicodeTransition()
    {
        var src = "根据项目Gutenberg许可证的条款重新使用它";
        var bytes = StringSquasher.Pack(src);
        var unpacked = StringSquasher.Unpack(bytes);
        Assert.That(unpacked, Is.EqualTo(src));
        Assert.Less(bytes.Length, Encoding.UTF8.GetByteCount(src));
    }
}