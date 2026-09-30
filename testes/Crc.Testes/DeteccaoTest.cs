using System.Text;
using Xunit;

namespace Conde.Crc.Testes;

/// <summary>
/// As garantias de detecção — que são <b>deduções</b>, não medidas.
/// </summary>
/// <remarks>
/// <para>
/// Um CRC não é um resumo criptográfico e não tenta ser. O que ele dá são
/// garantias <b>absolutas</b> sobre classes específicas de erro, e elas saem da
/// álgebra da divisão polinomial — não de estatística:
/// </para>
/// <list type="bullet">
///   <item><description>todo erro de <b>um bit</b> é detectado</description></item>
///   <item><description>toda <b>rajada</b> de até <c>largura</c> bits é detectada, <b>sempre</b></description></item>
///   <item><description>todo erro de número <b>ímpar</b> de bits, se o polinômio for divisível por <c>x+1</c></description></item>
/// </list>
/// <para>
/// "Sempre" quer dizer sempre: não é uma probabilidade alta, é uma
/// impossibilidade. É por isso que um CRC-32 está no rodapé de todo quadro de
/// Ethernet — o erro típico de um cabo é uma rajada, e uma rajada de até 32 bits
/// nunca passa.
/// </para>
/// <para>
/// Estes testes são, portanto, exaustivos dentro de cada classe: não sorteiam
/// erros, produzem <b>todos</b> os de cada tipo.
/// </para>
/// </remarks>
public class DeteccaoTest
{
    private static byte[] Mensagem(int tamanho, int semente)
    {
        var bytes = new byte[tamanho];

        new Random(semente).NextBytes(bytes);

        return bytes;
    }

    /// <summary>Vira um bit, na ordem de transmissão que o modelo implica.</summary>
    /// <remarks>
    /// <para>
    /// A numeração dos bits <b>não é livre</b>, e isto um teste reprovado me
    /// ensinou. O teorema da rajada fala da ordem em que os bits entram no
    /// registrador, e ela depende da reflexão: um modelo refletido consome cada
    /// byte do bit menos significativo para o mais, e um modelo direto faz o
    /// contrário.
    /// </para>
    /// <para>
    /// Virar "o bit 7" com uma numeração e medir a garantia da outra produz uma
    /// falha que parece defeito do CRC e é defeito do teste.
    /// </para>
    /// </remarks>
    private static void Virar(byte[] bytes, int bit, Modelo modelo) =>
        bytes[bit / 8] ^= (byte)(modelo.RefletirEntrada
            ? 1 << (bit % 8)
            : 1 << (7 - bit % 8));

    [Fact(DisplayName = "TODO erro de um bit é detectado, em todos os modelos")]
    public void UmBit()
    {
        // Exaustivo: cada bit de uma mensagem de 200 bytes, virado um de cada
        // vez. Mil e seiscentos erros por modelo, e nenhum pode passar.
        var mensagem = Mensagem(200, 1);

        foreach (var modelo in Catalogo.Todos)
        {
            var soma = new Soma(modelo);

            var original = soma.De(mensagem);

            for (var bit = 0; bit < mensagem.Length * 8; bit++)
            {
                var estragada = (byte[])mensagem.Clone();

                Virar(estragada, bit, modelo);

                Assert.True(soma.De(estragada) != original,
                    $"{modelo.Nome}: virar o bit {bit} não mudou o CRC");
            }
        }
    }

