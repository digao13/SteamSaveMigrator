<#
.SYNOPSIS
    Script de automacao de versionamento, compilacao, testes, instalador e publicacao no GitHub.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$Version,

    [Parameter(Mandatory = $false)]
    [string]$Title,

    [Parameter(Mandatory = $false)]
    [string]$NotesFile,

    [Parameter(Mandatory = $false)]
    [switch]$SkipGit = $false,

    [Parameter(Mandatory = $false)]
    [switch]$SkipTests = $false
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "SteamSave Migrator - Automacao de Build e Release GitHub" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$rootDir = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $rootDir "SteamSaveMigrator.slnx"))) {
    $rootDir = Get-Location
}
Set-Location $rootDir

$wpfCsproj = Join-Path $rootDir "src\SteamSaveMigrator.Wpf\SteamSaveMigrator.Wpf.csproj"
$coreCsproj = Join-Path $rootDir "src\SteamSaveMigrator.Core\SteamSaveMigrator.Core.csproj"
$installerIss = Join-Path $rootDir "installer.iss"

if (-not (Test-Path $wpfCsproj)) {
    throw "Arquivo $wpfCsproj nao encontrado."
}

# 1. Determina a versao
$wpfContent = Get-Content $wpfCsproj -Raw
if ($wpfContent -match '<Version>([0-9\.]+)</Version>') {
    $currentVersion = $matches[1]
} else {
    $currentVersion = "1.3.0"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $parts = $currentVersion.Split('.')
    if ($parts.Length -ge 3) {
        $patch = [int]$parts[2] + 1
        $targetVersion = "$($parts[0]).$($parts[1]).$patch"
    } else {
        $targetVersion = "$currentVersion.1"
    }
} else {
    $targetVersion = $Version.Trim().TrimStart('v', 'V')
}

$cleanVer = $targetVersion.Replace(".", "")
$tag = "v$targetVersion"
$publishDir = "dist\publish_v$cleanVer"
$setupExeName = "SteamSaveMigrator_v$($targetVersion)_Setup.exe"
$setupExePath = Join-Path $rootDir "dist\$setupExeName"

Write-Host "`nVersao atual:    $currentVersion" -ForegroundColor Yellow
Write-Host "Versao release:  $targetVersion (Tag: $tag)" -ForegroundColor Green
Write-Host "Pasta publish:   $publishDir" -ForegroundColor Gray
Write-Host "Instalador:      dist\$setupExeName`n" -ForegroundColor Gray

