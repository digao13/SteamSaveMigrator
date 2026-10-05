# Política de Segurança (Security Policy)

## 🔒 Diretrizes de Privacidade e Dados Locais

O **SteamSaveMigrator** foi projetado com forte foco em privacidade e segurança dos seus dados de jogos:

1. **Saves e Backups Locais**:
   - Os arquivos de saves dos seus jogos permanecem **exclusivamente na sua máquina**, dentro da pasta local de backups configurada por você ou no seu próprio armazenamento Google Drive.
   - Nenhuma informação de savegame, progresso ou conta é enviada a servidores de terceiros ou coletada pelos desenvolvedores.

2. **Segredos e Credenciais OAuth do Google Drive**:
   - O aplicativo nunca envia ou compartilha seus tokens de autenticação.
   - Os arquivos de credenciais (`credentials.json`, `client_secret.json`, `client_secrets.cfg`) ficam armazenados localmente no seu computador e estão protegidos pelo `.gitignore` para nunca serem commitados em repositórios Git ou forks.
   - Se você estiver construindo seu próprio fork ou executando em desenvolvimento, configure suas próprias credenciais OAuth através do Google Cloud Console ou via variáveis de ambiente `GOOGLE_CLIENT_ID` e `GOOGLE_CLIENT_SECRET`.

3. **Arquivos Restritos em Forks**:
   - Se você for realizar um fork deste projeto, nunca adicione arquivos da pasta `backups/`, tokens locais ou executáveis binários ao repositório público.

---

## 🛡️ Reportando Vulnerabilidades de Segurança

Se você descobrir alguma falha de segurança, vulnerabilidade de integridade ou vazamento acidental de dados no código:

- **NÃO** abra uma Issue pública no GitHub.
- Utilize a funcionalidade **"Report a vulnerability"** na aba **Security** do repositório no GitHub para criar um relatório privado.
- Os mantenedores responderão rapidamente e coordenarão uma correção segura antes da publicação.
