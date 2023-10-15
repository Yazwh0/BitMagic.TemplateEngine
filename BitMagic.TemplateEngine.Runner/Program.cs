// Application which loads the X16 assemblies and runs them to procude the code

using BitMagic.TemplateEngine.Objects;
using BitMagic.TemplateEngine.Runner;
using BitMagic.TemplateEngine.X16;
using CommandLine;
using System.Reflection;
using Newtonsoft.Json;

var result = Parser.Default.ParseArguments<Options>(args);

if (result.Errors.Any())
{
    foreach (var i in result.Errors)
        Console.Error.Write(i);

    return 1;
}

try
{

    var basePath = Path.GetFullPath(result.Value.BasePath);

    foreach (var i in result.Value.Assemblies)
    {
        Assembly.LoadFrom(Path.Combine(basePath, i)); // we need to _read from disk_ so the activator works later!!
    }

    Template.StartProject();

    var @namespace = result.Value.Namespace;
    var classname = result.Value.Classname;

    var oh = AppDomain.CurrentDomain.CreateInstance($"{@namespace}.dll", $"{@namespace}.{classname}");
    if (oh == null)
    {
        Console.Error.WriteLine($"Create instance for {@namespace}.{classname} returned null.");
        return 1;
    }

    var runner = oh.Unwrap() as ITemplateRunner;

    if (runner == null)
    {
        Console.Error.WriteLine($"{classname} is not a ITemplateRunner");
        return 1;
    }

    runner.Initialise();
    await runner.Execute();
   
    Console.WriteLine(JsonConvert.SerializeObject(CsasmEngine.Beautify(Template.GenerateCode()), 
        new JsonSerializerSettings() { TypeNameHandling = TypeNameHandling.Auto}));
}
catch (Exception e)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine(e.StackTrace);
    return 1;
}

return 0;