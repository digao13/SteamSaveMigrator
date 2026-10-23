<#
.SYNOPSIS
    Script automatizado de gestão de versão e release para o SteamSave Migrator.
    Gera a versão segura para o GitHub (Release Oficial) e a versão configurada para a máquina local.

.PARAMETER Version
    Número da versão a ser lançada (ex: "1.5.1"). Se omitido, usa a versão atual.

.PARAMETER UpdateLocalInstall
    Se verdadeiro, atualiza automaticamente a pasta de instalação local (C:\Testew\SteamSaveMigrator)
    e coloca o instalador e atalho na Área de Trabalho real do usuário.

.PARAMETER CreateGitHubRelease
    Se verdadeiro, cria ou atualiza a Release no GitHub usando a ferramenta 'gh'.

.EXAMPLE
    .\build-release.ps1 -Version "1.5.1"
#>

[CmdletBinding()]
param(
    [string]$Version = "1.5.2",
    [string]$ReleaseTitle = "",
    [string]$ReleaseNotes = "",
    [switch]$UpdateLocalInstall = $true,
    [switch]$CreateGitHubRelease = $true
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   SteamSave Migrator - Automação de Versão e Release     " -ForegroundColor Cyan
Write-Host "   Versão de Lançamento: v$Version                        " -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan

$scriptRoot = $PSScriptRoot
if (-not $scriptRoot) { $scriptRoot = Get-Location }

# 1. ATUALIZAR VERSÃO NOS ARQUIVOS DO PROJETO (PRESERVANDO UTF-8 SEM CORROMPER CARACTERES)
Write-Host "`n[1/7] Atualizando metadados de versão nos arquivos..." -ForegroundColor Green

$coreCsproj = Join-Path $scriptRoot "src\SteamSaveMigrator.Core\SteamSaveMigrator.Core.csproj"
$wpfCsproj = Join-Path $scriptRoot "src\SteamSaveMigrator.Wpf\SteamSaveMigrator.Wpf.csproj"
$installerScript = Join-Path $scriptRoot "installer.iss"
$mainVm = Join-Path $scriptRoot "src\SteamSaveMigrator.Wpf\ViewModels\MainViewModel.cs"

# Atualiza .csproj Core e WPF
foreach ($proj in @($coreCsproj, $wpfCsproj)) {
    if (Test-Path $proj) {
        $content = [System.IO.File]::ReadAllText($proj, [System.Text.Encoding]::UTF8)
        $content = [regex]::Replace($content, '<Version>[\d\.]+</Version>', "<Version>$Version</Version>")
        $content = [regex]::Replace($content, '<AssemblyVersion>[\d\.]+</AssemblyVersion>', "<AssemblyVersion>$Version.0</AssemblyVersion>")
        $content = [regex]::Replace($content, '<FileVersion>[\d\.]+</FileVersion>', "<FileVersion>$Version.0</FileVersion>")
        [System.IO.File]::WriteAllText($proj, $content, [System.Text.Encoding]::UTF8)
    }
}

# Atualiza installer.iss
if (Test-Path $installerScript) {
    $content = [System.IO.File]::ReadAllText($installerScript, [System.Text.Encoding]::UTF8)
    $content = [regex]::Replace($content, '#define MyAppVersion "[^"]+"', "#define MyAppVersion `"$Version`"")
    [System.IO.File]::WriteAllText($installerScript, $content, [System.Text.Encoding]::UTF8)
}

# Atualiza MainViewModel.cs
if (Test-Path $mainVm) {
    $content = [System.IO.File]::ReadAllText($mainVm, [System.Text.Encoding]::UTF8)
    $content = [regex]::Replace($content, ': "\d+\.\d+\.\d+"', ": `"$Version`"")
    [System.IO.File]::WriteAllText($mainVm, $content, [System.Text.Encoding]::UTF8)
}

# Atualiza MainWindow.xaml
$mainWindowXaml = Join-Path $scriptRoot "src\SteamSaveMigrator.Wpf\MainWindow.xaml"
if (Test-Path $mainWindowXaml) {
    $content = [System.IO.File]::ReadAllText($mainWindowXaml, [System.Text.Encoding]::UTF8)
    $content = [regex]::Replace($content, "FallbackValue='v\d+\.\d+\.\d+'", "FallbackValue='v$Version'")
    $content = [regex]::Replace($content, "SteamSave Migrator v\d+\.\d+\.\d+", "SteamSave Migrator v$Version")
    [System.IO.File]::WriteAllText($mainWindowXaml, $content, [System.Text.Encoding]::UTF8)
}

# 2. EXECUTAR TESTES AUTOMATIZADOS
Write-Host "`n[2/7] Executando testes unitários automatizados..." -ForegroundColor Green
$testProj = Join-Path $scriptRoot "tests\SteamSaveMigrator.Tests\SteamSaveMigrator.Tests.csproj"
dotnet test $testProj -c Release --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "Os testes automatizados falharam! O build foi cancelado."
    exit 1
}

# 3. COMPILAR E PUBLICAR BUILD AUTOCONTIDO (WIN-X64)
Write-Host "`n[3/7] Compilando aplicativo autocontido (Self-Contained)..." -ForegroundColor Green
$publishDir = Join-Path $scriptRoot "dist\publish_v150"
dotnet publish $wpfCsproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $publishDir --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "Falha ao publicar os binários."
    exit 1
}

