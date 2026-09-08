#Requires -Version 5.1
<#
.SYNOPSIS
    Runs one of the projects under samples/.
.PARAMETER SampleName
    The sample project's folder/assembly name (e.g. TriQL.Samples.UserNamePassword). Defaults to
    TriQL.Samples.UserNamePassword.
.EXAMPLE
    ./samples/Run-Sample.ps1
.EXAMPLE
    ./samples/Run-Sample.ps1 -SampleName TriQL.Samples.Console
#>
param(
    [string]$SampleName = "TriQL.Samples.UserNamePassword"
)

$ErrorActionPreference = "Stop"

$projectPath = Join-Path $PSScriptRoot "$SampleName/$SampleName.csproj"
if (-not (Test-Path $projectPath)) {
    throw "No sample project found at '$projectPath'."
}

dotnet run --project $projectPath
