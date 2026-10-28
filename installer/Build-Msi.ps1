param(
    [Parameter(Mandatory = $true)][string]$PayloadDirectory,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [Parameter(Mandatory = $true)][string]$WixPath,
    [Parameter(Mandatory = $true)][string]$IntermediateDirectory
)
$ErrorActionPreference = 'Stop'
$payload = (Resolve-Path -LiteralPath $PayloadDirectory).Path
$repository = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $output) { throw "Output already exists: $output" }
$productNames = @('FixedDataBuilder.exe', 'FixedDataBuilder.dll', 'FixedDataBuilder.pdb', 'FixedDataBuilder.deps.json', 'FixedDataBuilder.runtimeconfig.json')
$sampleNames = @(git -C $repository ls-files samples)
if ($LASTEXITCODE -ne 0 -or $sampleNames.Count -eq 0) { throw 'Cannot obtain the tracked public sample list.' }
$names = @($productNames + $sampleNames)
foreach ($name in $names) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $name) -PathType Leaf)) { throw "Missing payload: $name" }
}
$version = (Get-Item -LiteralPath (Join-Path $payload 'FixedDataBuilder.exe')).VersionInfo.FileVersion
$productVersion = ([version]$version).ToString(3)
$runtime = Get-Content -LiteralPath (Join-Path $payload 'FixedDataBuilder.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($runtime.runtimeOptions.includedFrameworks -or -not @($runtime.runtimeOptions.frameworks | Where-Object {
    $_.name -eq 'Microsoft.WindowsDesktop.App' -and $_.version -like '8.*'
})) { throw 'Expected a .NET 8 framework-dependent Windows Desktop payload.' }
New-Item -ItemType Directory -Path $IntermediateDirectory -Force | Out-Null
$intermediate = (Resolve-Path -LiteralPath $IntermediateDirectory).Path
$namespace = 'http://wixtoolset.org/schemas/v4/wxs'
$document = [xml]"<Wix xmlns='$namespace'><Fragment><ComponentGroup Id='PayloadFiles' /></Fragment></Wix>"
$group = $document.DocumentElement.FirstChild.FirstChild
function Add-Element($parent, [string]$name, [hashtable]$attributes) {
    $element = $document.CreateElement($name, $namespace)
    foreach ($entry in $attributes.GetEnumerator()) { $element.SetAttribute($entry.Key, [string]$entry.Value) }
    $parent.AppendChild($element) | Out-Null
    return $element
}
foreach ($kind in @('Product', 'Samples')) {
    $directory = if ($kind -eq 'Product') { 'INSTALLFOLDER' } else { 'SAMPLESFOLDER' }
    $guid = if ($kind -eq 'Product') { '8AF5BBF5-3E29-4178-AFAF-65E79CD9DDA7' } else { 'D3C94F76-CF0C-4384-BECC-4390CEE0F3DA' }
    $component = Add-Element $group 'Component' @{ Id="${kind}Payload"; Directory=$directory; Guid=$guid }
    $kindNames = if ($kind -eq 'Product') { $productNames } else { $sampleNames }
    $index = 0
    foreach ($name in $kindNames) {
        Add-Element $component 'File' @{ Id="${kind}File$index"; Source=(Join-Path $payload $name); Name=[IO.Path]::GetFileName($name); KeyPath='no' } | Out-Null
        $index++
    }
    Add-Element $component 'RegistryValue' @{ Root='HKCU'; Key='Software\konikatsu\FixedDataBuilder\Installer';
        Name=$kind; Type='string'; Value="[$directory]"; KeyPath='yes' } | Out-Null
    Add-Element $component 'RemoveFolder' @{ Id="Remove${kind}Folder"; Directory=$directory; On='uninstall' } | Out-Null
}
$fragment = Join-Path $intermediate 'Payload.wxs'
$document.Save($fragment)
$before = @($names | ForEach-Object { [pscustomobject]@{ Name=$_; Sha256=(Get-FileHash -LiteralPath (Join-Path $payload $_) -Algorithm SHA256).Hash } })
& $WixPath build (Join-Path $PSScriptRoot 'Package.wxs') $fragment -arch x64 -d "ProductVersion=$productVersion" -intermediatefolder (Join-Path $intermediate 'wix') -pdbtype none -o $output
if ($LASTEXITCODE -ne 0) { throw "WiX build failed ($LASTEXITCODE)." }
foreach ($entry in $before) {
    if ($entry.Sha256 -ne (Get-FileHash -LiteralPath (Join-Path $payload $entry.Name) -Algorithm SHA256).Hash) { throw "Payload changed: $($entry.Name)" }
}
[pscustomobject]@{ Version=$productVersion; PayloadDirectory=$payload; Output=$output; FileCount=$names.Count;
    MsiSha256=(Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash; Payload=$before } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $intermediate 'build-manifest.json') -Encoding UTF8