# 2. Atualiza arquivos .csproj
Write-Host "[1/7] Atualizando versao nos arquivos .csproj..." -ForegroundColor Cyan
$coreContent = Get-Content $coreCsproj -Raw
$coreUpdated = [regex]::Replace($coreContent, '<Version>[^<]+</Version>', "<Version>$targetVersion</Version>")
$coreUpdated = [regex]::Replace($coreUpdated, '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$targetVersion.0</AssemblyVersion>")
$coreUpdated = [regex]::Replace($coreUpdated, '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$targetVersion.0</FileVersion>")
Set-Content -Path $coreCsproj -Value $coreUpdated -Encoding utf8

$wpfUpdated = [regex]::Replace($wpfContent, '<Version>[^<]+</Version>', "<Version>$targetVersion</Version>")
$wpfUpdated = [regex]::Replace($wpfUpdated, '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$targetVersion.0</AssemblyVersion>")
$wpfUpdated = [regex]::Replace($wpfUpdated, '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$targetVersion.0</FileVersion>")
Set-Content -Path $wpfCsproj -Value $wpfUpdated -Encoding utf8

# Atualiza fallback no MainViewModel.cs caso exista
$mainVmPath = Join-Path $rootDir "src\SteamSaveMigrator.Wpf\ViewModels\MainViewModel.cs"
if (Test-Path $mainVmPath) {
    $vmContent = Get-Content $mainVmPath -Raw
    $vmUpdated = [regex]::Replace($vmContent, ': "[0-9\.]+";', ": `"$targetVersion`";")
    Set-Content -Path $mainVmPath -Value $vmUpdated -Encoding utf8
}

# 3. Compilacao da solucao
Write-Host "[2/7] Compilando a solucao em Release (dotnet build)..." -ForegroundColor Cyan
dotnet build -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Falha na compilacao do projeto!"
}

# 4. Execucao de testes unitarios
if (-not $SkipTests) {
    Write-Host "[3/7] Executando testes automatizados (dotnet test)..." -ForegroundColor Cyan
    dotnet test --no-build -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "Um ou mais testes falharam! Abortando release para seguranca."
    }
} else {
    Write-Host "[3/7] Testes ignorados (-SkipTests)." -ForegroundColor Yellow
}

# 5. Publicacao autonoma (Publish win-x64)
Write-Host "[4/7] Gerando build autonomo em $publishDir..." -ForegroundColor Cyan
dotnet publish "$wpfCsproj" -c Release -r win-x64 --self-contained true -o "$publishDir"
if ($LASTEXITCODE -ne 0) {
    throw "Falha no dotnet publish!"
}

# 6. Compilacao do Instalador Inno Setup
Write-Host "[5/7] Compilando instalador oficial com Inno Setup..." -ForegroundColor Cyan
$isccLocations = @(
    "C:\Users\User\AppData\Local\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
$iscc = $null
foreach ($loc in $isccLocations) {
    if (Test-Path $loc) { $iscc = $loc; break }
}
if (-not $iscc) {
    $cmd = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}

if (-not $iscc) {
    Write-Warning "Inno Setup Compiler (ISCC.exe) nao foi encontrado. Instalador nao gerado."
} else {
    & $iscc "/dMyAppVersion=$targetVersion" "/dPublishDir=$publishDir" "$installerIss"
    if ($LASTEXITCODE -ne 0) {
        throw "Erro ao compilar o instalador com Inno Setup!"
    }
    Write-Host "Instalador gerado com sucesso em: $setupExePath" -ForegroundColor Green
}

# 7. Git Commit, Tag e Push
if (-not $SkipGit) {
    Write-Host "[6/7] Registrando alteracoes e criando Tag no Git..." -ForegroundColor Cyan
    git add -A
    git commit -m "release: $tag" --allow-empty
    
    # Remove tag local se ja existir para atualizar
    git tag -d $tag 2>$null
    git tag -a $tag -m "Release $tag"

    Write-Host "Enviando commits e tags para o GitHub (git push origin main --tags)..." -ForegroundColor Cyan
    git push origin main --tags
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Aviso no git push. Verifique a conexao remota."
    }

    # 8. GitHub Release via gh CLI
    Write-Host "[7/7] Publicando Release no GitHub via gh release..." -ForegroundColor Cyan
    $gh = Get-Command "gh.exe" -ErrorAction SilentlyContinue
    if ($gh) {
        $releaseTitle = if (-not [string]::IsNullOrWhiteSpace($Title)) { $Title } else { "SteamSave Migrator $tag" }
        
        # Deleta release antiga de mesmo nome se houver
        gh release delete $tag --yes --cleanup-tag 2>$null

        $ghArgs = @("release", "create", $tag)
        if (Test-Path $setupExePath) {
            $ghArgs += $setupExePath
        }
        $ghArgs += "--title"
        $ghArgs += $releaseTitle

        if (-not [string]::IsNullOrWhiteSpace($NotesFile) -and (Test-Path $NotesFile)) {
            $ghArgs += "--notes-file"
            $ghArgs += $NotesFile
        } else {
            $ghArgs += "--generate-notes"
        }

        & $gh.Source @ghArgs
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Release publicada com sucesso no GitHub!" -ForegroundColor Green
            Write-Host "URL: https://github.com/digao13/SteamSaveMigrator/releases/tag/$tag" -ForegroundColor Green
        } else {
            Write-Warning "gh release create retornou codigo $LASTEXITCODE."
        }
    } else {
        Write-Warning "GitHub CLI (gh) nao encontrado."
    }
} else {
    Write-Host "[6/7 e 7/7] Etapas do Git/GitHub ignoradas (-SkipGit)." -ForegroundColor Yellow
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "Release $tag concluida com sucesso!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
