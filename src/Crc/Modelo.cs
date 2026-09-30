namespace Conde.Crc;

/// <summary>
/// Um modelo de CRC — os seis parâmetros que definem qualquer um deles.
/// </summary>
/// <remarks>
/// <para>
/// "CRC-16" não identifica nada. Há dezenas de CRC de 16 bits em uso, todos
/// incompatíveis entre si, e o que os separa são seis números. Ross Williams
/// catalogou isso em 1993 — o "modelo de Rocksoft" — e é a única forma
/// desambígua de dizer qual CRC se está usando:
/// </para>
/// <list type="table">
///   <item><term><c>largura</c></term><description>quantos bits o resultado tem</description></item>
///   <item><term><c>polinômio</c></term><description>o divisor, sem o bit mais alto, que é implícito</description></item>
///   <item><term><c>inicial</c></term><description>com o que o registrador começa</description></item>
///   <item><term><c>refletirEntrada</c></term><description>se cada byte entra do bit menos significativo para o mais</description></item>
///   <item><term><c>refletirSaida</c></term><description>se o resultado é espelhado no fim</description></item>
///   <item><term><c>ouExclusivoFinal</c></term><description>com o que o resultado é somado no fim</description></item>
/// </list>
/// <para>
/// E há um sétimo campo que não é parâmetro: o <b>conferidor</b>, que é o CRC da
/// cadeia ASCII <c>"123456789"</c>. Ele existe no catálogo para que qualquer
/// implementação possa provar, com uma linha, que implementou o modelo certo — e
/// é exatamente para isso que ele é usado aqui.
/// </para>
/// <para>
/// O "refletir" é o parâmetro que mais confunde, e ele não é capricho: vem do
/// hardware. Um transmissor serial que manda o bit menos significativo primeiro
/// calcula o CRC nessa ordem, e foi assim que a metade dos CRC do mundo ficou
/// refletida e a outra metade não.
/// </para>
/// </remarks>
/// <param name="Nome">O nome do catálogo.</param>
/// <param name="Largura">Quantos bits — de 3 a 64.</param>
/// <param name="Polinomio">O divisor, sem o bit implícito do topo.</param>
/// <param name="Inicial">O valor inicial do registrador.</param>
/// <param name="RefletirEntrada">Cada byte entra espelhado?</param>
/// <param name="RefletirSaida">O resultado sai espelhado?</param>
/// <param name="OuExclusivoFinal">O que se soma ao resultado no fim.</param>
/// <param name="Conferidor">O CRC de <c>"123456789"</c>, publicado no catálogo.</param>
public sealed record Modelo(
    string Nome,
    int Largura,
    ulong Polinomio,
    ulong Inicial,
    bool RefletirEntrada,
    bool RefletirSaida,
    ulong OuExclusivoFinal,
    ulong Conferidor)
{
    /// <summary>A máscara que corta o resultado na largura certa.</summary>
    public ulong Mascara => Largura == 64 ? ulong.MaxValue : (1UL << Largura) - 1;

    public override string ToString() =>
        $"{Nome} (largura={Largura} poli=0x{Polinomio:X} inicial=0x{Inicial:X} "
        + $"refin={RefletirEntrada} refout={RefletirSaida} xorout=0x{OuExclusivoFinal:X})";
}

/// <summary>
/// O catálogo — os modelos que se encontram no mundo, com o conferidor de cada um.
/// </summary>
/// <remarks>
/// Todos os valores aqui, incluindo o conferidor, vêm do catálogo de Greg Cook,
/// que é a continuação do trabalho de Ross Williams e a referência de fato.
/// Nenhum deles foi calculado por mim — e é isso que faz deles um juiz.
/// </remarks>
public static class Catalogo
{
    /// <summary>CRC-32/ISO-HDLC — o do zip, do gzip, do PNG e do Ethernet.</summary>
    /// <remarks>
    /// É o CRC mais usado do mundo, e tem quatro outros nomes: CRC-32, CRC-32/ADCCP,
    /// PKZIP e, no <c>zlib</c>, simplesmente <c>crc32</c>.
    /// </remarks>
    public static Modelo Crc32 { get; } = new(
        "CRC-32/ISO-HDLC", 32, 0x04C11DB7, 0xFFFFFFFF, true, true, 0xFFFFFFFF, 0xCBF43926);

    /// <summary>CRC-32C — o de Castagnoli, que tem instrução própria no processador.</summary>
    /// <remarks>
    /// <para>
    /// O polinômio de Castagnoli, de 1993, foi escolhido por busca exaustiva para
    /// ter distância de Hamming melhor que a do CRC-32 clássico em mensagens
    /// longas. O iSCSI, o SCTP, o Btrfs e o ext4 o usam.
    /// </para>
    /// <para>
    /// E desde o Nehalem, em 2008, o x86 tem a instrução <c>CRC32</c> que o
    /// calcula em <b>hardware</b> — o que o torna uma ordem de grandeza mais
    /// rápido que qualquer tabela. É o motivo real de ele ter se espalhado.
    /// </para>
    /// </remarks>
    public static Modelo Crc32C { get; } = new(
        "CRC-32/ISCSI", 32, 0x1EDC6F41, 0xFFFFFFFF, true, true, 0xFFFFFFFF, 0xE3069283);

    /// <summary>CRC-32/BZIP2 — o mesmo polinômio do CRC-32, sem reflexão nenhuma.</summary>
    public static Modelo Crc32Bzip2 { get; } = new(
        "CRC-32/BZIP2", 32, 0x04C11DB7, 0xFFFFFFFF, false, false, 0xFFFFFFFF, 0xFC891918);

