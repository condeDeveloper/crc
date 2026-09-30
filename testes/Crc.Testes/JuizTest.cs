using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace Conde.Crc.Testes;

/// <summary>
/// Os juízes: o catálogo, e dois oráculos ilimitados escondidos no .NET.
/// </summary>
/// <remarks>
/// <para>
/// <b>O catálogo.</b> Cada modelo de CRC vem com um <c>check</c> — o valor da
/// cadeia ASCII <c>"123456789"</c> — publicado por Ross Williams e mantido por
/// Greg Cook. São dezoito valores que existem exatamente para isto: provar, com
/// uma linha, que a implementação é do modelo certo. Nenhum deles é meu.
/// </para>
/// <para>
/// <b>O gzip.</b> Uma tabela de dezoito valores é uma amostra, e o que se quer é
/// um juiz para <b>qualquer</b> entrada. Ele existe, escondido: o formato gzip
/// guarda o <b>CRC-32 dos dados originais</b> nos últimos oito bytes do fluxo.
/// Comprimir com <c>GZipStream</c> e ler o rodapé dá o CRC-32 calculado pelo
/// .NET, para qualquer mensagem, sem nenhuma dependência.
/// </para>
/// <para>
/// <b>O zlib.</b> A mesma coisa para o Adler-32: o formato zlib o guarda nos
/// últimos quatro bytes, em big-endian. O <c>ZLibStream</c> entrega um oráculo
/// ilimitado de Adler-32 de graça.
/// </para>
/// <para>
/// Achar um oráculo assim é a parte que mais compensa em montar um teste. Ele
/// transforma "bateu nos dezoito valores publicados" em "bate em todas as
/// entradas que eu quiser gerar".
/// </para>
/// </remarks>
public class JuizTest
{
    /// <summary>O CRC-32 que o .NET calculou, lido do rodapé do gzip.</summary>
    private static uint CrcPeloGzip(byte[] dados)
    {
        using var memoria = new MemoryStream();

        using (var gzip = new GZipStream(memoria, CompressionLevel.NoCompression, leaveOpen: true))
        {
            gzip.Write(dados);
        }

        var bytes = memoria.ToArray();

        // Os oito últimos bytes do gzip são: CRC-32 e tamanho, os dois em
        // little-endian.
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(bytes.Length - 8, 4));
    }

    /// <summary>O Adler-32 que o .NET calculou, lido do rodapé do zlib.</summary>
    private static uint AdlerPeloZlib(byte[] dados)
    {
        using var memoria = new MemoryStream();

        using (var zlib = new ZLibStream(memoria, CompressionLevel.NoCompression, leaveOpen: true))
        {
            zlib.Write(dados);
        }

        var bytes = memoria.ToArray();

        // Os quatro últimos bytes do zlib são o Adler-32, em BIG-endian -- ao
        // contrário do gzip, que é little. Os dois formatos são do mesmo autor e
        // do mesmo ano.
        return BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(bytes.Length - 4, 4));
    }

    [Fact(DisplayName = "o conferidor de TODOS os dezoito modelos do catálogo")]
    public void OConferidor()
    {
        var nove = Encoding.ASCII.GetBytes("123456789");

        foreach (var modelo in Catalogo.Todos)
        {
            var soma = new Soma(modelo);

            Assert.True(modelo.Conferidor == soma.De(nove),
                $"{modelo.Nome}: o catálogo diz 0x{modelo.Conferidor:X} e a tabela deu "
                + $"0x{soma.De(nove):X}");

            // E a versão bit a bit, que é a DEFINIÇÃO, tem de dar o mesmo.
            Assert.True(modelo.Conferidor == soma.DeBitABit(nove),
                $"{modelo.Nome}: o catálogo diz 0x{modelo.Conferidor:X} e o bit a bit deu "
                + $"0x{soma.DeBitABit(nove):X}");
        }

        Assert.Equal(18, Catalogo.Todos.Count);
    }

    [Fact(DisplayName = "TODAS as mensagens de 0, 1 e 2 bytes, contra o CRC-32 do gzip")]
    public void TodasAsMensagensCurtas()
    {
        // 65.793 mensagens, e o juiz é o CRC-32 que o próprio .NET escreveu no
        // rodapé do gzip. Não é uma tabela: é a implementação da plataforma,
        // consultada uma vez por mensagem.
        var soma = new Soma(Catalogo.Crc32);

        var quantos = 0;

        for (var tamanho = 0; tamanho <= 2; tamanho++)
        {
            var total = 1 << (8 * tamanho);

            var bytes = new byte[tamanho];

            for (var combinacao = 0; combinacao < total; combinacao++)
            {
                for (var i = 0; i < tamanho; i++)
                {
                    bytes[i] = (byte)(combinacao >> (8 * (tamanho - 1 - i)));
                }

                var esperado = CrcPeloGzip(bytes);

                Assert.True(esperado == (uint)soma.De(bytes),
                    $"os bytes {Convert.ToHexString(bytes)} dão 0x{esperado:X8} no gzip e "
                    + $"0x{soma.De(bytes):X8} aqui");

                quantos++;
            }
        }

        Assert.Equal(1 + 256 + 65_536, quantos);
    }

    [Fact(DisplayName = "cinquenta mil mensagens sorteadas, contra o gzip e o zlib")]
    public void MensagensSorteadas()
    {
        var crc = new Soma(Catalogo.Crc32);

        var sorteio = new Random(29);

        for (var caso = 0; caso < 50_000; caso++)
        {
            var bytes = new byte[sorteio.Next(0, 300)];

            sorteio.NextBytes(bytes);

            Assert.Equal(CrcPeloGzip(bytes), (uint)crc.De(bytes));
            Assert.Equal(AdlerPeloZlib(bytes), Adler.De(bytes));
        }
    }

    [Fact(DisplayName = "mensagens grandes, onde o Adler passa do bloco de 5.552")]
    public void MensagensGrandes()
    {
        // O laço do Adler só tira o módulo a cada 5.552 bytes, que é o maior
        // número de bytes 0xFF que cabe antes de o acumulador estourar 32 bits.
        // Uma mensagem menor que isso nunca exercita a volta do laço.
        var crc = new Soma(Catalogo.Crc32);

        var sorteio = new Random(7);

        foreach (var tamanho in (int[])[5_551, 5_552, 5_553, 11_104, 100_000])
        {
            var bytes = new byte[tamanho];

            sorteio.NextBytes(bytes);

            Assert.Equal(CrcPeloGzip(bytes), (uint)crc.De(bytes));
            Assert.Equal(AdlerPeloZlib(bytes), Adler.De(bytes));

            // E o pior caso do acumulador: tudo 0xFF.
            Array.Fill(bytes, (byte)0xFF);

            Assert.Equal(CrcPeloGzip(bytes), (uint)crc.De(bytes));
            Assert.Equal(AdlerPeloZlib(bytes), Adler.De(bytes));
        }
    }

    [Fact(DisplayName = "a tabela e o bit a bit concordam, em todos os modelos")]
    public void ATabelaEOBitABit()
    {
        // A versão bit a bit é a DEFINIÇÃO; a por tabela é uma otimização dela.
        // Uma tabela montada errada produz um CRC que parece perfeitamente
        // aleatório, muda quando os dados mudam, e está errado -- e sem um valor
        // publicado para cada entrada, comparar com a definição é a única forma
        // de pegar isso.
        var sorteio = new Random(11);

        foreach (var modelo in Catalogo.Todos)
        {
            var soma = new Soma(modelo);

            for (var caso = 0; caso < 500; caso++)
            {
                var bytes = new byte[sorteio.Next(0, 60)];

                sorteio.NextBytes(bytes);

                Assert.True(soma.De(bytes) == soma.DeBitABit(bytes),
                    $"{modelo.Nome} com {Convert.ToHexString(bytes)}: tabela deu "
                    + $"0x{soma.De(bytes):X} e bit a bit deu 0x{soma.DeBitABit(bytes):X}");
            }
        }
    }

    [Fact(DisplayName = "calcular em pedaços dá o mesmo que de uma vez")]
    public void EmPedacos()
    {
        // É o que um decodificador de fluxo precisa, e é onde um cálculo com
        // estado escondido quebraria.
        var sorteio = new Random(13);

        foreach (var modelo in Catalogo.Todos)
        {
            var soma = new Soma(modelo);

            for (var caso = 0; caso < 200; caso++)
            {
                var bytes = new byte[sorteio.Next(2, 100)];

                sorteio.NextBytes(bytes);

                var corte = sorteio.Next(1, bytes.Length);

                var emPedacos = soma.Terminar(
                    soma.Continuar(
                        soma.Continuar(soma.Comecar(), bytes.AsSpan(0, corte)),
                        bytes.AsSpan(corte)));

                Assert.True(soma.De(bytes) == emPedacos,
                    $"{modelo.Nome}: de uma vez 0x{soma.De(bytes):X}, "
                    + $"em dois pedaços 0x{emPedacos:X}");
            }
        }

        // E o Adler também.
        var adlerSorteio = new Random(17);

        for (var caso = 0; caso < 2_000; caso++)
        {
            var bytes = new byte[adlerSorteio.Next(2, 200)];

            adlerSorteio.NextBytes(bytes);

            var corte = adlerSorteio.Next(1, bytes.Length);

            Assert.Equal(
                Adler.De(bytes),
                Adler.Continuar(Adler.Continuar(1, bytes.AsSpan(0, corte)), bytes.AsSpan(corte)));
        }
    }

    [Fact(DisplayName = "o resíduo é uma constante, e é assim que o hardware confere")]
    public void OResiduo()
    {
        // Um receptor não precisa calcular o CRC e comparar: basta passar TUDO --
        // mensagem e CRC -- pelo mesmo registrador e olhar se o que sobra é a
        // constante do modelo. Uma comparação, sempre a mesma, para qualquer
        // tamanho de quadro.
        //
        // E há uma condição que este teste me ensinou, reprovando no CRC-3/GSM:
        // o truque só vale quando a LARGURA É MÚLTIPLO DE OITO. Um CRC de 3 bits
        // ocupa três bits, não um byte, e anexá-lo como byte inteiro acrescenta
        // cinco bits de enchimento que o registrador vai processar -- e o resíduo
        // deixa de ser constante.
        //
        // Não é uma limitação desta implementação: é da propriedade. Os
        // protocolos que usam CRC de largura estranha -- o USB, o GSM -- são
        // orientados a BIT, e ali a conta fecha. Byte a byte, não.
        var sorteio = new Random(19);

        var conferidos = 0;

        foreach (var modelo in Catalogo.Todos.Where(m => m.Largura % 8 == 0))
        {
            conferidos++;

            var soma = new Soma(modelo);

            var residuo = soma.Residuo();

            for (var caso = 0; caso < 200; caso++)
            {
                var mensagem = new byte[sorteio.Next(1, 80)];

                sorteio.NextBytes(mensagem);

                var comCrc = new byte[mensagem.Length + (modelo.Largura + 7) / 8];

                mensagem.CopyTo(comCrc, 0);

                soma.EscreverCrc(soma.De(mensagem), comCrc.AsSpan(mensagem.Length));

                Assert.True(residuo == soma.De(comCrc),
                    $"{modelo.Nome}: o resíduo é 0x{residuo:X} e esta mensagem deu "
                    + $"0x{soma.De(comCrc):X}");
            }
        }

        // Dezesseis dos dezoito modelos têm largura múltipla de oito. Os dois de
        // fora são o CRC-3/GSM e o CRC-5/USB.
        Assert.Equal(16, conferidos);
    }

    [Fact(DisplayName = "o resíduo NÃO vale para largura que não fecha byte")]
    public void OResiduoNaoValeParaLarguraEstranha()
    {
        // O teste acima só roda nos modelos de largura múltipla de oito, e este
        // registra o porquê em vez de deixar a exclusão sem explicação.
        //
        // Com o CRC-3/GSM, anexar o CRC como um byte inteiro mete cinco bits de
        // enchimento no registrador, e o resultado varia com a mensagem.
        var soma = new Soma(Catalogo.Crc3Gsm);

        var residuo = soma.Residuo();

        var sorteio = new Random(23);

        var quantosBateram = 0;

        for (var caso = 0; caso < 200; caso++)
        {
            var mensagem = new byte[sorteio.Next(1, 20)];

            sorteio.NextBytes(mensagem);

            var comCrc = new byte[mensagem.Length + 1];

            mensagem.CopyTo(comCrc, 0);

            soma.EscreverCrc(soma.De(mensagem), comCrc.AsSpan(mensagem.Length));

            if (soma.De(comCrc) == residuo)
            {
                quantosBateram++;
            }
        }

        // Se fosse constante, bateriam os 200. Batem uns poucos, por acaso.
        Assert.True(quantosBateram < 100,
            $"bateram {quantosBateram} de 200 -- se fosse constante, seriam 200");
    }

    [Fact(DisplayName = "o Adler-32 e o Fletcher, nos valores publicados")]
    public void OsValoresPublicados()
    {
        var nove = Encoding.ASCII.GetBytes("123456789");

        // O Adler-32 de "123456789" é 0x091E01DE, e o do texto vazio é 1 -- e
        // não zero, porque o acumulador começa em 1.
        Assert.Equal(0x091E01DEu, Adler.De(nove));
        Assert.Equal(1u, Adler.De([]));

        // O Adler de "Wikipedia" é o exemplo que a própria RFC 1950 e todo texto
        // sobre o assunto usam.
        Assert.Equal(0x11E60398u, Adler.De(Encoding.ASCII.GetBytes("Wikipedia")));

        // Fletcher-16 de "abcde" e "abcdef", que são os exemplos clássicos.
        Assert.Equal(0xC8F0, Adler.Fletcher16(Encoding.ASCII.GetBytes("abcde")));
        Assert.Equal(0x2057, Adler.Fletcher16(Encoding.ASCII.GetBytes("abcdef")));

        // E o zlib concorda com os três primeiros, que é o juiz de verdade.
        Assert.Equal(AdlerPeloZlib(nove), Adler.De(nove));
        Assert.Equal(AdlerPeloZlib([]), Adler.De([]));
        Assert.Equal(
            AdlerPeloZlib(Encoding.ASCII.GetBytes("Wikipedia")),
            Adler.De(Encoding.ASCII.GetBytes("Wikipedia")));
    }

    [Fact(DisplayName = "os CINCO CRC-32 com o mesmo polinômio dão resultados diferentes")]
    public void OMesmoPolinomio()
    {
        // É a prova de que dizer "usamos CRC-32 com o polinômio 0x04C11DB7" não
        // especifica nada. Quatro modelos do catálogo têm esse polinômio, e os
        // quatro são incompatíveis entre si.
        var comEsseMesmo = Catalogo.Todos
            .Where(m => m.Polinomio == 0x04C11DB7)
            .ToList();

        // São CINCO, e eu tinha escrito quatro: CRC-32, BZIP2, MPEG-2, CKSUM e
        // JAMCRC. O teste corrigiu a conta.
        Assert.Equal(5, comEsseMesmo.Count);

        var nove = Encoding.ASCII.GetBytes("123456789");

        var resultados = comEsseMesmo
            .Select(m => new Soma(m).De(nove))
            .ToList();

        // Os cinco são diferentes entre si.
        Assert.Equal(5, resultados.Distinct().Count());

        // E só um deles é o do gzip.
        Assert.Single(resultados.Where(r => r == CrcPeloGzip(nove)));
    }
}
