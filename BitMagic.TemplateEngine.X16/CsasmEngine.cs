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

    private static readonly Regex Label = new(@"^(\.[\w\-_]+\:)", RegexOptions.Compiled);

    // Code and directives are indented by the scope and procs they're in, following how the compiler treats them:
    // procs nest, a scope doesn't (a new .scope replaces the current one), and .scope, .segment and .endsegment
    // all go back to the segment's default procedure. The exception is a .scope opened inside a proc, which is
    // indented within it and returns to it at its .endscope. Labels stay at the left.
    public static ISourceResult Beautify(ISourceResult input)
    {
        var sb = new StringBuilder();
        var lines = input.Code.Split('\n');
        var inScope = false;
        var procDepth = 0;
        var baseIndent = 0; // indent of the proc a nested scope was opened in
        var nested = new Stack<(bool inScope, int procDepth, int baseIndent)>();
        var lastBlank = true; // so nothing is inserted before the first line
        var map = new List<ISourceResultMap>();
        var idx = 0;

        void AddBlank()
        {
            sb.AppendLine();
            map.Add(new SourceResultMap(0, ""));
            lastBlank = true;
        }

        foreach (var l in lines)
        {
            if (l == null)
                continue;

            var line = l.Trim();
            var isBlank = line.Length == 0;

            // collapse runs of blank lines, dropping their map entries with them
            if (isBlank && lastBlank)
            {
                idx++;
                continue;
            }

            var isProc = IsDirective(line, ".proc");
            var isEndProc = IsDirective(line, ".endproc");
            var isScope = IsDirective(line, ".scope");
            var isEndScope = IsDirective(line, ".endscope");
            var isLabel = Label.IsMatch(line);

            var isSegment = IsDirective(line, ".segment") || IsDirective(line, ".endsegment");

            if (isEndScope && nested.Count > 0)
            {
                // back to the proc the scope was opened in
                (inScope, procDepth, baseIndent) = nested.Pop();
            }
            else if (isScope && procDepth > 0)
            {
                // a scope opened inside a proc nests, rather than replacing the current one
                nested.Push((inScope, procDepth, baseIndent));
                baseIndent += (inScope ? 1 : 0) + procDepth;
                inScope = false;
                procDepth = 0;
            }
            else if (isScope || isEndScope || isSegment)
            {
                nested.Clear();
                baseIndent = 0;
                inScope = false;
                procDepth = 0;
            }

            if (isEndProc && procDepth > 0)
                procDepth--;

            if ((isProc || isScope || isLabel) && !lastBlank)
                AddBlank();

            var indent = baseIndent + (inScope ? 1 : 0) + procDepth;
            if (!isLabel && !isBlank && indent > 0)
                sb.Append('\t', indent);

            sb.AppendLine(line);
            if (input.Map.Length > idx)
                map.Add(input.Map[idx]);
            idx++;

            lastBlank = isBlank;

            if (isScope)
                inScope = true;

            if (isProc)
                procDepth++;

            if (isEndProc || isEndScope)
                AddBlank();
        }
        map.Add(new SourceResultMap(0, ""));

        return new SourceResult(sb.ToString(), map.ToArray());
    }

    // The line is the directive, rather than one that starts with the same letters. eg .proc, but not .process_data: or .scopedelimiter
    private static bool IsDirective(string line, string directive) =>
        line.StartsWith(directive, StringComparison.InvariantCultureIgnoreCase) &&
        (line.Length == directive.Length || char.IsWhiteSpace(line[directive.Length]) || line[directive.Length] == ';');
}
