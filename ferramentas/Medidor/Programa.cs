using System.Diagnostics;
using System.Text;
using Conde.Crc;

namespace Conde.Crc.Ferramentas.Medidor;

/// <summary>
/// A ferramenta — que mostra o catálogo e mede o que os testes não medem.
/// </summary>
/// <remarks>
/// Tudo que depende de relógio mora aqui, e não na suíte de testes. Uma
/// afirmação de tempo falha sem que nada tenha piorado — basta a máquina do CI
/// estar ocupada — e o efeito é ensinar quem trabalha no projeto a ignorar teste
/// vermelho. Esta ferramenta imprime e sai com zero, sempre.
/// </remarks>
public static class Programa
{
    public static int Main(string[] argumentos)
    {
        // Procura o comando entre TODOS os argumentos. O `dotnet run` nem sempre
        // entrega só o que vem depois do `--`, e um `argumentos[0]` cru já
        // derrubou o CI de outro projeto daqui com o código todo funcionando.
        var comandos = new[] { "catalogo", "deteccao", "zeros", "velocidade", "texto" };

        var onde = Array.FindIndex(argumentos, a => comandos.Contains(a));

        if (onde < 0)
        {
            Console.WriteLine(Ajuda);

            return 0;
        }

        var resto = argumentos[onde..];

        switch (resto[0])
        {
            case "catalogo":
                ACatalogo();
                break;

            case "deteccao":
                Deteccao();
                break;

            case "zeros":
                Zeros();
                break;

            case "texto":
                Texto(resto.Length > 1 ? string.Join(' ', resto[1..]) : "123456789");
                break;

            default:
                Velocidade();
                break;
        }

        return 0;
    }

    /// <summary>O catálogo, com o conferidor de cada modelo.</summary>
    private static void ACatalogo()
    {
        Console.WriteLine("Os dezoito modelos, e o conferidor de \"123456789\"");
        Console.WriteLine();
        Console.WriteLine("  modelo              larg   polinômio          inicial   refin refout   conferidor");
        Console.WriteLine("  " + new string('-', 92));

        var nove = Encoding.ASCII.GetBytes("123456789");

        foreach (var modelo in Catalogo.Todos)
        {
            var soma = new Soma(modelo);

            var calculado = soma.De(nove);

            Console.WriteLine(
                $"  {modelo.Nome,-18} {modelo.Largura,4}   0x{modelo.Polinomio,-16:X}"
                + $" 0x{modelo.Inicial,-8:X} {(modelo.RefletirEntrada ? "sim" : "não"),5}"
                + $" {(modelo.RefletirSaida ? "sim" : "não"),6}   0x{calculado:X}"
                + (calculado == modelo.Conferidor ? string.Empty : "  DIVERGE"));
        }

        Console.WriteLine();
        Console.WriteLine("  Cinco modelos desta tabela têm o MESMO polinômio, 0x04C11DB7, e dão");
        Console.WriteLine("  cinco resultados diferentes. É por isso que \"usamos CRC-32\" não");
        Console.WriteLine("  especifica nada: são seis parâmetros, e o polinômio é só um deles.");
    }

