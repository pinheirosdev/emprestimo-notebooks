namespace EmprestimoNotebooks.Dominio;

public class Emprestimo
{
    public int Id { get; set; }
    public int NotebookId { get; set; }
    public int AlunoId { get; set; }
    public DateTime RetiradoEmUtc { get; set; }
    public DateTime? DevolvidoEmUtc { get; set; }
    public required byte[] FotoRetiradaJpeg { get; set; }
    public byte[]? FotoNotebookRetiradaJpeg { get; set; }
    public byte[]? FotoDevolucaoJpeg { get; set; }
    public byte[]? FotoNotebookJpeg { get; set; }
    public string? ObservacaoDevolucao { get; set; }
}
