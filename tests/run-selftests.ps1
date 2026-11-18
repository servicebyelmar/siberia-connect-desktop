# CI-проверки собранного SiberiaConnect.exe без GUI, VPN, боевого API и системных изменений.
#   1) --selftest без SC_INSTANCE: конфиги движков, резервная копия, прежние имена мьютекса/события и путь профиля.
#   2) --selftest с SC_INSTANCE: суффиксы имён и профиля, запреты режима экземпляра.
#   3) --selftest-api: разбор сохранённого ответа nodes/rank и сбои рейтинга на локальном сервере 127.0.0.1.
# Профили экземпляров пишутся только в -Work. Сценарий 4 (физический разрыв линка) здесь не проверяется — только стенд (tests/STAND-4.md).
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Work
)
$ErrorActionPreference = 'Stop'
$Exe = (Resolve-Path -LiteralPath $Exe).Path
$fixture = Join-Path $PSScriptRoot 'fixtures\rank-ok.json'
$expectedFixtureSha = '92280CA23513A8BC52F0E5E2577A37CE1C6EE8897982BD05956F3C648A3823D2'
if ((Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash -ne $expectedFixtureSha) { throw 'rank-ok.json не совпадает с записанным ответом сервера (SHA256)' }
[IO.Directory]::CreateDirectory($Work) | Out-Null
$failed = New-Object System.Collections.Generic.List[string]

function Invoke-Mode([string]$Name, [string[]]$Arguments, [hashtable]$Env, [string[]]$MustExist, [string]$MustContain) {
    $out = Join-Path $Work ('out-' + $Name)
    if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
    $saved = @{}
    foreach ($k in 'SC_INSTANCE', 'SC_DATA_ROOT', 'SC_API_BASE') { $saved[$k] = [Environment]::GetEnvironmentVariable($k); [Environment]::SetEnvironmentVariable($k, $null) }
    foreach ($k in $Env.Keys) { [Environment]::SetEnvironmentVariable($k, $Env[$k]) }
    try {
        $argList = @($Arguments[0], ('"' + $out + '"')) + @($Arguments | Select-Object -Skip 1 | ForEach-Object { '"' + $_ + '"' })
        $p = Start-Process -FilePath $Exe -ArgumentList $argList -PassThru
        if (-not $p.WaitForExit(180000)) { $p.Kill(); throw "${Name}: таймаут 180 с" }
        if ($p.ExitCode -ne 0) { throw "${Name}: код выхода $($p.ExitCode)" }
    }
    finally { foreach ($k in $saved.Keys) { [Environment]::SetEnvironmentVariable($k, $saved[$k]) } }
    if (Test-Path -LiteralPath (Join-Path $out 'error.txt')) { throw ("${Name}: " + (Get-Content -LiteralPath (Join-Path $out 'error.txt') -Raw)) }
    foreach ($f in $MustExist) { if (-not (Test-Path -LiteralPath (Join-Path $out $f))) { throw "${Name}: нет файла $f" } }
    if ($MustContain) {
        $text = Get-Content -LiteralPath (Join-Path $out $MustContain.Split('|')[0]) -Raw
        if ($text -notmatch [regex]::Escape($MustContain.Split('|')[1])) { throw "${Name}: в $($MustContain.Split('|')[0]) нет «$($MustContain.Split('|')[1])»" }
    }
    Write-Output "PASS $Name"
}

function Test-Step([string]$Name, [scriptblock]$Body) {
    try { & $Body } catch { $failed.Add($Name); Write-Output "FAIL $Name :: $($_.Exception.Message)" }
}

$common = @('backup-ok.txt', 'tun.json', 'xray-reality.json', 'xray-xhttp.json', 'xray-tun.json', 'xray-probe.json')
Test-Step 'selftest-default' { Invoke-Mode 'selftest-default' @('--selftest') @{} ($common + 'instance-default-ok.txt') $null }
Test-Step 'selftest-instance' { Invoke-Mode 'selftest-instance' @('--selftest') @{ SC_INSTANCE = 'ci-a'; SC_DATA_ROOT = (Join-Path $Work 'data') } ($common + 'instance-ok.txt') 'instance-ok.txt|ci-a' }
Test-Step 'selftest-api' { Invoke-Mode 'selftest-api' @('--selftest-api', $fixture) @{ SC_INSTANCE = 'ci-api'; SC_DATA_ROOT = (Join-Path $Work 'data') } @('api-ok.txt') 'api-ok.txt|done' }

if ($failed.Count) { Write-Output ('FAILED: ' + ($failed -join ', ')); exit 1 }
Write-Output 'ALL PASSED'