# Garante que as credenciais NÃO estejam presentes na compilação pública
Remove-Item -Path (Join-Path $publishDir "credentials.json"), (Join-Path $publishDir "client_secrets.cfg") -ErrorAction SilentlyContinue -Force

# 4. GERAR INSTALADOR PÚBLICO SEGURO PARA O GITHUB
Write-Host "`n[4/7] Gerando instalador público seguro para o GitHub..." -ForegroundColor Green
$isccPaths = @(
    "C:\Users\User\AppData\Local\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)
$isccExe = $isccPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

$setupExe = Join-Path $scriptRoot "dist\SteamSaveMigrator_v${Version}_Setup.exe"

if ($isccExe) {
    & $isccExe $installerScript
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Falha ao compilar o instalador Inno Setup."
    } else {
        Write-Host " -> Instalador gerado: $setupExe" -ForegroundColor Cyan
    }
} else {
    Write-Warning "Compilador Inno Setup (ISCC.exe) não encontrado."
}

# 5. GERAR PACOTE PORTÁTIL E PUBLICAR RELEASE NO GITHUB (VIA GH CLI)
$portableZip = Join-Path $scriptRoot "dist\SteamSaveMigrator_v${Version}_Portable.zip"
$portableTemp = Join-Path $scriptRoot "dist\publish_portable"
if (Test-Path $portableTemp) { Remove-Item $portableTemp -Recurse -Force }
Copy-Item -Path $publishDir -Destination $portableTemp -Recurse -Force
Remove-Item -Path (Join-Path $portableTemp "credentials.json"), (Join-Path $portableTemp "client_secrets.cfg") -ErrorAction SilentlyContinue -Force
if (Test-Path $portableZip) { Remove-Item $portableZip -Force }
Compress-Archive -Path "$portableTemp\*" -DestinationPath $portableZip -CompressionLevel Optimal
Remove-Item $portableTemp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host " -> Pacote portátil .zip gerado: $portableZip" -ForegroundColor Cyan

if ($CreateGitHubRelease -and (Get-Command gh -ErrorAction SilentlyContinue) -and (Test-Path $setupExe)) {
    Write-Host "`n[5/7] Publicando Release no GitHub..." -ForegroundColor Green
    try {
        $tag = "v$Version"
        $title = if (![string]::IsNullOrWhiteSpace($ReleaseTitle)) { $ReleaseTitle } else { "SteamSave Migrator v$Version" }
        $notes = if (![string]::IsNullOrWhiteSpace($ReleaseNotes)) {
            $ReleaseNotes
        } else {
@"
## Novidades da Versao $Version

- Aba Non-Steam: Deteccao e backup automatico de saves para jogos com correcoes (CODEX, Rune, PLAZA, CPY, HOODLUM, Voices38, Goldberg, EMPRESS, SKIDROW, FLT).
- Sincronizacao em Nuvem: Envio automatico e upload direto para o Google Drive.
- Guia OAuth Interativo: Tutorial completo para habilitar a Google Drive API e adicionar usuario de teste.
- Seguranca: Codigo e instalador seguros sem credenciais expostas.
"@
        }

        gh release view $tag > $null 2>&1
        if ($LASTEXITCODE -eq 0) {
            gh release upload $tag $setupExe $portableZip --clobber
            Write-Host " -> Release $tag atualizada com novos binários no GitHub!" -ForegroundColor Cyan
        } else {
            gh release create $tag $setupExe $portableZip --title $title --notes $notes
            Write-Host " -> Release $tag criada com sucesso no GitHub!" -ForegroundColor Cyan
        }
    } catch {
        Write-Warning "Não foi possível publicar a release no GitHub: $_"
    }
} else {
    Write-Host "`n[5/7] Etapa de Release no GitHub ignorada (gh não disponível ou instalador ausente)." -ForegroundColor Yellow
}

