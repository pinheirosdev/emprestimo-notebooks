namespace EmprestimoNotebooks.Dominio;

public class ManutencaoNotebook
{
    public int Id { get; set; }
    public int NotebookId { get; set; }
    public DateTime AlteradoEmUtc { get; set; }
    public bool EmManutencao { get; set; }
}
