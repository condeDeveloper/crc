# crc

CRC e somas de verificação do zero em C# e .NET 8 — dezoito modelos do catálogo,
Adler-32, Fletcher e a soma do cabeçalho IP.

```
$ dotnet medidor.dll catalogo

  modelo              larg   polinômio          inicial      refin refout   conferidor
  ------------------------------------------------------------------------------------
  CRC-16/ARC           16   0x8005             0x0            sim    sim   0xBB3D
  CRC-16/IBM-3740      16   0x1021             0xFFFF         não    não   0x29B1
  CRC-32/ISO-HDLC      32   0x4C11DB7          0xFFFFFFFF     sim    sim   0xCBF43926
  CRC-32/ISCSI         32   0x1EDC6F41         0xFFFFFFFF     sim    sim   0xE3069283
  CRC-32/BZIP2         32   0x4C11DB7          0xFFFFFFFF     não    não   0xFC891918
  CRC-32/MPEG-2        32   0x4C11DB7          0xFFFFFFFF     não    não   0x376E6E7
  CRC-32/CKSUM         32   0x4C11DB7          0x0            não    não   0x765E7680
  CRC-32/JAMCRC        32   0x4C11DB7          0xFFFFFFFF     sim    sim   0x340BC6D9
  CRC-64/XZ            64   0x42F0E1EBA9EA3693 0xFFFFFFFFFFFFFFFF sim sim  0x995DC9BBDF1939FA
```

**Cinco modelos desta tabela têm o mesmo polinômio** e dão cinco resultados
diferentes. É por isso que "usamos CRC-32 com o polinômio 0x04C11DB7" não
especifica nada: são **seis** parâmetros, e o polinômio é um deles.

## Os juízes, e dois deles estavam escondidos no .NET

**O catálogo.** Cada modelo vem com um `check` — o valor da cadeia `"123456789"`
— publicado por Ross Williams em 1993 e mantido por Greg Cook. São dezoito
valores que existem exatamente para isto: provar, com uma linha, que a
implementação é do modelo certo.

Só que dezoito valores são uma amostra, e o que se quer é um juiz para
**qualquer** entrada. Ele existe, e estava à vista:

**O gzip guarda o CRC-32 dos dados originais nos últimos oito bytes do fluxo.**

```csharp
using (var gzip = new GZipStream(memoria, CompressionLevel.NoCompression, true))
    gzip.Write(dados);

return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(bytes.Length - 8, 4));
```

**O zlib guarda o Adler-32 nos últimos quatro**, em big-endian. Duas linhas, zero
dependências, e dois oráculos **ilimitados** — a implementação da própria
plataforma, consultada uma vez por mensagem.

Achar um oráculo assim é a parte que mais compensa em montar um teste. Ele
transforma *"bateu nos dezoito valores publicados"* em *"bate em todas as entradas
que eu quiser gerar"*: aqui são **todas as 65.793 mensagens de até dois bytes**,
mais cinquenta mil sorteadas, mais os tamanhos que atravessam o bloco interno do
Adler.

E há um terceiro juiz que não depende de ninguém: a versão **bit a bit**, que é a
definição escrita direto, contra a versão por tabela, que é uma otimização dela.
Uma tabela montada errada produz um CRC que parece aleatório, muda quando os dados
mudam, e está errado.

São 17 testes.

## Por que o registrador começa em uns

```
$ dotnet medidor.dll zeros

  modelo              inicial     vazio    um zero   três zeros   cego?
  ----------------------------------------------------------------------
  CRC-32/ISO-HDLC    0xFFFFFFFF  0x0        0xD202EF8D 0xFF41D912     não
  CRC-32/CKSUM       0x0         0xFFFFFFFF 0xFFFFFFFF 0xFFFFFFFF     SIM
  CRC-64/ECMA-182    0x0         0x0        0x0        0x0            SIM
```

Eu tinha escrito um teste afirmando que um byte zero **nunca** dá o mesmo que a
mensagem vazia. Ele falhou no CRC-3/GSM, e o erro era meu.

Com o registrador em zero, processar um byte zero não muda nada — zero dividido
por qualquer coisa dá zero. O CRC fica **cego para zeros no começo da mensagem**,
e um quadro que perdeu o cabeçalho inteiro passa pela conferência.

Os cegos são exatamente os de inicial zero, e essa é a **única função** do
parâmetro `inicial`. É por isso que quase todo modelo moderno começa com o
registrador em uns.

## As garantias são deduções, não medidas

```
$ dotnet medidor.dll deteccao

  soma                 bits    erros de 1 bit   erros de 8 bits sorteados
  ------------------------------------------------------------------------
  CRC-8/SMBUS             8           0 de 512        1520 de 200.000
  CRC-16/ARC             16           0 de 512           3 de 200.000
  CRC-32/ISO-HDLC        32           0 de 512           0 de 200.000
  CRC-64/XZ              64           0 de 512           0 de 200.000
  soma da internet       16           —                 38 de 200.000
```

A coluna do meio é uma **garantia**, e não uma medida. Ela sai da álgebra da
divisão polinomial:

- todo erro de **um bit** é detectado
- toda **rajada** de até `largura` bits é detectada, **sempre** — e é a que mais
  vale, porque o erro físico típico (um risco no disco, um pico na linha) é uma
  rajada, não bits isolados
