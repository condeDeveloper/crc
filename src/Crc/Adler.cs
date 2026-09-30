namespace Conde.Crc;

/// <summary>
/// Adler-32 e Fletcher — as somas que não são CRC, e o que elas trocam.
/// </summary>
/// <remarks>
/// <para>
/// Mark Adler desenhou o Adler-32 para o <c>zlib</c> em 1995, com uma meta
/// explícita: ser <b>muito mais rápido</b> que um CRC-32 e quase tão bom. Ele é
/// duas somas correndo juntas, sem tabela e sem deslocamento nenhum.
/// </para>
/// <code>
///   a = 1 + d₁ + d₂ + … + dₙ           (mod 65521)
///   b = (1 + d₁) + (1 + d₁ + d₂) + …   (mod 65521)
///   adler = b · 65536 + a
/// </code>
/// <para>
/// O <c>a</c> é a soma dos bytes, e sozinho não veria uma troca de ordem. O
/// <c>b</c> é a soma das somas parciais, e é ele que faz a <b>posição</b>
/// importar: um byte no começo entra em todas as parcelas, um no fim entra em
/// uma só.
/// </para>
/// <para>
/// <b>65521 é o maior primo abaixo de 65536</b>, e ser primo é o ponto: com um
/// módulo composto, os valores se concentrariam em certos resíduos. Ser
/// <b>abaixo</b> de 65536 é o preço — sobram 15 valores que a soma nunca produz,
/// e é a razão de o Adler-32 ter menos que 32 bits de qualidade real.
/// </para>
/// <para>
/// O preço maior está em mensagens <b>curtas</b>: com poucos bytes, o <c>a</c>
/// mal sai de 1 e o <c>b</c> cresce devagar, então o valor usa uma fração
/// pequena do espaço. Para menos de uns cem bytes, o Adler-32 é claramente pior
/// que um CRC-32 — e o <c>zlib</c> o usa mesmo assim, porque o caso dele é o
/// arquivo grande.
/// </para>
/// </remarks>
public static class Adler
{
    /// <summary>O módulo: o maior primo abaixo de 65536.</summary>
    public const int Primo = 65_521;

    /// <summary>O Adler-32 dos bytes.</summary>
    public static uint De(ReadOnlySpan<byte> bytes) => Continuar(1, bytes);

    /// <summary>Continua o cálculo sobre mais bytes — para fluxo.</summary>
    /// <remarks>
    /// <para>
    /// O valor inicial é <b>1</b>, e não zero. Sem isso, uma mensagem só de zeros
    /// teria Adler zero de qualquer tamanho — e acrescentar zeros no começo não
    /// mudaria nada. O 1 faz o comprimento entrar na conta.
    /// </para>
    /// <para>
    /// O laço só tira o módulo a cada 5.552 bytes, que é o maior número de bytes
    /// de valor 255 que cabe antes de o <c>b</c> estourar 32 bits. É a otimização
    /// do <c>zlib</c> original, e sem ela haveria duas divisões por byte.
    /// </para>
    /// </remarks>
    public static uint Continuar(uint anterior, ReadOnlySpan<byte> bytes)
    {
        var a = anterior & 0xFFFF;
        var b = (anterior >> 16) & 0xFFFF;

        const int Bloco = 5_552;

        var onde = 0;

        while (onde < bytes.Length)
        {
            var ate = Math.Min(onde + Bloco, bytes.Length);

            for (; onde < ate; onde++)
            {
                a += bytes[onde];
                b += a;
            }

            a %= Primo;
            b %= Primo;
        }

        return (b << 16) | a;
    }

    /// <summary>
    /// Fletcher-16 — a mesma ideia, oito anos antes e com módulo 255.
    /// </summary>
    /// <remarks>
    /// <para>
    /// John Fletcher, na Lawrence Livermore, 1982. A estrutura é idêntica à do
    /// Adler, e a diferença é o módulo: 255 em vez de 65521. O módulo 255 é
    /// barato — dá para fazer com somas, sem divisão — e é composto, o que
    /// custa qualidade.
    /// </para>
    /// <para>
    /// A fraqueza conhecida: com módulo 255, os bytes <c>0x00</c> e <c>0xFF</c>
    /// são <b>equivalentes</b>, porque 255 ≡ 0. Trocar um pelo outro não muda a
    /// soma. O Adler trocou isso por uma divisão de verdade.
    /// </para>
    /// </remarks>
    public static ushort Fletcher16(ReadOnlySpan<byte> bytes)
    {
        uint a = 0;
        uint b = 0;

        foreach (var bruto in bytes)
        {
            a = (a + bruto) % 255;
            b = (b + a) % 255;
        }

        return (ushort)((b << 8) | a);
    }

    /// <summary>Fletcher-32 — em palavras de 16 bits, com módulo 65535.</summary>
    public static uint Fletcher32(ReadOnlySpan<ushort> palavras)
    {
        uint a = 0;
        uint b = 0;

        foreach (var palavra in palavras)
        {
            a = (a + palavra) % 65_535;
            b = (b + a) % 65_535;
        }

        return (b << 16) | a;
    }

    /// <summary>
    /// A soma do cabeçalho IP, da RFC 1071 — o complemento de um em 16 bits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// É a mais fraca das somas em uso, e está em toda parte: no cabeçalho IP, no
    /// TCP e no UDP. Ela soma palavras de 16 bits em complemento de um — o que
    /// significa que o "vai um" volta para o bit de baixo — e inverte no fim.
    /// </para>
    /// <para>
    /// A escolha foi de 1981 e é defensável pelo que ela dá de graça: é
    /// <b>independente da ordem dos bytes</b> da máquina, e é <b>incremental</b>
    /// — um roteador que decrementa o TTL pode corrigir a soma com duas
    /// operações, sem reler o cabeçalho.
    /// </para>
    /// <para>
    /// O preço é que ela não vê troca de palavras de lugar, e não vê dois erros
    /// que se cancelem. Em 2000, um estudo do tráfego da internet encontrou
    /// pacotes corrompidos passando por ela na ordem de <b>um em bilhões</b> —
    /// o que, no volume de hoje, é muito.
    /// </para>
    /// </remarks>
    public static ushort Internet(ReadOnlySpan<byte> bytes)
    {
        uint soma = 0;

        var onde = 0;

        for (; onde + 1 < bytes.Length; onde += 2)
        {
            soma += (uint)((bytes[onde] << 8) | bytes[onde + 1]);
        }

        // Um byte sobrando é completado com zero à direita.
        if (onde < bytes.Length)
        {
            soma += (uint)(bytes[onde] << 8);
        }

        // O "vai um" volta para baixo -- é o que faz o complemento de um.
        while ((soma >> 16) != 0)
        {
            soma = (soma & 0xFFFF) + (soma >> 16);
        }

        return (ushort)~soma;
    }
}
