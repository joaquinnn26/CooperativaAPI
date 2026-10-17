param([string]$OutputDirectory = (Join-Path $env:TEMP 'SocioAppAdhesionesTests'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$bin = Join-Path $root 'CooperativaAPI\bin'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$exe = Join-Path $OutputDirectory 'AdhesionesTests.exe'
$config = New-Object System.Xml.XmlDocument
$config.Load((Join-Path $root 'CooperativaAPI\Web.config'))
$doc = New-Object System.Xml.XmlDocument
$doc.LoadXml('<configuration><configSections><section name="entityFramework" type="System.Data.Entity.Internal.ConfigFile.EntityFrameworkSection, EntityFramework" /></configSections></configuration>')
foreach ($nodeName in @('connectionStrings','entityFramework')) { $node = $config.configuration.SelectSingleNode($nodeName); if ($node) { $doc.DocumentElement.AppendChild($doc.ImportNode($node,$true)) | Out-Null } }
$doc.Save($exe + '.config')
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe "/out:$exe" /r:System.Data.dll /r:System.Xml.dll /r:System.Net.Http.dll "/r:$bin\System.Web.Http.dll" "/r:$bin\CooperativaAPI.dll" "/r:$bin\EntityFramework.dll" "/r:$bin\Newtonsoft.Json.dll" (Join-Path $PSScriptRoot 'AdhesionesTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Error compilando las pruebas.' }
& $exe $bin
if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas.' }
