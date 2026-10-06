using Xunit;

// Garante execução sequencial dos testes para evitar concorrência em variáveis estáticas e arquivos temporários
[assembly: CollectionBehavior(DisableTestParallelization = true)]
