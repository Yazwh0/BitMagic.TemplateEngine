using BitMagic.TemplateEngine.Objects;
using System.Text;

namespace BitMagic.TemplateEngine.X16;

public static class Helper
{
    public static void Bytes(IEnumerable<int> bytes, int width = 16) => Bytes(bytes.Select(i => (byte)i), width);

    public static void Bytes(IEnumerable<sbyte> bytes, int width = 16) => Bytes(bytes.Select(i => unchecked((byte)i)), width);

    public static void Bytes(IEnumerable<byte> bytes, int width = 16)
    {
        StringBuilder sb = new StringBuilder();
        var cnt = 0;
        var first = true;
        foreach (var i in bytes)
        {
            if (first)
            {
                sb.Append(".byte\t");
                first = false;
            }
            else
            {
                sb.Append(", ");
            }

            sb.Append($"${i:X2}");
            cnt++;
            if (cnt == width)
            {
                sb.AppendLine();
                cnt = 0;
                first = true;
            }
        }

        Template.WriteLiteral(sb.ToString());
    }

    public static void Bytes(string stringData)
        => Bytes(stringData.ToCharArray().Select(i => (byte)i));

    public static void Words(IEnumerable<ushort> words, int width = 16)
    {
        StringBuilder sb = new StringBuilder();
        var cnt = 0;
        var first = true;
        foreach (var i in words)
        {
            if (first)
            {
                sb.Append(".word\t");
                first = false;
            }
            else
            {
                sb.Append(", ");
            }

            sb.Append($"${i:X4}");
            cnt++;
            if (cnt == width)
            {
                sb.AppendLine();
                cnt = 0;
                first = true;
            }
        }

        Template.WriteLiteral(sb.ToString());
    }

    public static void Words(IEnumerable<short> words, int width = 16)
    {
        StringBuilder sb = new StringBuilder();
        var cnt = 0;
        var first = true;
        foreach (var i in words)
        {
            if (first)
            {
                sb.Append(".word\t");
                first = false;
            }
            else
            {
                sb.Append(", ");
            }

            sb.Append($"${i:X4}");
            cnt++;
            if (cnt == width)
            {
                sb.AppendLine();
                cnt = 0;
                first = true;
            }
        }

        Template.WriteLiteral(sb.ToString());
    }

    //    .byte $0C, $08              ; $080C - pointer to next line of BASIC code
    //    .byte $0A, $00              ; 2-byte line number($000A = 10)
    //    .byte $9E                   ; SYS BASIC token
    //    .byte $20                   ; [space]
    //    .byte $32, $30, $36, $34    ; $32="2",$30="0",$36="6",$34="4"
    //    .byte $00                   ; End of Line
    //    .byte $00, $00              ; This is address $080C containing
    //                                ; 2-byte pointer to next line of BASIC code
    //                                ; ($0000 = end of program)
    //    .byte $00, $00              ; Padding so code starts at $0810
    public static void X16Header() => Bytes(new byte[] { 0x0c, 0x08, 0x0a, 0x00, 0x9e, 0x20, 0x32, 0x30, 0x36, 0x34, 0x00, 0x00, 0x00, 0x00, 0x00 });

    public static void Petscii(string input, bool addNullTermination = true) => Bytes(StringToPetscii(input, addNullTermination));

    public static IEnumerable<byte> StringToPetscii(string input, bool addNullTermination = true)
    {
        for(var i = 0; i < input.Length; i++)
        {
            yield return (byte)input[i];
        }

        if (addNullTermination)
            yield return 0x00;
    }

    public static void IsoPetscii(string input, bool addNullTermination = true) => Bytes(IsoStringToPetscii(input, addNullTermination));

    private static IEnumerable<byte> IsoStringToPetscii(string input, bool addNullTermination = true)
    {
        for(var i = 0; i < input.Length; i++)
        {
            var val = (byte)input[i];

            if (val >= 0x40 && val < 0x60)
                val -= 0x40;

            yield return val;
        }

        if (addNullTermination)
            yield return 0x00;
    }
}