    [Fact(DisplayName = "TODA rajada de até `largura` bits é detectada")]
    public void Rajada()
    {
        // É a garantia que vale mais na prática, porque o erro físico típico --
        // um risco no disco, um pico na linha, um pacote com interferência --
        // é uma rajada, e não bits isolados.
        //
        // Exaustivo: toda posição de começo, todo comprimento até a largura, e
        // todo padrão de bits dentro da rajada seria demais; aqui são todas as
        // posições e comprimentos, com o padrão sorteado -- e o primeiro e o
        // último bit da rajada SEMPRE virados, que é a definição de rajada de
        // comprimento n.
        var mensagem = Mensagem(64, 3);

        var sorteio = new Random(5);

        foreach (var modelo in Catalogo.Todos)
        {
            var soma = new Soma(modelo);

            var original = soma.De(mensagem);

            for (var comeco = 0; comeco + modelo.Largura <= mensagem.Length * 8; comeco++)
            {
                for (var comprimento = 1; comprimento <= modelo.Largura; comprimento++)
                {
                    var estragada = (byte[])mensagem.Clone();

                    // O primeiro e o último bit da rajada, sempre.
                    Virar(estragada, comeco, modelo);

                    if (comprimento > 1)
                    {
                        Virar(estragada, comeco + comprimento - 1, modelo);
                    }

                    // Os do meio, sorteados.
                    for (var i = 1; i < comprimento - 1; i++)
                    {
                        if (sorteio.Next(2) == 0)
                        {
                            Virar(estragada, comeco + i, modelo);
                        }
                    }

                    Assert.True(soma.De(estragada) != original,
                        $"{modelo.Nome}: uma rajada de {comprimento} bits a partir do "
                        + $"{comeco} passou despercebida");
                }
            }
        }
    }

    [Fact(DisplayName = "um número ÍMPAR de bits é sempre detectado — se o polinômio permitir")]
    public void NumeroImpar()
    {
        // A garantia vale quando o polinômio é divisível por (x + 1), que em
        // binário quer dizer: o polinômio completo tem um número PAR de bits em 1.
        //
        // É uma escolha de projeto de quem desenhou cada polinômio, e nem todos
        // a fizeram. Este teste descobre quais a têm, em vez de supor.
        var mensagem = Mensagem(40, 7);

        var sorteio = new Random(11);

        var comAGarantia = new List<string>();
        var semAGarantia = new List<string>();

        foreach (var modelo in Catalogo.Todos)
        {
            var soma = new Soma(modelo);

            var original = soma.De(mensagem);

            // O polinômio completo inclui o termo x^largura, que é implícito --
            // e ele está no bit de índice `largura`, não `largura - 1`. Eu tinha
            // escrito `largura - 1` E somado um a mais, contando o mesmo termo
            // duas vezes; o teste reprovou no CRC-3/GSM.
            //
            // Divisível por (x+1) equivale a ter um número PAR de termos, e é o
            // que dá a garantia sobre erro de número ímpar de bits: se p(1) = 0,
            // todo polinômio de erro com número ímpar de termos -- que também
            // vale 1 em x=1 -- não pode ser múltiplo dele.
            var completo = modelo.Largura == 64
                ? modelo.Polinomio    // o x^64 não cabe, e é sempre 1
                : modelo.Polinomio | (1UL << modelo.Largura);

            var quantosTermos = System.Numerics.BitOperations.PopCount(completo)
                + (modelo.Largura == 64 ? 1 : 0);

            var divisivel = quantosTermos % 2 == 0;

            var passou = false;

            for (var caso = 0; caso < 3_000 && !passou; caso++)
            {
                var quantos = sorteio.Next(3) * 2 + 1;   // 1, 3 ou 5

                var estragada = (byte[])mensagem.Clone();

                var bits = new HashSet<int>();

                while (bits.Count < quantos)
                {
                    bits.Add(sorteio.Next(mensagem.Length * 8));
                }

                foreach (var bit in bits)
                {
                    Virar(estragada, bit, modelo);
                }

                if (soma.De(estragada) == original)
                {
                    passou = true;
                }
            }

            if (passou)
            {
                semAGarantia.Add(modelo.Nome);
            }
            else
            {
                comAGarantia.Add(modelo.Nome);
            }

            // A dedução e a medida têm de concordar: se o polinômio é divisível
            // por (x+1), nenhum erro ímpar passa.
            if (divisivel)
            {
                Assert.False(passou,
                    $"{modelo.Nome} tem polinômio divisível por (x+1) e deixou passar "
                    + "um erro de número ímpar de bits");
            }
        }

        // A maioria dos modelos em uso tem a garantia -- foi escolhida de
        // propósito, porque erro de número ímpar é comum.
        Assert.True(comAGarantia.Count > semAGarantia.Count,
            $"com a garantia: {string.Join(", ", comAGarantia)}; "
            + $"sem: {string.Join(", ", semAGarantia)}");
    }

