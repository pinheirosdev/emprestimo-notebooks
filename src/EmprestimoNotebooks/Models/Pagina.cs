namespace EmprestimoNotebooks.Models;

public record Pagina<T>(IReadOnlyList<T> Itens, int Atual, int TotalPaginas, int TotalItens)
{
    public static Pagina<T> Criar(IReadOnlyList<T> itens, int pagina, int limite)
    {
        var totalPaginas = Math.Max(1, (int)Math.Ceiling(itens.Count / (double)limite));
        var atual = Math.Clamp(pagina, 1, totalPaginas);
        var registros = itens.Skip((atual - 1) * limite).Take(limite).ToList();
        return new Pagina<T>(registros, atual, totalPaginas, itens.Count);
    }
}

public record NavegacaoPagina(int Atual, int TotalPaginas, string Rotulo, Func<int, string?> Link)
{
    public IEnumerable<int> Numeros()
    {
        if (TotalPaginas <= 7)
        {
            return Enumerable.Range(1, TotalPaginas);
        }

        var numeros = new SortedSet<int> { 1, Atual, TotalPaginas };
        if (Atual > 1) numeros.Add(Atual - 1);
        if (Atual < TotalPaginas) numeros.Add(Atual + 1);
        if (numeros.Contains(3)) numeros.Add(2);
        if (numeros.Contains(TotalPaginas - 2)) numeros.Add(TotalPaginas - 1);
        return numeros;
    }
}
