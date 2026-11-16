namespace EmprestimoNotebooks.Dominio;

public class Aluno
{
    public int Id { get; set; }
    public required string NomeCompleto { get; set; }
    public required string Matricula { get; set; }
    public required string Turma { get; set; }
    public required byte[] FotoJpeg { get; set; }
    public DateTime? CadastradoEmUtc { get; set; }
}
