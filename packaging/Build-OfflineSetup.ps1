param(
    [string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'output\MaintenanceSetup')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$appProject = Join-Path $root 'Maintenance_app\Maintenance_app.csproj'
$setupProject = Join-Path $PSScriptRoot 'Setup\Setup.csproj'
$work = Join-Path ([IO.Path]::GetTempPath()) ('maintenance-setup-' + [Guid]::NewGuid().ToString('N'))
$app = Join-Path $work 'Application'
$setup = Join-Path $work 'Setup'
$payload = Join-Path $work 'Application.zip'

# نثبت إصدار وقت التشغيل في التطبيق والمثبّت حتى تحتوي الحزمة على الملفات نفسها.
$publish = @('publish', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    '-p:RuntimeFrameworkVersion=10.0.14', '-p:DebugType=None', '-p:DebugSymbols=false',
    '-p:TargetPlatformDisplayName=Windows', '-p:NuGetAudit=false', '--ignore-failed-sources')

try {
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    & dotnet @publish $appProject -p:Platform=x64 -o $app
    if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }

    # نغيّر اسم المشغّل فقط ليبقى اسم التجميعة ومراجع موارد WPF كما هي.
    Rename-Item -LiteralPath (Join-Path $app 'Maintenance_app.exe') -NewName 'منظومة الخدمات والصيانة.exe'
    $legacyConnection = Join-Path $app 'ConectionString'
    if (Test-Path -LiteralPath $legacyConnection) {
        Remove-Item -LiteralPath $legacyConnection
    }

    # يفشل بناء الحزمة عند فقدان أحد قوالب الطباعة التي تُقرأ أثناء التشغيل.
    foreach ($report in 'Maint_grop.rdlc', 'Maint_grop_date.rdlc', 'Maint_grop_Build.rdlc', 'new_Daly_Check.rdlc', 'new_Daly_Check_serv.rdlc', 'chart_report.repx',
                         'Mainte_Report.repx', 'parts.repx', 'Done_Mainte_Report.repx', 'new_Daly_Check.repx', 'new_Daly_Check_serv.repx') {
        if (-not (Test-Path -LiteralPath (Join-Path $app ('Reports\' + $report)))) {
            throw "Missing report: $report"
        }
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($app, $payload, [IO.Compression.CompressionLevel]::Optimal, $false)

    # ملفات المثبّت وملفات المنظومة مضمنة في EXE واحد ولا تُنزّل وقت التثبيت.
    & dotnet @publish $setupProject -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true "-p:ApplicationPayload=$payload" -o $setup
    if ($LASTEXITCODE -ne 0) { throw 'Setup publish failed.' }

    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $installer = Join-Path $OutputDirectory 'Setup.exe'
    Copy-Item -LiteralPath (Join-Path $setup 'MaintenanceSetup.exe') -Destination $installer -Force
    Get-FileHash -LiteralPath $installer -Algorithm SHA256
}
finally {
    # مجلد عمل معزول ومولّد لهذه العملية داخل المسار المؤقت.
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ((Test-Path -LiteralPath $work) -and [IO.Path]::GetFullPath($work).StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $work -Recurse -Force
    }
}
