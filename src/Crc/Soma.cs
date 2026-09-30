namespace Conde.Crc;

/// <summary>
/// O cálculo — bit a bit e por tabela, que têm de dar o mesmo.
/// </summary>
/// <remarks>
/// <para>
/// Um CRC é o <b>resto da divisão</b> da mensagem por um polinômio, em aritmética
/// binária — onde somar e subtrair são a mesma coisa, o ou-exclusivo, e não há
/// "vai um".
/// </para>
/// <para>
/// Essa é a diferença entre um CRC e uma soma comum, e é o que ele compra: numa
/// soma, trocar dois bytes de lugar não muda nada, e trocar dois bits em colunas
/// opostas se cancela. Numa divisão polinomial, a <b>posição</b> de cada bit
/// importa — e daí saem garantias que uma soma não dá:
/// </para>
/// <list type="bullet">
///   <item><description>todo erro de <b>um bit</b> é detectado</description></item>
///   <item><description>toda <b>rajada</b> de até <c>largura</c> bits é detectada, sempre</description></item>
///   <item><description>todo erro de número <b>ímpar</b> de bits, se o polinômio for divisível por <c>x+1</c></description></item>
///   <item><description>dos erros maiores, escapam <c>2^-largura</c> — uma em quatro bilhões para 32 bits</description></item>
/// </list>
/// </remarks>
public sealed class Soma
{
    private readonly ulong[] _tabela = new ulong[256];

    /// <summary>O modelo.</summary>
    public Modelo Modelo { get; }

    /// <summary>Monta o calculador e a tabela de 256 entradas.</summary>
    public Soma(Modelo modelo)
    {
        ArgumentNullException.ThrowIfNull(modelo);

        if (modelo.Largura is < 3 or > 64)
        {
            throw new ArgumentOutOfRangeException(
                nameof(modelo), modelo.Largura, "a largura vai de 3 a 64");
        }

        Modelo = modelo;

        MontarTabela();
    }

    /// <summary>
    /// A tabela: o resto de cada um dos 256 bytes possíveis, calculado uma vez.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A ideia é de 1988, de Dilip Sarwate, e é o que tornou o CRC barato: em vez
    /// de oito passos de deslocamento por byte, um acesso a tabela e um
    /// ou-exclusivo. Como a divisão é linear, o resto de um byte no topo do
    /// registrador <b>não depende</b> do resto da mensagem — dá para calcular os
    /// 256 antes e consultar.
    /// </para>
    /// <para>
    /// A tabela é montada nas duas formas conforme a reflexão. Na forma
    /// refletida, o polinômio é espelhado e o registrador anda para a direita;
    /// na forma direta, ele anda para a esquerda. Fazer a reflexão byte a byte
    /// na hora, em vez de embutir na tabela, custaria mais que a tabela economiza.
    /// </para>
    /// </remarks>
    private void MontarTabela()
    {
        var largura = Modelo.Largura;
        var mascara = Modelo.Mascara;

        if (Modelo.RefletirEntrada)
        {
            var polinomio = Espelhar(Modelo.Polinomio, largura);

            for (var i = 0; i < 256; i++)
            {
                var resto = (ulong)i;

                for (var bit = 0; bit < 8; bit++)
                {
                    resto = (resto & 1) != 0 ? (resto >> 1) ^ polinomio : resto >> 1;
                }

                _tabela[i] = resto & mascara;
            }
        }
        else
        {
            // O byte entra no TOPO do registrador. Para larguras menores que 8 --
            // um CRC-3, um CRC-5 -- o topo fica abaixo do byte, e é preciso
            // trabalhar numa largura de pelo menos 8.
            var deslocamento = Math.Max(largura, 8) - 8;

            var polinomio = largura < 8
                ? Modelo.Polinomio << (8 - largura)
                : Modelo.Polinomio;

            var bitDoTopo = 1UL << (Math.Max(largura, 8) - 1);

            var mascaraDeTrabalho = Math.Max(largura, 8) == 64
                ? ulong.MaxValue
                : (1UL << Math.Max(largura, 8)) - 1;

            for (var i = 0; i < 256; i++)
            {
                var resto = (ulong)i << deslocamento;

                for (var bit = 0; bit < 8; bit++)
                {
                    resto = (resto & bitDoTopo) != 0
                        ? ((resto << 1) ^ polinomio) & mascaraDeTrabalho
                        : (resto << 1) & mascaraDeTrabalho;
                }

                _tabela[i] = resto;
            }
        }
    }

    /// <summary>Espelha os <paramref name="quantos"/> bits de baixo de um valor.</summary>
    public static ulong Espelhar(ulong valor, int quantos)
    {
        ulong espelhado = 0;

        for (var i = 0; i < quantos; i++)
        {
            espelhado = (espelhado << 1) | ((valor >> i) & 1);
        }

        return espelhado;
    }

    /// <summary>O CRC dos bytes, pela tabela.</summary>
    public ulong De(ReadOnlySpan<byte> bytes) => Terminar(Continuar(Comecar(), bytes));

    /// <summary>O registrador inicial, já ajustado à forma.</summary>
    public ulong Comecar()
    {
        var inicial = Modelo.Inicial;

        if (Modelo.RefletirEntrada)
        {
            return Espelhar(inicial, Modelo.Largura);
        }

        // Numa largura menor que 8, o registrador trabalha alinhado ao topo do
        // byte -- e o inicial precisa ser subido junto.
        return Modelo.Largura < 8 ? inicial << (8 - Modelo.Largura) : inicial;
    }

