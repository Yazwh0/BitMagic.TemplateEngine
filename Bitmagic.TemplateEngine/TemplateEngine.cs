using BitMagic.TemplateEngine.Objects;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace BitMagic.TemplateEngine
{
    public interface ITemplateEngine
    {
        string TemplateName { get; }
        ProcessResult Process(IEnumerable<string> lines, int startLine, string sourceFileName, bool isLibrary);
        ISourceResult Beautify(ISourceResult input);
        public IEnumerable<string> Namespaces { get; }
        public IEnumerable<Assembly> Assemblies { get; }
    }

    public sealed record class ProcessResult(string Code, IList<int> Map);

    public class TemplateEngine : ITemplateEngine
    {
        private readonly Regex[] _lineParsers;
        private readonly (Regex Search, Regex Substitute)[] _inLineCSharp;
        private readonly Func<ISourceResult, ISourceResult> _beautify;
        private readonly string[] _namespaces;
        private readonly Assembly[] _assemblies;
        private readonly string[] _variablePrefixes;

        public string TemplateName { get; }
        public bool RequiresTidyup { get; }
        public string TidyMarker { get; }
        public IEnumerable<string> Namespaces => _namespaces;
        public IEnumerable<Assembly> Assemblies => _assemblies;

        internal TemplateEngine(string name, IEnumerable<Regex> lineParsers, IEnumerable<(Regex Search, Regex Substitute)> inLineParsers,
            Func<ISourceResult, ISourceResult> beautify, IEnumerable<string> namespaces, IEnumerable<Assembly> assemblies, IEnumerable<string> variablePrefixs, bool requiresTidyup = false, string tidyMarker = "")
        {
            TemplateName = name;
            _namespaces = namespaces.ToArray();
            _lineParsers = lineParsers.ToArray();
            _inLineCSharp = inLineParsers.ToArray();
            _variablePrefixes = variablePrefixs.ToArray();

            _assemblies = assemblies.ToArray();
            _beautify = beautify;
            RequiresTidyup = requiresTidyup;
            TidyMarker = tidyMarker;
        }

        public ProcessResult Process(IEnumerable<string> lines, int startLine, string sourceFileName, bool isLibrary)
        {
            int lineNumber = -startLine;// - lineAdust;// + lineAdust; // was +2!

            var sb = new StringBuilder();
            var map = new List<int>();

            foreach (var line in lines)
            {
                bool matched = false;
                foreach(var r in _lineParsers)
                {
                    var result = r.Match(line);

                    if (result.Success)
                    {
                        var match = result.Groups["line"];

                        if (match.Success)
                        {
                            //map.Add(0);
                            //sb.AppendLine($"BitMagic.TemplateEngine.Objects.Template.SetSourceMap(@\"{sourceFileName}\", {lineNumber + lineAdust});");

                            map.Add(lineNumber);
                            sb.AppendLine(ProcessAsmLine(match.Value, lineNumber - 1, sourceFileName));

                            lineNumber++;
                            //sb.AppendLine($"BitMagic.TemplateEngine.Objects.Template.SetSourceMap(@\"{sourceFileName}\", {lineNumber + lineAdust});");
                            //map.Add(0);
                        }
                        // perform change
                        matched = true;
                        break;
                    }
                }

                var raw = line.Trim();
                var prefix = _variablePrefixes.FirstOrDefault(i => raw.StartsWith(i));

                if (prefix != null)
                {
                    map.Add(lineNumber);
                    sb.AppendLine(ProcessVariableLine(raw.Substring(prefix.Length), lineNumber - 1, sourceFileName));

                    matched = true;
                }

                if (!matched)
                {
                    map.Add(lineNumber);
                    sb.AppendLine(line);
                    if (lineNumber == 1 && !isLibrary)
                    {
                        //sb.AppendLine($"BitMagic.TemplateEngine.Objects.Template.SetSourceMap(@\"{sourceFileName}\", {lineNumber + lineAdust});");
                        //map.Add(0);
                    }
                    lineNumber++;
                }
            }

#if DEBUG
            var allText = sb.ToString();
#endif

            return new ProcessResult(sb.ToString(), map);
        }

        public string ProcessAsmLine(string input, int lineNumber, string sourceFile)
        {
            var output = input;

            foreach(var r in _inLineCSharp)
            {
                output = r.Search.Replace(output, 
                    m => r.Substitute.Replace(m.Value, @"{${csharp}}")
                    );
            }

            //output = output.Replace("\"", "\\\"");

            if (RequiresTidyup)
            {
                output = output.Trim();
                if (!string.IsNullOrEmpty(TidyMarker))
                {
                    var idx = output.IndexOf(TidyMarker);
                    if (idx != -1)
                        output = output[(idx + 1)..];
                }
            }
            else
            {
                if (output == ".")
                    output = "";
            }

            return $"BitMagic.TemplateEngine.Objects.Template.WriteLiteral($@\"{output}\", {lineNumber}, @\"{sourceFile}\");";
        }

        public string ProcessVariableLine(string input, int lineNumber, string sourceFile)
        {
            return $"BitMagic.TemplateEngine.Objects.Template.WriteLiteral({input}, {lineNumber}, @\"{sourceFile}\");";
        }

        public ISourceResult Beautify(ISourceResult input) => _beautify(input);
    }
}