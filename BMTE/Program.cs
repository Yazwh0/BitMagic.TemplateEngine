using BigMagic.Macro;
using BitMagic.TemplateEngine.X16;

var engine = CsasmEngine.CreateEngine();

var inputCode = @"
    BM.X16Header();

    .proc docount
    for (var i = 0; i < 10; i++)
    {
        lda #@(i)
    }
    .endproc

    @BM.Bytes(new [] {1, 2, 3});
";

var result = await engine.ProcessFile(inputCode, "main.dll");

Console.WriteLine(result.Content);
