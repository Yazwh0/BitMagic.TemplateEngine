# BitMagic.TemplateEngine
Template Engine

Used to generate code from a template, for example:

```c#
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
```

Generates:

```asm
.byte   $0C, $08, $0A, $00, $9E, $20, $32, $30, $36, $34, $00, $00, $00, $00, $00

.proc docount
        lda #0
        lda #1
        lda #2
        lda #3
        lda #4
        lda #5
        lda #6
        lda #7
        lda #8
        lda #9
.endproc

.byte   $01, $02, $03
```