- todo erro de número **ímpar** de bits, se o polinômio for divisível por `x+1`

"Sempre" quer dizer sempre: não é probabilidade alta, é impossibilidade. É por
isso que um CRC-32 está no rodapé de todo quadro de Ethernet.

A coluna da direita é estatística: dos erros grandes, escapa `2^-largura`.

Os testes destas garantias são **exaustivos dentro de cada classe** — não sorteiam
erros, produzem todos os de cada tipo. E um deles me ensinou que **a numeração
dos bits não é livre**: o teorema da rajada fala da ordem em que os bits entram no
registrador, e ela depende da reflexão. Virar "o bit 7" com uma numeração e medir
a garantia da outra produz uma falha que parece defeito do CRC e é defeito do
teste.

## O resíduo, e o que ele não alcança

Um receptor não precisa calcular o CRC e comparar: basta passar **tudo** —
mensagem e CRC — pelo mesmo registrador e olhar se o que sobra é uma constante do
modelo. Uma comparação, sempre a mesma, para qualquer tamanho de quadro. É assim
que uma placa de rede confere um pacote.

E o truque só vale quando a **largura é múltiplo de oito**. Um CRC de 3 bits ocupa
três bits, não um byte, e anexá-lo como byte inteiro acrescenta cinco bits de
enchimento que o registrador vai processar. Não é limitação da implementação, é da
propriedade: os protocolos de largura estranha — o USB, o GSM — são orientados a
**bit**, e ali a conta fecha.

Dezesseis dos dezoito modelos passam no teste do resíduo. Os dois de fora estão
nomeados, com o motivo.

## O Adler-32, e o que ele trocou

```
  a = 1 + d₁ + d₂ + … + dₙ           (mod 65521)
  b = (1 + d₁) + (1 + d₁ + d₂) + …   (mod 65521)
```

O `a` é a soma dos bytes e sozinho não veria uma troca de ordem. O `b` é a soma
das somas parciais, e é ele que faz a **posição** importar.

**65521 é o maior primo abaixo de 65536**, e ser primo é o ponto — com um módulo
composto os valores se concentrariam. O Fletcher-16, oito anos antes, usou 255
justamente por ser barato, e pagou: com módulo 255 os bytes `0x00` e `0xFF` são
**equivalentes**, porque 255 ≡ 0.

E a fraqueza do Adler está na mensagem **curta**. Com dois bytes, o `a` vai no
máximo a 511 e o `b` a 767 — os 65.536 valores possíveis cabem em **1,2%** do
espaço de 32 bits. (Eu tinha escrito "menos de 1%" num teste; é 1,17%.) O `zlib` o
usa mesmo assim, porque o caso dele é o arquivo grande.

## A velocidade

```
  cálculo                    tempo      MB/s
  ----------------------------------------------
  CRC-32 por tabela          21.3 ms       469
  CRC-32 bit a bit          435.5 ms        23
  Adler-32                    5.1 ms      1946
```

A tabela é **20 vezes** mais rápida que a definição. A ideia é de 1988, de Dilip
Sarwate: em vez de oito deslocamentos por byte, um acesso a tabela e um
ou-exclusivo. Como a divisão é linear, o resto de cada byte no topo do registrador
dá para calcular antes.

E o Adler é **4 vezes** mais rápido que o CRC por tabela — que era exatamente a
meta de Mark Adler em 1995.

O que falta aqui é a instrução do processador: desde 2008 o x86 calcula o CRC-32C
em hardware, e ali a tabela perde por outra ordem de grandeza. É o motivo real de
o polinômio de Castagnoli ter se espalhado.

## Os nomes errados que viraram padrão

O `CRC-16/IBM-3740` é conhecido como **`CRC-16/CCITT-FALSE`**, e esse apelido é
literalmente o nome que o catálogo lhe dá. Durante anos circulou código rotulado
"CCITT" que não era o CCITT de verdade; o catálogo registrou o engano no próprio
nome, e o engano virou o padrão. O CCITT de verdade é o `CRC-16/KERMIT`.

## Rodar

.NET 8. Zero dependências fora do xUnit, e só nos testes.

```
dotnet test testes/Crc.Testes/Crc.Testes.csproj -c Release
dotnet run --project ferramentas/Medidor/Medidor.csproj -c Release -- catalogo
```

As medidas de tempo moram na ferramenta e **não** na suíte, de propósito: uma
afirmação de relógio falha sem que nada tenha piorado — basta a máquina do CI
estar ocupada — e o efeito é ensinar quem trabalha no projeto a ignorar teste
vermelho.

## O que ele não faz

Não usa SIMD nem a instrução `CRC32` do processador, como está dito acima. Não faz
*slicing-by-8*, que processa oito bytes por vez com oito tabelas e é o que uma
implementação séria de produção usa.

Não calcula sobre número de bits que não fecha byte — o que os protocolos de CRC-3
e CRC-5 realmente precisam.

Não é um resumo criptográfico e não tenta ser: **é trivial** fabricar duas
mensagens com o mesmo CRC, e um CRC não protege contra ninguém que queira
enganá-lo. Para isso a resposta é HMAC.

Não tem o catálogo inteiro — são mais de cem modelos, e aqui estão dezoito.

## Licença

MIT.
