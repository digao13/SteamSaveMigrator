# 🎮 SteamSave Migrator v1.4.0 - Versão Final

Uma grande atualização focada em estabilidade definitiva, persistência garantida de diretórios, revisão textual completa em Português do Brasil e um design renovado com a estética autêntica da Steam!

---

### 🌟 Destaques desta Versão

#### 💾 1. Persistência Garantida do Diretório de Backup
- **Correção Definitiva do Bug da Pasta**: O aplicativo agora memoriza e preserva de forma permanente o último local de backup configurado pelo usuário, nunca mais retornando para pastas indesejadas.
- **Normalização Inteligente de Caminhos**: Tratamento automático de aspas, espaços extras, caminhos com caracteres especiais e resolução transparente de variáveis como `%USERPROFILE%`.
- **Sincronização Redundante e Tolerante a Falhas**: Gravação atômica sincronizada entre `%APPDATA%`, `%PROGRAMDATA%` e diretório local da aplicação.
- **Detecção Automática**: Ao escolher arquivos ou pastas nas telas de backup e restauração, a preferência é atualizada e fixada automaticamente.

#### 🎨 2. Novo Design System Inspirado na Steam
- **Paleta Oficial Autêntica**: Estilização imersiva baseada no cliente Steam com tons azul-marinho profundos (`#171A21`, `#1B2838`, `#212F43`), realces em azul celeste (`#66C0F4`) e botões verdes clássicos de ação primária (`#5C7E10`).
- **DataGrids Estilo Biblioteca Steam**: Tabelas redesenhadas com linhas alternadas suaves, cabeçalhos contrastantes e espaçamento ergonômico.
- **Organização de Contextos Aperfeiçoada**:
  - **Aba de Backup**: Divisão clara entre configuração do destino dos arquivos, card de backup individual com detalhes do jogo escaneado e painel de backup em lote de múltiplos jogos.
  - **Aba de Restauração**: Nova barra de ferramentas com botões para alternar pasta de busca, abrir no Explorer, atualizar lista em tempo real, carregar .zips externos e excluir backups.

#### ✍️ 3. Revisão Textual e Ortográfica Completa (pt-BR)
- **Eliminação de 100% dos Mojibakes**: Remoção de todos os caracteres truncados e símbolos corrompidos no código-fonte e recursos.
- **Textos Refinados**: Todas as mensagens, instruções, menus e tooltips revisados para oferecer uma experiência clara, elegante e natural em Português do Brasil.

#### 🧪 4. Confiabilidade e Testes
- **63 Testes Automatizados**: 100% de sucesso em toda a suíte de testes unitários e de integração.
- **Isolamento Estrito**: Configuração de testes totalmente desacoplada das preferências do sistema operacional.

---

### 📦 Como Instalar
1. Baixe o executável do instalador `SteamSaveMigrator_v1.4.0_Setup.exe` abaixo.
2. Execute o instalador e siga o assistente.
3. Seus dados e configurações anteriores serão automaticamente mantidos!