    /// <summary>Continua o cálculo sobre mais bytes — para fluxo.</summary>
    public ulong Continuar(ulong registrador, ReadOnlySpan<byte> bytes)
    {
        var largura = Math.Max(Modelo.Largura, 8);

        if (Modelo.RefletirEntrada)
        {
            foreach (var bruto in bytes)
            {
                registrador = _tabela[(registrador ^ bruto) & 0xFF] ^ (registrador >> 8);
            }

            return registrador & Modelo.Mascara;
        }

        var deslocamento = largura - 8;

        var mascaraDeTrabalho = largura == 64 ? ulong.MaxValue : (1UL << largura) - 1;

        foreach (var bruto in bytes)
        {
            registrador = _tabela[((registrador >> deslocamento) ^ bruto) & 0xFF]
                ^ ((registrador << 8) & mascaraDeTrabalho);
        }

        return registrador;
    }

    /// <summary>Fecha o cálculo: espelha se for o caso, corta e soma o final.</summary>
    public ulong Terminar(ulong registrador)
    {
        if (Modelo.RefletirEntrada != Modelo.RefletirSaida)
        {
            registrador = Espelhar(registrador, Math.Max(Modelo.Largura, 8));

            if (Modelo.Largura < 8 && !Modelo.RefletirEntrada)
            {
                registrador >>= 8 - Modelo.Largura;
            }
        }
        else if (!Modelo.RefletirEntrada && Modelo.Largura < 8)
        {
            registrador >>= 8 - Modelo.Largura;
        }

        return (registrador ^ Modelo.OuExclusivoFinal) & Modelo.Mascara;
    }

    /// <summary>
    /// O CRC calculado <b>bit a bit</b>, sem tabela — a definição, escrita direto.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Esta é a implementação lenta, e ela existe por um motivo: é a <b>definição</b>
    /// do CRC, e a versão por tabela é uma otimização dela. As duas têm de
    /// concordar em toda entrada, e é isso que um dos testes exige.
    /// </para>
    /// <para>
    /// Uma tabela montada errada produz um CRC que parece perfeitamente
    /// aleatório, muda quando os dados mudam, e está errado. Comparar com a
    /// definição é a única forma de pegar isso sem um valor publicado.
    /// </para>
    /// </remarks>
    public ulong DeBitABit(ReadOnlySpan<byte> bytes)
    {
        var largura = Modelo.Largura;

        var bitDoTopo = 1UL << (largura - 1);

        var mascara = Modelo.Mascara;

        var registrador = Modelo.Inicial & mascara;

        foreach (var bruto in bytes)
        {
            var oByte = Modelo.RefletirEntrada ? Espelhar(bruto, 8) : bruto;

            // O byte entra pelo topo do registrador, um bit de cada vez.
            for (var bit = 7; bit >= 0; bit--)
            {
                var entrou = ((oByte >> bit) & 1) != 0;

                var saiu = (registrador & bitDoTopo) != 0;

                registrador = (registrador << 1) & mascara;

                if (entrou != saiu)
                {
                    registrador ^= Modelo.Polinomio;
                }
            }
        }

        if (Modelo.RefletirSaida)
        {
            registrador = Espelhar(registrador, largura);
        }

        return (registrador ^ Modelo.OuExclusivoFinal) & mascara;
    }

    /// <summary>
    /// O resíduo: o que sobra ao calcular o CRC da mensagem <b>com o CRC junto</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// É uma constante que só depende do modelo, e é a propriedade que faz o CRC
    /// ser barato em hardware: um receptor não precisa calcular o CRC da mensagem
    /// e comparar com o que veio — basta passar <b>tudo</b>, mensagem e CRC, pelo
    /// mesmo registrador e olhar se o que sobra é a constante.
    /// </para>
    /// <para>
    /// Uma comparação, sempre a mesma, para qualquer tamanho de quadro. É assim
    /// que uma placa de rede confere um pacote de Ethernet.
    /// </para>
    /// </remarks>
    public ulong Residuo()
    {
        // Calcula sobre uma mensagem qualquer seguida do seu próprio CRC.
        var mensagem = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };

        var crc = De(mensagem);

        var comCrc = new byte[mensagem.Length + (Modelo.Largura + 7) / 8];

        mensagem.CopyTo(comCrc, 0);

        EscreverCrc(crc, comCrc.AsSpan(mensagem.Length));

        return De(comCrc);
    }

    /// <summary>Escreve o CRC nos bytes, na ordem que o modelo implica.</summary>
    /// <remarks>
    /// A ordem dos bytes segue a reflexão: um modelo refletido põe o byte menos
    /// significativo primeiro, e um direto põe o mais significativo. É a mesma
    /// razão da reflexão em si — a ordem em que o hardware serial transmite.
    /// </remarks>
    public void EscreverCrc(ulong crc, Span<byte> destino)
    {
        var quantos = (Modelo.Largura + 7) / 8;

        if (destino.Length < quantos)
        {
            throw new ArgumentException($"precisa de {quantos} byte(s)", nameof(destino));
        }

        for (var i = 0; i < quantos; i++)
        {
            destino[i] = Modelo.RefletirSaida
                ? (byte)(crc >> (8 * i))
                : (byte)(crc >> (8 * (quantos - 1 - i)));
        }
    }

    public override string ToString() => Modelo.ToString();
}