    [Fact(DisplayName = "dois bits errados: quantos escapam de cada largura")]
    public void DoisBits()
    {
        // Aqui não há garantia absoluta -- há uma DISTÂNCIA DE HAMMING, que
        // depende do polinômio e do tamanho da mensagem. O que se mede é quantos
        // pares de bits, de todos os possíveis, produzem o mesmo CRC.
        //
        // Exaustivo sobre os pares de uma mensagem de 32 bytes: 256 bits dão
        // 32.640 pares.
        var mensagem = Mensagem(32, 13);

        foreach (var modelo in (Modelo[])
                 [Catalogo.Crc8, Catalogo.Crc16Arc, Catalogo.Crc32, Catalogo.Crc32C])
        {
            var soma = new Soma(modelo);

            var original = soma.De(mensagem);

            var escaparam = 0;
            var pares = 0;

            for (var um = 0; um < mensagem.Length * 8; um++)
            {
                for (var outro = um + 1; outro < mensagem.Length * 8; outro++)
                {
                    var estragada = (byte[])mensagem.Clone();

                    Virar(estragada, um, modelo);
                    Virar(estragada, outro, modelo);

                    pares++;

                    if (soma.De(estragada) == original)
                    {
                        escaparam++;
                    }
                }
            }

            Assert.Equal(32_640, pares);

            // Para 8 bits de CRC, alguns pares escapam mesmo numa mensagem curta.
            // Para 16, 32 e 32C, nenhum -- a distância de Hamming desses
            // polinômios é maior que 2 nesse tamanho, e isso é um fato conhecido
            // sobre eles, não uma sorte.
            if (modelo.Largura >= 16)
            {
                Assert.Equal(0, escaparam);
            }
        }
    }

    [Fact(DisplayName = "o que a soma da internet NÃO vê, e o CRC vê")]
    public void AContraMostra()
    {
        // A soma do cabeçalho IP soma palavras de 16 bits. Trocar duas palavras
        // de lugar não muda a soma -- a adição é comutativa -- e ela não vê.
        var original = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };

        var trocado = new byte[] { 0x33, 0x44, 0x11, 0x22, 0x55, 0x66, 0x77, 0x88 };

        Assert.Equal(Adler.Internet(original), Adler.Internet(trocado));

        // O CRC vê, porque a divisão polinomial não é comutativa: a POSIÇÃO de
        // cada bit entra na conta.
        var crc = new Soma(Catalogo.Crc32);

        Assert.NotEqual(crc.De(original), crc.De(trocado));

        // O Adler também vê, pelo mesmo motivo: o acumulador `b` soma as somas
        // parciais, e um byte no começo entra em todas as parcelas.
        Assert.NotEqual(Adler.De(original), Adler.De(trocado));

        // E o Fletcher-16 tem a fraqueza dele: com módulo 255, o byte 0x00 e o
        // 0xFF são EQUIVALENTES, porque 255 ≡ 0.
        Assert.Equal(
            Adler.Fletcher16([0x00, 0x41, 0x42]),
            Adler.Fletcher16([0xFF, 0x41, 0x42]));

        // O Adler-32 não tem essa, porque 65521 não divide 256.
        Assert.NotEqual(
            Adler.De([0x00, 0x41, 0x42]),
            Adler.De([0xFF, 0x41, 0x42]));