# 6. INJETAR CREDENCIAIS LOCAIS PARA A MÁQUINA DESTE USUÁRIO
Write-Host "`n[6/7] Injetando credenciais locais para uso nesta máquina..." -ForegroundColor Green
$appDataOAuth = Join-Path $env:APPDATA "SteamSaveMigrator\GoogleDriveAuth"
$localCreds = Join-Path $appDataOAuth "credentials.json"
$localSecrets = Join-Path $appDataOAuth "client_secrets.cfg"

if (Test-Path $localCreds) {
    Copy-Item -Path $localCreds -Destination (Join-Path $publishDir "credentials.json") -Force
}
if (Test-Path $localSecrets) {
    Copy-Item -Path $localSecrets -Destination (Join-Path $publishDir "client_secrets.cfg") -Force
}

# 7. ATUALIZAR INSTALAÇÃO LOCAL DO USUÁRIO & ÁREA DE TRABALHO
if ($UpdateLocalInstall) {
    Write-Host "`n[7/7] Atualizando máquina local..." -ForegroundColor Green
    $localTarget = "C:\Testew\SteamSaveMigrator"
    if (-not (Test-Path $localTarget)) {
        New-Item -ItemType Directory -Path $localTarget -Force | Out-Null
    }

    # Fecha qualquer processo antes de copiar
    Get-Process -Name "SteamSaveMigrator.Wpf" -ErrorAction SilentlyContinue | Stop-Process -Force

    Copy-Item -Path "$publishDir\*" -Destination $localTarget -Recurse -Force
    Write-Host " -> Pasta local C:\Testew\SteamSaveMigrator atualizada!" -ForegroundColor Cyan

    # Registra no Windows via Inno Setup silencioso
    if (Test-Path $setupExe) {
        & $setupExe /VERYSILENT /SUPPRESSMSGBOXES /DIR="$localTarget"
    }

    # Atualiza Atalho e Instalador na Área de Trabalho REAL
    $desktop = [Environment]::GetFolderPath('Desktop')
    if (Test-Path $desktop) {
        if (Test-Path $setupExe) {
            Copy-Item -Path $setupExe -Destination (Join-Path $desktop "SteamSaveMigrator_v${Version}_Setup.exe") -Force
            Write-Host " -> Instalador copiado para a Área de Trabalho: $desktop" -ForegroundColor Cyan
        }

        $sh = New-Object -ComObject WScript.Shell
        $lnk = $sh.CreateShortcut((Join-Path $desktop "SteamSave Migrator.lnk"))
        $lnk.TargetPath = Join-Path $localTarget "SteamSaveMigrator.Wpf.exe"
        $lnk.WorkingDirectory = $localTarget
        $lnk.IconLocation = (Join-Path $localTarget "SteamSaveMigrator.Wpf.exe") + ",0"
        $lnk.Description = "SteamSave Migrator v$Version"
        $lnk.Save()
        Write-Host " -> Atalho atualizado na Área de Trabalho com sucesso!" -ForegroundColor Cyan

        # Atualiza atalhos legados no Menu Iniciar e Inicialização
        $startMenuLnk = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\SteamSaveMigrator.lnk"
        if (Test-Path $startMenuLnk) {
            $sLnk = $sh.CreateShortcut($startMenuLnk)
            $sLnk.TargetPath = Join-Path $localTarget "SteamSaveMigrator.Wpf.exe"
            $sLnk.WorkingDirectory = $localTarget
            $sLnk.Save()
        }
        $startupLnk = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup\SteamSaveMigrator.lnk"
        if (Test-Path $startupLnk) {
            $stLnk = $sh.CreateShortcut($startupLnk)
            $stLnk.TargetPath = Join-Path $localTarget "SteamSaveMigrator.Wpf.exe"
            $stLnk.WorkingDirectory = $localTarget
            $stLnk.Save()
        }

        # Atualiza pasta legada bin\Release\app se existir
        $legacyBinApp = Join-Path $scriptRoot "src\SteamSaveMigrator.Wpf\bin\Release\app"
        if (Test-Path $legacyBinApp) {
            Copy-Item -Path "$publishDir\*" -Destination $legacyBinApp -Recurse -Force
        }
    }
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "   Sucesso! Release v$Version finalizado com êxito!       " -ForegroundColor Green
Write-Host "   • GitHub: Release e instalador oficial disponíveis     " -ForegroundColor Green
Write-Host "   • Local: Instalado em C:\Testew\SteamSaveMigrator     " -ForegroundColor Green
Write-Host "   • Desktop: Instalador e Atalho prontos                 " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
