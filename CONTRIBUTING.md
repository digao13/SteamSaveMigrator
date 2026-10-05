# Guia de Contribuição (Contributing Guide)

Obrigado pelo interesse em contribuir com o **SteamSaveMigrator**! 🎉

Este projeto é de código aberto e todas as contribuições, sugestões de melhorias, correções de bugs e adições de novos jogos ao banco de dados são muito bem-vindas.

---

## ⚠️ Atenção Especial ao Fazer Fork (Informações Restritas)

Ao fazer fork deste repositório e enviar Pull Requests (PRs), tome os seguintes cuidados obrigatórios:

1. **Nunca envie arquivos de saves ou backups pessoais**:
   - A pasta `backups/` e arquivos `.zip` estão no `.gitignore`. Certifique-se de não forçar a inclusão (`git add -f`) de nenhum arquivo pessoal.
2. **Nunca envie segredos ou credenciais**:
   - Nunca comite arquivos `credentials.json`, `client_secret.json`, `client_secrets.cfg` ou chaves de API.
   - Utilize as variáveis de ambiente `GOOGLE_CLIENT_ID` e `GOOGLE_CLIENT_SECRET` para testes locais.
3. **Mantenha os testes automatizados com dados genéricos e anonimizados**:
   - Use nomes de usuário fictícios (ex: `GamerOne`, `TestUser`, `Alice`) e nunca dados de contas reais.

---

## 🛠️ Ambiente de Desenvolvimento

### Pré-requisitos
- [.NET SDK](https://dotnet.microsoft.com/download) (.NET 8.0, 9.0 ou superior)
- Windows 10 ou 11 (para execução do WPF)
- Visual Studio 2022+ ou VS Code / Rider

### Compilação e Testes

Para compilar a solução completa:
```powershell
dotnet build SteamSaveMigrator.slnx -c Release
```

Para rodar todos os testes automatizados com xUnit:
```powershell
dotnet test
```

---

## 🔄 Fluxo de Contribuição

1. Faça um **Fork** do repositório no GitHub.
2. Crie uma branch para sua funcionalidade ou correção:
   ```bash
   git checkout -b feature/minha-melhoria
   ```
3. Faça suas alterações e certifique-se de que os testes passam:
   ```bash
   dotnet test
   ```
4. Faça o commit com mensagens claras:
   ```bash
   git commit -m "feat: adiciona suporte à detecção de saves do jogo XYZ"
   ```
5. Envie para o seu fork:
   ```bash
   git push origin feature/minha-melhoria
   ```
6. Abra um **Pull Request** descrevendo detalhadamente as melhorias introduzidas.