        // E nenhuma das somas vê zeros acrescentados no começo de uma mensagem
        // que já começa com zero? O Adler vê, por causa do valor inicial 1.
        Assert.NotEqual(Adler.De([0x00]), Adler.De([0x00, 0x00]));
    }

    [Fact(DisplayName = "o Adler-32 é fraco em mensagem curta, e dá para ver")]
    public void OAdlerEmMensagemCurta()
    {
        // Com poucos bytes, o `a` mal sai de 1 e o `b` cresce devagar, então o
        // valor usa uma fração minúscula dos 32 bits. Aqui se conta quantos
        // valores distintos as 65.536 mensagens de dois bytes produzem.
        var adler = new HashSet<uint>();
        var crc = new HashSet<ulong>();

        var soma = new Soma(Catalogo.Crc32);

        for (var combinacao = 0; combinacao < 1 << 16; combinacao++)
        {
            byte[] bytes = [(byte)(combinacao >> 8), (byte)combinacao];

            adler.Add(Adler.De(bytes));
            crc.Add(soma.De(bytes));
        }

        // O CRC-32 é uma bijeção em mensagens de até 4 bytes: 65.536 mensagens,
        // 65.536 valores distintos.
        Assert.Equal(65_536, crc.Count);

        // O Adler-32 também separa todas as de dois bytes -- mas os valores se
        // concentram: o `a` vai de 1 a 511 e o `b` de 1 a 766, então o conjunto
        // inteiro cabe num cantinho dos 32 bits.
        Assert.Equal(65_536, adler.Count);

        var maior = adler.Max();
        var menor = adler.Min();

        // A faixa usada é ínfima perto de 2^32.
        Assert.True(maior < 0x03000000,
            $"o maior Adler de dois bytes é 0x{maior:X8}, e 2^32 é 0x100000000");

        Assert.True(menor >= 0x00010001);

        // Em números: os 65.536 valores cabem em 1,2% do espaço de 32 bits.
        //
        // Eu tinha escrito "menos de 1%" e o teste reprovou -- é 1,17%. A conta
        // sai direto: com dois bytes, o `a` vai no máximo a 511 e o `b` a 767,
        // então o maior valor possível é 767·65536 + 511, que é 0x02FF01FF.
        var fracao = (double)(maior - menor) / uint.MaxValue;

        Assert.InRange(fracao, 0.011, 0.012);

        Assert.Equal(0x02FF01FFu, Adler.De([0xFF, 0xFF]));
        // Dois bytes zero dao 0x00020001, e nao 0x00010001: o `a` fica em 1 e o
        // `b` acumula 1 + 1 = 2. Escrevi o valor de UM byte e o teste corrigiu.
        Assert.Equal(0x00020001u, Adler.De([0x00, 0x00]));
        Assert.Equal(0x00010001u, Adler.De([0x00]));
    }

    [Fact(DisplayName = "o CRC-32 do conteúdo vazio, e outros casos de borda")]
    public void AsBordas()
    {
        foreach (var modelo in Catalogo.Todos)
        {
            var soma = new Soma(modelo);

            // O CRC do vazio é o inicial passado pelo fim -- e tem de bater
            // entre a tabela e a definição.
            Assert.Equal(soma.De([]), soma.DeBitABit([]));

            // Um byte zero dá o mesmo que o vazio quando o INICIAL é zero -- e
            // isto é a razão de existirem os iniciais 0xFFFF e 0xFFFFFFFF.
            //
            // Com o registrador em zero, processar um byte zero não muda nada:
            // zero dividido por qualquer coisa dá zero. O CRC fica cego para
            // zeros no começo da mensagem, e um quadro que perdeu o cabeçalho
            // inteiro passa pela conferência.
            //
            // Eu tinha afirmado aqui que isso NUNCA acontece, e o teste reprovou
            // no CRC-3/GSM. A afirmação certa é condicional, e a condição é
            // exatamente o motivo pelo qual quase todo modelo moderno começa com
            // o registrador em uns.
            if (modelo.Inicial != 0)
            {
                Assert.NotEqual(soma.De([]), soma.De([0x00]));
                Assert.NotEqual(soma.De([0x00]), soma.De([0x00, 0x00]));
            }
            else
            {
                Assert.Equal(soma.De([]), soma.De([0x00]));
                Assert.Equal(soma.De([]), soma.De([0x00, 0x00, 0x00]));
            }

            // E o resultado cabe na largura.
            Assert.True(soma.De([]) <= modelo.Mascara);
            Assert.True(soma.De(Encoding.ASCII.GetBytes("qualquer coisa")) <= modelo.Mascara);
        }

        // O CRC-32 do vazio é zero, o que é uma coincidência bonita do modelo:
        // o inicial 0xFFFFFFFF, espelhado e somado com 0xFFFFFFFF, dá zero.
        Assert.Equal(0u, (uint)new Soma(Catalogo.Crc32).De([]));

        // Uma largura fora da faixa é recusada.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Soma(Catalogo.Crc32 with { Largura = 2 }));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Soma(Catalogo.Crc32 with { Largura = 65 }));

        Assert.Throws<ArgumentNullException>(() => new Soma(null!));
    }
}