    /// <summary>A detecção de erro, medida.</summary>
    private static void Deteccao()
    {
        Console.WriteLine("Quantos erros escapam de cada soma");
        Console.WriteLine();

        var mensagem = new byte[64];

        new Random(3).NextBytes(mensagem);

        var sorteio = new Random(29);

        const int quantosCasos = 200_000;

        Console.WriteLine("  soma                 bits    erros de 1 bit   erros de 8 bits sorteados");
        Console.WriteLine("  " + new string('-', 78));

        foreach (var modelo in (Modelo[])
                 [
                     Catalogo.Crc8, Catalogo.Crc16Arc, Catalogo.Crc16CcittFalse,
                     Catalogo.Crc32, Catalogo.Crc32C, Catalogo.Crc64Xz,
                 ])
        {
            var soma = new Soma(modelo);

            var original = soma.De(mensagem);

            var escaparamDeUm = 0;

            for (var bit = 0; bit < mensagem.Length * 8; bit++)
            {
                var estragada = (byte[])mensagem.Clone();

                estragada[bit / 8] ^= (byte)(1 << (bit % 8));

                if (soma.De(estragada) == original)
                {
                    escaparamDeUm++;
                }
            }

            var escaparamDeOito = 0;

            for (var caso = 0; caso < quantosCasos; caso++)
            {
                var estragada = (byte[])mensagem.Clone();

                for (var i = 0; i < 8; i++)
                {
                    var bit = sorteio.Next(mensagem.Length * 8);

                    estragada[bit / 8] ^= (byte)(1 << (bit % 8));
                }

                if (soma.De(estragada) == original)
                {
                    escaparamDeOito++;
                }
            }

            Console.WriteLine(
                $"  {modelo.Nome,-20} {modelo.Largura,4}    {escaparamDeUm,8} de {mensagem.Length * 8,-6}"
                + $"   {escaparamDeOito,6} de {quantosCasos:N0}");
        }

        // E as somas que não são CRC.
        var adlerEscaparam = 0;
        var internetEscaparam = 0;

        var adlerOriginal = Adler.De(mensagem);
        var internetOriginal = Adler.Internet(mensagem);

        for (var caso = 0; caso < quantosCasos; caso++)
        {
            var estragada = (byte[])mensagem.Clone();

            for (var i = 0; i < 8; i++)
            {
                var bit = sorteio.Next(mensagem.Length * 8);

                estragada[bit / 8] ^= (byte)(1 << (bit % 8));
            }

            if (Adler.De(estragada) == adlerOriginal)
            {
                adlerEscaparam++;
            }

            if (Adler.Internet(estragada) == internetOriginal)
            {
                internetEscaparam++;
            }
        }

        Console.WriteLine($"  {"Adler-32",-20} {32,4}    {"—",8}          "
            + $"   {adlerEscaparam,6} de {quantosCasos:N0}");

        Console.WriteLine($"  {"soma da internet",-20} {16,4}    {"—",8}          "
            + $"   {internetEscaparam,6} de {quantosCasos:N0}");

        Console.WriteLine();
        Console.WriteLine("  A coluna do meio é uma GARANTIA, não uma medida: nenhum erro de um bit");
        Console.WriteLine("  escapa de um CRC, nunca, e isso sai da álgebra da divisão polinomial.");
        Console.WriteLine();
        Console.WriteLine("  A da direita é estatística: dos erros grandes, escapa 2^-largura. Para");
        Console.WriteLine("  16 bits são 3 em 200 mil; para 32, nenhum em 200 mil -- seria preciso");
        Console.WriteLine("  esperar uns quatro bilhões de erros para ver um passar.");
    }

    /// <summary>Por que o registrador começa em uns, e não em zeros.</summary>
    private static void Zeros()
    {
        Console.WriteLine("Por que o registrador começa em uns");
        Console.WriteLine();
        Console.WriteLine("  Com o registrador em ZERO, processar um byte zero não muda nada: zero");
        Console.WriteLine("  dividido por qualquer coisa dá zero. O CRC fica cego para zeros no");
        Console.WriteLine("  começo da mensagem.");
        Console.WriteLine();
        Console.WriteLine("  modelo              inicial     vazio    um zero   três zeros   cego?");
        Console.WriteLine("  " + new string('-', 76));

        foreach (var modelo in Catalogo.Todos)
        {
            var soma = new Soma(modelo);

            var vazio = soma.De([]);
            var umZero = soma.De([0x00]);
            var tresZeros = soma.De([0x00, 0x00, 0x00]);

            var cego = vazio == umZero && vazio == tresZeros;

            Console.WriteLine(
                $"  {modelo.Nome,-18} 0x{modelo.Inicial,-8:X}  0x{vazio,-8:X} 0x{umZero,-8:X}"
                + $" 0x{tresZeros,-10:X}   {(cego ? "SIM" : "não")}");
        }

        Console.WriteLine();
        Console.WriteLine("  Os cegos são exatamente os de inicial zero. Um quadro que perdeu o");
        Console.WriteLine("  cabeçalho inteiro -- e virou zeros -- passa pela conferência deles.");
        Console.WriteLine();
        Console.WriteLine("  É por isso que quase todo modelo moderno começa com o registrador em");
        Console.WriteLine("  uns, e é a única função do parâmetro `inicial`.");
    }

