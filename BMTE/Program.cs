using BigMagic.TemplateEngine.Compiler;
using BitMagic.TemplateEngine.X16;

var engine = CsasmEngine.CreateEngine();

var inputCode = @".machine CommanderX16R42
    @BM.X16Header();
    nop
    nop
    nop
    nop

    for (var i = 1; i < 10; i ++)
    {
        ; step @(i)
        lda #@(i)
    }

.loop:
    jmp loop

    rts
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
