![StringSquash](StringSquash/logo.png)

Small string compressor targeting .NET 8.0

Guaranteed to not exceed the size of the original string encoded as UTF-8. The compression method here is heavily based on the one described in the [Unishox2](https://github.com/siara-cc/Unishox2) paper, though it omits dictionary and template modes.

Usage:

```csharp
byte[] compressed = StringSquasher.Pack("Hello World!");
string uncompressed = StringSquasher.Unpack(compressed);

Console.WriteLine(compressed);
// OUTPUT: Hello World!
```

See StringSquashCLI for an interactive demo with statistics for compression

Example:

```
StringSquash Test CLI
Use [input.txt] [output.bin] to compress
Use --decompress [file.bin] to decompress to stdout
Exits on empty string
>Hello World!
Compressed to packed: 10, utf8: 12
>癩蛤蟆想吃天鵝肉 - out of your league
Compressed to packed: 35, utf8: 45
```