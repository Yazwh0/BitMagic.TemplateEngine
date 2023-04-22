using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BitMagic.TemplateEngine.Objects;

public interface ITemplateRunner
{
    Task Execute();
}

public interface ISourceResult
{
    string Code { get; }
    int[] Map { get; }
}
