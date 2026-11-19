namespace EmprestimoNotebooks.Models;

public record InventarioFormulario(Pagina<NotebookResumo> Notebooks, int Disponiveis, int Emprestados, int EmManutencao, int Inativos);
