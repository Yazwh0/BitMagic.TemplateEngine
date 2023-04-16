using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace BitMagic.TemplateEngine
{
    public interface ITemplateEngine
    {
        string TemplateName { get; }
        string Process(string input);
        string Beautify(string input);
        public IEnumerable<string> Namespaces { get; }
        public IEnumerable<Assembly> Assemblies { get; }
    }

    public class TemplateEngine : ITemplateEngine
    {
        private readonly Regex[] _lineParsers;
        private readonly (Regex Search, Regex Substitute)[] _inLineCSharp;
        private readonly Func<string, string> _beautify;
        private readonly string[] _namespaces;
        private readonly Assembly[] _assemblies;

        public string TemplateName { get; }
        public bool RequiresTidyup { get; }
        public string TidyMarker { get; }
        public IEnumerable<string> Namespaces => _namespaces;
        public IEnumerable<Assembly> Assemblies => _assemblies;

        internal TemplateEngine(string name, IEnumerable<Regex> lineParsers, IEnumerable<(Regex Search, Regex Substitute)> inLineParsers,
            Func<string, string> beautify, IEnumerable<string> namespaces, IEnumerable<Assembly> assemblies, bool requiresTidyup = false, string tidyMarker = "")
        {
            TemplateName = name;
            _namespaces = namespaces.ToArray();
            _lineParsers = lineParsers.ToArray();
            _inLineCSharp = inLineParsers.ToArray();
            _assemblies = assemblies.ToArray();
            _beautify = beautify;
            RequiresTidyup = requiresTidyup;
            TidyMarker = tidyMarker;
        }

        public string Process(string input)
        {
            var lines = input.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var sb = new StringBuilder();

            foreach(var line in lines)
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
                            sb.AppendLine(ProcessAsmLine(match.Value));
                        }
                        // perform change
                        matched = true;
                    } 
                }

                if (!matched)
                {
                    sb.AppendLine(line);
                }
            }

            return sb.ToString();
        }

        public string ProcessAsmLine(string input)
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

            return $"BitMagic.TemplateEngine.Objects.Template.WriteLiteral($@\"{output}\");";
        }

        public string Beautify(string input) => _beautify(input);
    }
}