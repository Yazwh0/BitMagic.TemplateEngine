using BitMagic.TemplateEngine.Objects;
using System.Text;
using System.Text.RegularExpressions;

namespace BitMagic.TemplateEngine.X16;

public static class CsasmEngine
{
    public static ITemplateEngine CreateEngine() => TemplateEngineBuilder
            .As("csasm")
            // 6502
            .WithUnderlying(new Regex(@"^\s*(?<line>((?i)(adc|and|asl|bcc|bcs|beq|bit|bmi|bne|bpl|brk|bvc|bvs|clc|cld|cli|clv|cmp|cpx|cpy|dec|dex|dey|eor|inc|inx|iny|jmp|jsr|lda|ldx|ldy|lsr|nop|ora|pha|php|pla|plp|rol|ror|rti|rts|sbc|sec|sed|sei|sta|stx|sty|stp|tax|tay|tsx|txa|txs|tya)(\s+.*|)))$", RegexOptions.Compiled))
            // 65c02
            .WithUnderlying(new Regex(@"^\s*(?<line>((?i)(bra|phx|phy|plx|ply|stz|trb|tsb|bbr0|bbr1|bbr2|bbr3|bbr4|bbr5|bbr6|bbr7|bbs0|bbs1|bbs2|bbs3|bbs4|bbs5|bbs6|bbs7|rmb0|rmb1|rmb2|rmb3|rmb4|rmb5|rmb6|rmb7|smb0|smb1|smb2|smb3|smb4|smb5|smb6|smb7|wai|ldd)(\s+.*|)))$", RegexOptions.Compiled))
            // bmasm lines, anything that starts with a . or a ;
            .WithUnderlying(new Regex(@"^\s*(?<line>([\.;].*))$", RegexOptions.Compiled))
            // imbedded csharp, eg lda @( csharp_variable ) - https://stackoverflow.com/questions/17003799/what-are-regular-expression-balancing-groups
            .WithCSharpInline(new Regex(@"(?<csharp>(@[^\s](?:[^\(\)]|(?<open>\()|(?<-open>\)))+(?(open)(?!))\)))", RegexOptions.Compiled), new Regex(@"(@\()(?<csharp>(.*))(\))", RegexOptions.Compiled))
            .WithCSharpRawVariablePrefix("@")
            .WithBeautifier(Beautify)
            //.WithNamespace("BM = BitMagic.TemplateEngine.X16.Helper")
            //.WithAssembly(typeof(Helper).Assembly)
            .Build();

    public static ISourceResult Beautify(ISourceResult input)
    {
        var sb = new StringBuilder();
        var lines = input.Code.Split('\n');
        var indent = 0;
        var label = new Regex(@"^(\.[\w\-_]+\:)", RegexOptions.Compiled);
        var lastBlank = false;
        var map = new List<ISourceResultMap>();
        var idx = 0;

        foreach (var l in lines)
        {
            if (l == null)
                continue;

            var line = l.Trim();

            var addBlank = false;

            if (line.StartsWith(".scope", StringComparison.InvariantCultureIgnoreCase) && !lastBlank)
            {
                sb.AppendLine();
                map.Add(new SourceResultMap(0, ""));
            }

            if (line.StartsWith(".proc", StringComparison.InvariantCultureIgnoreCase) && !lastBlank)
            {
                sb.AppendLine();
                map.Add(new SourceResultMap(0, ""));
            }

            if (line.StartsWith(".endproc", StringComparison.InvariantCultureIgnoreCase))
            {
                addBlank = true;
                indent--;
            }

            if (line.StartsWith(".endscope", StringComparison.InvariantCultureIgnoreCase))
            {
                addBlank = true;
                indent--;
            }

            if (label.IsMatch(line))
            {
                sb.AppendLine();
                map.Add(new SourceResultMap(0, ""));
            }

            if (indent > 0)
                sb.Append('\t', indent);

            sb.AppendLine(line);
            if (input.Map.Length > idx) //                //map.Add(input.Map[idx++]);
                map.Add(input.Map[idx++]);

            if (line.StartsWith(".proc", StringComparison.InvariantCultureIgnoreCase))
                indent++;

            if (line.StartsWith(".scope", StringComparison.InvariantCultureIgnoreCase))
                indent++;

            if (indent < 0)
                indent = 0;

            lastBlank = string.IsNullOrWhiteSpace(line);

            if (addBlank)
            {
                sb.AppendLine();
                map.Add(new SourceResultMap(0, ""));
                lastBlank = true;
            }
        }
        map.Add(new SourceResultMap(0, ""));

        return new SourceResult(sb.ToString(), map.ToArray());
    }
}
