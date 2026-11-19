# Empréstimo de Notebooks

Aplicação web para controlar a retirada e a devolução de notebooks emprestados a alunos durante as aulas. Projeto da disciplina de Projeto de Desenvolvimento de Software da UniFil.

## Stack

| Camada | Tecnologia |
| --- | --- |
| Aplicação | C# com .NET 10 e ASP.NET Core MVC |
| Interface | Views Razor, CSS e JavaScript |
| Banco | SQLite com Entity Framework Core |
| Autenticação | Login do atendente com sessão por cookie |
| Execução | Docker Compose |

## Funcionalidades

1. Entrar e sair da conta de atendente.
2. Cadastrar, consultar e editar notebooks, alterar a manutenção e inativar ou reativar equipamentos sem perder o histórico. A inativação exige que não haja empréstimo em aberto; a exclusão só é permitida sem empréstimos ou registros de manutenção.
3. Cadastrar alunos com nome, matrícula, turma e foto. Buscar por nome ou matrícula.
4. Registrar a retirada com uma foto do aluno e outra do notebook. O sistema bloqueia a retirada de equipamentos inativos, em manutenção ou já emprestados.
5. Registrar a devolução com novas fotos do aluno e do notebook e uma observação opcional.
6. Consultar empréstimos em aberto e o histórico mensal de retiradas, devoluções, cadastros e mudanças de manutenção.

## Como rodar

O Docker usa a imagem do SDK do .NET 10 para compilar o projeto e a imagem do ASP.NET Core para executar a aplicação. Para rodar dessa forma, basta ter o Docker com Compose instalado. No Windows, abra o Docker Desktop e execute na raiz do repositório, no PowerShell:

```powershell
Copy-Item .env.example .env
```

No `.env`, preencha `ATENDENTE_SENHA` entre aspas simples. O usuário padrão é `atendente`. Em seguida, execute:

```powershell
docker compose up --build -d
```

Abra [http://127.0.0.1:5180](http://127.0.0.1:5180/) e entre com as credenciais do `.env`. Para tirar fotos, permita o acesso à câmera no navegador.

Na primeira execução, a aplicação cria o banco e carrega seis notebooks de demonstração. Para parar a aplicação e manter os dados, execute `docker compose down`.

## Testes

Com o Docker aberto, execute na raiz do repositório:

```powershell
docker build --target testes --progress plain --output=type=cacheonly .
```

O Docker executa a suíte com o SDK do .NET 10 em uma etapa própria. Os testes de integração usam um SQLite temporário. A suíte cobre login, inventário, inativação e reativação, cadastro de alunos, fotos, retirada, devolução, histórico e paginação. Os testes também verificam que duas retiradas simultâneas geram apenas um empréstimo e que uma retirada concorrente com a inativação não deixa um notebook inativo com empréstimo aberto.

## Estrutura

```text
src/EmprestimoNotebooks/
  Controllers/   Ações da aplicação
  Views/         Telas Razor
  Models/        Dados dos formulários e telas
  Dominio/       Entidades do sistema
  Dados/         Acesso ao SQLite e migrações
  Servicos/      Validação e tratamento de fotos
  wwwroot/       CSS e JavaScript
tests/EmprestimoNotebooks.Tests/
                 Testes de integração
```

## Documentação

O [relatório do projeto em PDF](docs/documentacao.pdf) reúne os requisitos, os casos de uso e os diagramas.
