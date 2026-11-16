namespace EmprestimoNotebooks.Dominio;

public class Notebook
{
    public int Id { get; set; }
    public required string Patrimonio { get; set; }
    public required string Marca { get; set; }
    public required string Modelo { get; set; }
    public bool EmManutencao { get; set; }
    public DateTime? CadastradoEmUtc { get; set; }
}
