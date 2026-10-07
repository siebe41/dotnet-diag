<#
.SYNOPSIS
    Runs dotnet-diag against every sample log in this folder.
.EXAMPLE
    ./samples/run-samples.ps1              # builds once, then runs from source
.EXAMPLE
    ./samples/run-samples.ps1 -Installed   # uses the installed global tool (dotnet tool install -g DotnetDiag.Tool)
.EXAMPLE
    ./samples/run-samples.ps1 -Filter package -ExtraArgs '--show-context'
#>
param(
    [switch] $Installed,
    [string] $Filter = '',
    [string[]] $ExtraArgs = @()
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if (-not $Installed) {
        dotnet build src/DotnetDiag.Cli -nologo -v quiet
        if ($LASTEXITCODE -ne 0) { throw 'Build failed; see the errors above.' }
    }

    $samples = Get-ChildItem -Path (Join-Path $PSScriptRoot '*') -Include '*.binlog', '*.txt' |
        Where-Object { $_.BaseName -like "*$Filter*" } |
        Sort-Object Name

    foreach ($sample in $samples) {
        $repo = Join-Path 'eval/scenarios' $sample.BaseName
        Write-Host ''
        Write-Host "=== $($sample.Name) ===" -ForegroundColor Cyan
        if ($Installed) {
            dotnet-diag analyze $sample.FullName --repo $repo @ExtraArgs
        }
        else {
            dotnet run --project src/DotnetDiag.Cli --no-build -- analyze $sample.FullName --repo $repo @ExtraArgs
        }
    }
}
finally {
    Pop-Location
}
