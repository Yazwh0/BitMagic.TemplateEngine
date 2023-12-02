
using System.Collections.Generic;
using BitMagic.TemplateEngine.Objects;
using BitMagic.Common;
using System;
using System.Threading.Tasks;
using System.Linq;

namespace BitMagic.TemplateEngine.Compiler;

public static partial class MacroAssembler
{
    public class ProcessResult : SourceFileBase
    {
        public ISourceResult Source { get; }
        public byte[] CompiledData { get; }
        private readonly GlobalBuildState _buildState;

        public ProcessResult[] References { get; }
        public string Classname { get; }
        public string Namespace { get; }

        private List<SourceFileBase> _parents = new List<SourceFileBase>();
        public override IReadOnlyList<ISourceFile> Parents => _parents;

        public override IReadOnlyList<string> Content { get; protected set; }

        private List<ParentSourceMapReference> _parentMap = new List<ParentSourceMapReference>();
        public override IReadOnlyList<ParentSourceMapReference> ParentMap => _parentMap;

        internal ProcessResult(ISourceResult source, byte[] compiledData, GlobalBuildState buildState, string @namespace, string classname)
        {
            Source = source;
            CompiledData = compiledData; // we need .net data as we create metadata from this later
            References = buildState.References.ToArray();
            Classname = classname;
            Namespace = @namespace;
            Origin = SourceFileType.Intermediary;
            ActualFile = false;

            _buildState = buildState;

            Content = source.Code.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        }

        public void SetName(string name)
        {
            Name = name;
            Path = name;
        }

        public void SetParentAndMap(SourceFileBase parent)
        {
            var lookup = new List<string>();

            var parentFiles = new List<SourceFileBase>() { parent };
            var parentMap = new List<ParentSourceMapReference>();

            lookup.Add(parent.Path);
            parent.AddChild(this);

            foreach (var i in Source.Map)
            {
                if (i.Line >= 0 && !string.IsNullOrWhiteSpace(i.SourceFilename))
                {
                    if (!lookup.Contains(i.SourceFilename))
                    {
                        var f = _buildState.SourceFiles.FirstOrDefault(j => j.Path == i.SourceFilename);

                        if (f == null)
                            throw new ImportParseException($"Cannot find file {i.SourceFilename} in build state, referenced in {parent.Name}, need a rebuild?");

                        parentFiles.Add(f);
                        lookup.Add(f.Path);

                        f.AddChild(this);

                        parentMap.Add(new ParentSourceMapReference(i.Line, parentFiles.Count - 1));
                        continue;
                    }

                    parentMap.Add(new ParentSourceMapReference(i.Line, lookup.IndexOf(i.SourceFilename)));
                    continue;

                }
                parentMap.Add(new ParentSourceMapReference(-1, -1));
            }

            _parents = parentFiles;
            _parentMap = parentMap;
        }

        public override Task UpdateContent()
        {
            return Task.CompletedTask;
        }
    }
}
