# Copyright 2026 Renaud Paquay All Rights Reserved.
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#     http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

param(
  [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

# Extract product version from VersionNumber.cs if not provided
if ([string]::IsNullOrEmpty($Version)) {
  $versionFile = Join-Path $ScriptDir "src\core-filesystem\VersionNumber.cs"
  if (Test-Path $versionFile) {
    $match = Select-String -Path $versionFile -Pattern 'public const string Product =\s*"([^"]+)"'
    if ($match -and $match.Matches.Groups.Count -ge 2) {
      $Version = $match.Matches.Groups[1].Value
    }
  }
}

if ([string]::IsNullOrEmpty($Version)) {
  Write-Error "Could not determine version from VersionNumber.cs. Pass -Version explicitly."
  exit 1
}

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host " Publishing mtsuite v$Version for all target platforms" -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan

$Platforms = @(
  "linux-x64",
  "win-x64",
  "osx-x64",
  "osx-arm64",
  "win-arm64"
)

$Apps = @(
  "mtcompact",
  "mtcopy",
  "mtdel",
  "mtfind",
  "mtfindstr",
  "mtinfo",
  "mtmir"
)

$PublishRoot = Join-Path $ScriptDir "publish"
if (!(Test-Path $PublishRoot)) {
  New-Item -ItemType Directory -Path $PublishRoot -Force | Out-Null
}

foreach ($rid in $Platforms) {
  Write-Host ""
  Write-Host "-----------------------------------------------------------------" -ForegroundColor Yellow
  Write-Host " Building & Publishing for: $rid" -ForegroundColor Yellow
  Write-Host "-----------------------------------------------------------------" -ForegroundColor Yellow

  # Run dotnet publish
  dotnet publish "mtsuite.sln" -c Release -r $rid --nologo

  $packageName = "mtsuite-$Version-$rid"
  $outDir = Join-Path $PublishRoot $packageName
  if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
  New-Item -ItemType Directory -Path $outDir -Force | Out-Null

  $isWindows = $rid.StartsWith("win-")

  foreach ($app in $Apps) {
    $binName = if ($isWindows) { "$app.exe" } else { $app }
    $binPath = Join-Path $ScriptDir "bin\Release\net8.0\$rid\publish\$binName"

    if (!(Test-Path $binPath)) {
      Write-Error "Expected binary not found: $binPath"
      exit 1
    }

    Copy-Item $binPath -Destination (Join-Path $outDir $binName)
  }

  $zipFile = Join-Path $PublishRoot "$packageName.zip"
  if (Test-Path $zipFile) { Remove-Item $zipFile -Force }

  Compress-Archive -Path "$outDir\*" -DestinationPath $zipFile -CompressionLevel Optimal

  $size = "{0:N2} MB" -f ((Get-Item $zipFile).Length / 1MB)
  Write-Host "Created directory: $outDir" -ForegroundColor Green
  Write-Host "Created archive:   $zipFile ($size)" -ForegroundColor Green
}

Write-Host ""
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host " Publish completed successfully! Generated Packages:" -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan
Get-ChildItem -Path $PublishRoot -Filter "mtsuite-*" | Select-Object FullName, Mode | Format-Table -AutoSize