    /// <summary>Um texto, em todos os modelos.</summary>
    private static void Texto(string texto)
    {
        var bytes = Encoding.UTF8.GetBytes(texto);

        Console.WriteLine($"\"{texto}\" — {bytes.Length} byte(s)");
        Console.WriteLine();

        foreach (var modelo in Catalogo.Todos)
        {
            var soma = new Soma(modelo);

            Console.WriteLine($"  {modelo.Nome,-20} 0x{soma.De(bytes):X}");
        }

        Console.WriteLine($"  {"Adler-32",-20} 0x{Adler.De(bytes):X8}");
        Console.WriteLine($"  {"Fletcher-16",-20} 0x{Adler.Fletcher16(bytes):X4}");
        Console.WriteLine($"  {"soma da internet",-20} 0x{Adler.Internet(bytes):X4}");
    }

    /// <summary>A velocidade: tabela contra bit a bit, e o Adler.</summary>
    private static void Velocidade()
    {
        Console.WriteLine("A velocidade, sobre 10 MB");
        Console.WriteLine();

        var dados = new byte[10 * 1024 * 1024];

        new Random(29).NextBytes(dados);

        var crc = new Soma(Catalogo.Crc32);

        var relogio = Stopwatch.StartNew();

        var porTabela = crc.De(dados);

        var tempoTabela = relogio.Elapsed.TotalMilliseconds;

        // O bit a bit é oito vezes mais trabalho por byte; 10 MB levariam
        // segundos, então mede-se sobre um pedaço e extrapola-se.
        var pedaco = dados.AsSpan(0, 256 * 1024);

        relogio.Restart();

        crc.DeBitABit(pedaco);

        var tempoPedaco = relogio.Elapsed.TotalMilliseconds;

        var tempoBitABit = tempoPedaco * dados.Length / pedaco.Length;

        relogio.Restart();

        var adler = Adler.De(dados);

        var tempoAdler = relogio.Elapsed.TotalMilliseconds;

        Console.WriteLine("  cálculo                    tempo      MB/s");
        Console.WriteLine("  " + new string('-', 46));
        Console.WriteLine($"  CRC-32 por tabela     {tempoTabela,9:F1} ms {10_000 / tempoTabela,9:F0}");
        Console.WriteLine($"  CRC-32 bit a bit      {tempoBitABit,9:F1} ms {10_000 / tempoBitABit,9:F0}"
            + "   (extrapolado de 256 kB)");
        Console.WriteLine($"  Adler-32              {tempoAdler,9:F1} ms {10_000 / tempoAdler,9:F0}");

        Console.WriteLine();
        Console.WriteLine($"  A tabela é {tempoBitABit / tempoTabela:F0} vezes mais rápida que a definição, e a ideia");
        Console.WriteLine("  é de 1988, de Dilip Sarwate: em vez de oito deslocamentos por byte, um");
        Console.WriteLine("  acesso a tabela e um ou-exclusivo. Como a divisão é linear, o resto de");
        Console.WriteLine("  cada byte no topo do registrador dá para calcular antes.");
        Console.WriteLine();
        Console.WriteLine($"  E o Adler é {tempoTabela / tempoAdler:F1} vezes mais rápido que o CRC por tabela -- que era");
        Console.WriteLine("  exatamente a meta de Mark Adler em 1995. Ele não tem tabela nenhuma:");
        Console.WriteLine("  são duas somas, e um módulo a cada 5.552 bytes.");
        Console.WriteLine();
        Console.WriteLine($"  (controle: crc 0x{porTabela:X8}, adler 0x{adler:X8})");
        Console.WriteLine();
        Console.WriteLine("  O que falta aqui é a instrução do processador: desde 2008 o x86 calcula");
        Console.WriteLine("  o CRC-32C em hardware, e ali a tabela perde por outra ordem de grandeza.");
        Console.WriteLine("  É o motivo real de o polinômio de Castagnoli ter se espalhado.");
    }

    private const string Ajuda = """
        crc -- CRC e somas de verificação do zero

          medidor catalogo        os dezoito modelos, e o conferidor de cada um
          medidor deteccao        quantos erros escapam de cada soma
          medidor zeros           por que o registrador começa em uns
          medidor texto [TEXTO]   um texto em todos os modelos
          medidor velocidade      tabela contra bit a bit, e o Adler
        """;
}