    /// <summary>CRC-32/MPEG-2 — igual ao do BZIP2, mas sem o ou-exclusivo final.</summary>
    /// <remarks>
    /// Os três — CRC-32, BZIP2 e MPEG-2 — têm o <b>mesmo polinômio</b> e dão
    /// resultados completamente diferentes. É a prova de que dizer "usamos
    /// CRC-32 com o polinômio 0x04C11DB7" não especifica nada.
    /// </remarks>
    public static Modelo Crc32Mpeg2 { get; } = new(
        "CRC-32/MPEG-2", 32, 0x04C11DB7, 0xFFFFFFFF, false, false, 0x00000000, 0x0376E6E7);

    /// <summary>CRC-32/CKSUM — o do comando <c>cksum</c> do POSIX.</summary>
    public static Modelo Crc32Cksum { get; } = new(
        "CRC-32/CKSUM", 32, 0x04C11DB7, 0x00000000, false, false, 0xFFFFFFFF, 0x765E7680);

    /// <summary>CRC-32/JAMCRC — o do CRC-32 sem o ou-exclusivo final.</summary>
    public static Modelo Crc32Jam { get; } = new(
        "CRC-32/JAMCRC", 32, 0x04C11DB7, 0xFFFFFFFF, true, true, 0x00000000, 0x340BC6D9);

    /// <summary>CRC-16/ARC — o mais antigo, do LHA e do Modbus antigo.</summary>
    public static Modelo Crc16Arc { get; } = new(
        "CRC-16/ARC", 16, 0x8005, 0x0000, true, true, 0x0000, 0xBB3D);

    /// <summary>CRC-16/IBM-3740 — o que quase todo mundo chama, errado, de "CCITT".</summary>
    /// <remarks>
    /// O apelido <c>CRC-16/CCITT-FALSE</c> é literalmente o nome que o catálogo
    /// lhe dá, e a história é boa: durante anos circulou código rotulado
    /// "CCITT" que não era o CCITT de verdade. O catálogo registrou o engano no
    /// próprio nome, e o engano se tornou o padrão.
    /// </remarks>
    public static Modelo Crc16CcittFalse { get; } = new(
        "CRC-16/IBM-3740", 16, 0x1021, 0xFFFF, false, false, 0x0000, 0x29B1);

    /// <summary>CRC-16/XMODEM — o mesmo polinômio, começando em zero.</summary>
    public static Modelo Crc16Xmodem { get; } = new(
        "CRC-16/XMODEM", 16, 0x1021, 0x0000, false, false, 0x0000, 0x31C3);

    /// <summary>CRC-16/KERMIT — o CCITT de verdade, refletido.</summary>
    public static Modelo Crc16Kermit { get; } = new(
        "CRC-16/KERMIT", 16, 0x1021, 0x0000, true, true, 0x0000, 0x2189);

    /// <summary>CRC-16/MODBUS — o do protocolo industrial.</summary>
    public static Modelo Crc16Modbus { get; } = new(
        "CRC-16/MODBUS", 16, 0x8005, 0xFFFF, true, true, 0x0000, 0x4B37);

    /// <summary>CRC-16/USB.</summary>
    public static Modelo Crc16Usb { get; } = new(
        "CRC-16/USB", 16, 0x8005, 0xFFFF, true, true, 0xFFFF, 0xB4C8);

    /// <summary>CRC-8/SMBUS — o CRC-8 clássico.</summary>
    public static Modelo Crc8 { get; } = new(
        "CRC-8/SMBUS", 8, 0x07, 0x00, false, false, 0x00, 0xF4);

    /// <summary>CRC-8/MAXIM-DOW — o do barramento 1-Wire da Dallas.</summary>
    public static Modelo Crc8Maxim { get; } = new(
        "CRC-8/MAXIM-DOW", 8, 0x31, 0x00, true, true, 0x00, 0xA1);

    /// <summary>CRC-64/XZ — o do formato <c>.xz</c>.</summary>
    public static Modelo Crc64Xz { get; } = new(
        "CRC-64/XZ", 64, 0x42F0E1EBA9EA3693, 0xFFFFFFFFFFFFFFFF, true, true,
        0xFFFFFFFFFFFFFFFF, 0x995DC9BBDF1939FA);

    /// <summary>CRC-64/ECMA-182 — o do formato DLT e da norma ECMA-182.</summary>
    public static Modelo Crc64Ecma { get; } = new(
        "CRC-64/ECMA-182", 64, 0x42F0E1EBA9EA3693, 0x0000000000000000, false, false,
        0x0000000000000000, 0x6C40DF5F0B497347);

    /// <summary>CRC-5/USB — um dos menores em uso.</summary>
    public static Modelo Crc5Usb { get; } = new(
        "CRC-5/USB", 5, 0x05, 0x1F, true, true, 0x1F, 0x19);

    /// <summary>CRC-3/GSM — o menor do catálogo.</summary>
    public static Modelo Crc3Gsm { get; } = new(
        "CRC-3/GSM", 3, 0x3, 0x0, false, false, 0x7, 0x4);

    /// <summary>Todos os modelos, para percorrer.</summary>
    public static IReadOnlyList<Modelo> Todos { get; } =
    [
        Crc3Gsm, Crc5Usb,
        Crc8, Crc8Maxim,
        Crc16Arc, Crc16CcittFalse, Crc16Xmodem, Crc16Kermit, Crc16Modbus, Crc16Usb,
        Crc32, Crc32C, Crc32Bzip2, Crc32Mpeg2, Crc32Cksum, Crc32Jam,
        Crc64Xz, Crc64Ecma,
    ];
}
