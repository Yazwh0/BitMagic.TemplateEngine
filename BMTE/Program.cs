using BigMagic.TemplateEngine.Compiler;
using BitMagic.TemplateEngine.X16;

var engine = CsasmEngine.CreateEngine();

var inputCode = @"
    BM.X16Header();

    .proc docount
    for (var i = 0; i < 10; i++)
    {
        lda #@(i)
        sta DATA0
    }
    .endproc

    @BM.Bytes(new [] {1, 2, 3});
";

var result = await engine.ProcessFile(inputCode, "main.dll");

var generated = result.Source.Code.Split(Environment.NewLine);
for(var i = 0; i < generated.Length; i++)
{
    if (result.Source.Map.Length > i)
        Console.Write(result.Source.Map[i]);
    Console.Write("\t");
    Console.WriteLine(generated[i]);
}
