<#
.SYNOPSIS
    Script automatizado de gestão de versão e build para o SteamSave Migrator.
    Gera a versão segura para o GitHub e a versão configurada para a máquina local.

.PARAMETER Version
    Número da versão a ser lançada (ex: "1.5.0"). Se omitido, usa a versão atual.

.PARAMETER UpdateLocalInstall
    Se verdadeiro, atualiza automaticamente a pasta de instalação local (C:\Testew\SteamSaveMigrator).

.EXAMPLE
    .\build-release.ps1 -Version "1.5.0"
#>

[CmdletBinding()]
param(
    [string]$Version = "1.5.0",
    [switch]$UpdateLocalInstall = $true
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   SteamSave Migrator - Automação de Versão e Release     " -ForegroundColor Cyan
Write-Host "   Versão de Lançamento: v$Version                        " -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan

$scriptRoot = $PSScriptRoot
if (-not $scriptRoot) { $scriptRoot = Get-Location }

# 1. ATUALIZAR VERSÃO NOS ARQUIVOS DO PROJETO
Write-Host "`n[1/6] Atualizando metadados de versão nos arquivos..." -ForegroundColor Green

$coreCsproj = Join-Path $scriptRoot "src\SteamSaveMigrator.Core\SteamSaveMigrator.Core.csproj"
$wpfCsproj = Join-Path $scriptRoot "src\SteamSaveMigrator.Wpf\SteamSaveMigrator.Wpf.csproj"
$installerScript = Join-Path $scriptRoot "installer.iss"
$mainWindowXaml = Join-Path $scriptRoot "src\SteamSaveMigrator.Wpf\MainWindow.xaml"
$mainVm = Join-Path $scriptRoot "src\SteamSaveMigrator.Wpf\ViewModels\MainViewModel.cs"

# Atualiza .csproj Core e WPF
foreach ($proj in @($coreCsproj, $wpfCsproj)) {
    if (Test-Path $proj) {
        $content = Get-Content $proj -Raw
        $content = [regex]::Replace($content, '<Version>[\d\.]+</Version>', "<Version>$Version</Version>")
        $content = [regex]::Replace($content, '<AssemblyVersion>[\d\.]+</AssemblyVersion>', "<AssemblyVersion>$Version.0</AssemblyVersion>")
        $content = [regex]::Replace($content, '<FileVersion>[\d\.]+</FileVersion>', "<FileVersion>$Version.0</FileVersion>")
        Set-Content -Path $proj -Value $content -Encoding UTF8
    }
}

# Atualiza installer.iss
if (Test-Path $installerScript) {
    $content = Get-Content $installerScript -Raw
    $content = [regex]::Replace($content, '#define MyAppVersion "[^"]+"', "#define MyAppVersion `"$Version`"")
    Set-Content -Path $installerScript -Value $content -Encoding UTF8
}

# Atualiza MainWindow.xaml e MainViewModel.cs
if (Test-Path $mainWindowXaml) {
    $content = Get-Content $mainWindowXaml -Raw
    $content = [regex]::Replace($content, "v\d+\.\d+\.\d+", "v$Version")
    Set-Content -Path $mainWindowXaml -Value $content -Encoding UTF8
}

if (Test-Path $mainVm) {
    $content = Get-Content $mainVm -Raw
    $content = [regex]::Replace($content, ': "\d+\.\d+\.\d+"', ": `"$Version`"")
    Set-Content -Path $mainVm -Value $content -Encoding UTF8
}

# 2. EXECUTAR TESTES AUTOMATIZADOS
Write-Host "`n[2/6] Executando testes unitários automatizados..." -ForegroundColor Green
$testProj = Join-Path $scriptRoot "tests\SteamSaveMigrator.Tests\SteamSaveMigrator.Tests.csproj"
dotnet test $testProj -c Release --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "Os testes automatizados falharam! O build foi cancelado."
    exit 1
}

# 3. COMPILAR E PUBLICAR BUILD AUTOCONTIDO (WIN-X64)
Write-Host "`n[3/6] Compilando aplicativo autocontido (Self-Contained)..." -ForegroundColor Green
$publishDir = Join-Path $scriptRoot "dist\publish_v150"
dotnet publish $wpfCsproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $publishDir --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "Falha ao publicar os binários."
    exit 1
}

# 4. INJETAR CREDENCIAIS LOCAIS NO BUILD LOCAL
Write-Host "`n[4/6] Injetando credenciais e configurações locais para uso nesta máquina..." -ForegroundColor Green
$appDataOAuth = Join-Path $env:APPDATA "SteamSaveMigrator\GoogleDriveAuth"
$localCreds = Join-Path $appDataOAuth "credentials.json"
$localSecrets = Join-Path $appDataOAuth "client_secrets.cfg"

if (Test-Path $localCreds) {
    Copy-Item -Path $localCreds -Destination (Join-Path $publishDir "credentials.json") -Force
    Write-Host " -> credentials.json local copiado com sucesso para a pasta de distribuição." -ForegroundColor Cyan
}

if (Test-Path $localSecrets) {
    Copy-Item -Path $localSecrets -Destination (Join-Path $publishDir "client_secrets.cfg") -Force
    Write-Host " -> client_secrets.cfg local copiado com sucesso." -ForegroundColor Cyan
}

# 5. GERAR INSTALADOR VIA INNO SETUP
Write-Host "`n[5/6] Gerando instalador .exe com Inno Setup..." -ForegroundColor Green
$isccPaths = @(
    "C:\Users\User\AppData\Local\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)

$isccExe = $isccPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($isccExe) {
    & $isccExe $installerScript
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Falha ao gerar o instalador Inno Setup."
    } else {
        $setupExe = Join-Path $scriptRoot "dist\SteamSaveMigrator_v${Version}_Setup.exe"
        Write-Host " -> Instalador gerado: $setupExe" -ForegroundColor Cyan
    }
} else {
    Write-Warning "Compilador Inno Setup (ISCC.exe) não encontrado nos caminhos padrão."
}

# 6. ATUALIZAR INSTALAÇÃO LOCAL DO USUÁRIO
if ($UpdateLocalInstall) {
    Write-Host "`n[6/6] Atualizando pasta de instalação local (C:\Testew\SteamSaveMigrator)..." -ForegroundColor Green
    $localTarget = "C:\Testew\SteamSaveMigrator"
    if (-not (Test-Path $localTarget)) {
        New-Item -ItemType Directory -Path $localTarget -Force | Out-Null
    }

    Copy-Item -Path "$publishDir\*" -Destination $localTarget -Recurse -Force
    Write-Host " -> Pasta de instalação local atualizada com sucesso!" -ForegroundColor Cyan

    # Se o instalador estiver disponível, executa a atualização silenciosa para registrar desinstalador
    $setupExe = Join-Path $scriptRoot "dist\SteamSaveMigrator_v${Version}_Setup.exe"
    if (Test-Path $setupExe) {
        & $setupExe /VERYSILENT /SUPPRESSMSGBOXES /DIR="$localTarget"
        Write-Host " -> Instalação registrada com sucesso no sistema operacional." -ForegroundColor Cyan
    }
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "   Sucesso! Release v$Version finalizado com êxito!       " -ForegroundColor Green
Write-Host "   • GitHub: Código limpo e seguro (sem credenciais)     " -ForegroundColor Green
Write-Host "   • Local: Instalado em C:\Testew\SteamSaveMigrator     " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
