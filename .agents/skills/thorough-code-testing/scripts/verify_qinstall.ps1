param(
  [string]$ProjectRoot = 'C:\Users\jmfus\Desktop\Qinstall',
  [switch]$UpdateExe
)

$ErrorActionPreference = 'Stop'
$passed = 0
$failed = 0
$warnings = 0

function Pass([string]$msg) { $script:passed++; Write-Host "[PASS] $msg" -ForegroundColor Green }
function Fail([string]$msg) { $script:failed++; Write-Host "[FAIL] $msg" -ForegroundColor Red }
function Warn([string]$msg) { $script:warnings++; Write-Host "[WARN] $msg" -ForegroundColor Yellow }

Write-Host "=== Qinstall Thorough Verification Suite ===" -ForegroundColor Cyan
Write-Host "ProjectRoot: $ProjectRoot`n"

# 1. Catalog JSON integrity & duplicate IDs
$appsJsonPath = Join-Path $ProjectRoot 'apps.json'
try {
  $catalog = Get-Content -Raw -Encoding UTF8 $appsJsonPath | ConvertFrom-Json
  if (-not $catalog.Apps -or $catalog.Apps.Count -eq 0) {
    Fail "apps.json has no Apps entries"
  } else {
    $dupes = $catalog.Apps | Group-Object Id | Where-Object { $_.Count -gt 1 }
    if ($dupes) {
      Fail ("Duplicate app IDs in apps.json: " + (($dupes | ForEach-Object { $_.Name }) -join ', '))
    } else {
      Pass "apps.json valid UTF-8 JSON with $($catalog.Apps.Count) unique apps"
    }
    $badEntries = $catalog.Apps | Where-Object { [string]::IsNullOrWhiteSpace($_.Id) -or [string]::IsNullOrWhiteSpace($_.Name) -or [string]::IsNullOrWhiteSpace($_.Target) }
    if ($badEntries) {
      Fail ("Entries missing Id/Name/Target: " + (($badEntries | ForEach-Object { $_.Name }) -join ', '))
    } else {
      Pass "All apps.json entries have Id, Name, and Target"
    }
  }
} catch {
  Fail "apps.json parse failed: $($_.Exception.Message)"
}

# 2. Models.cs parity check with apps.json
$modelsPath = Join-Path $ProjectRoot 'src\Models.cs'
if (Test-Path $modelsPath) {
  $modelsText = Get-Content -Raw -Encoding UTF8 $modelsPath
  $makeMatches = [regex]::Matches($modelsText, 'Make\("([^"]+)"')
  $modelIds = @($makeMatches | ForEach-Object { $_.Groups[1].Value })
  $jsonIds = @($catalog.Apps | ForEach-Object { $_.Id })
  $onlyJson = $jsonIds | Where-Object { $_ -notin $modelIds }
  $onlyModels = $modelIds | Where-Object { $_ -notin $jsonIds }
  if ($onlyJson.Count -eq 0 -and $onlyModels.Count -eq 0) {
    Pass "Models.cs CreateDefault() matches apps.json 100% ($($modelIds.Count) IDs)"
  } else {
    if ($onlyJson.Count -gt 0) { Fail ("IDs in apps.json missing from Models.cs: " + ($onlyJson -join ', ')) }
    if ($onlyModels.Count -gt 0) { Warn ("IDs in Models.cs not in apps.json: " + ($onlyModels -join ', ')) }
  }
}

# 3. C# 5 Compilation & Runtime Assembly Contract Tests
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$tmpExe = Join-Path $env:TEMP ('Qinstall_qa_' + [Guid]::NewGuid().ToString('N').Substring(0,8) + '.exe')
try {
  Push-Location $ProjectRoot
  $compileOut = & $csc /nologo /target:winexe /platform:x64 /optimize+ "/out:$tmpExe" `
    /win32manifest:src\app.manifest /win32icon:src\qinstall.ico `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll `
    /r:lib\webview2\Microsoft.Web.WebView2.Core.dll /r:lib\webview2\Microsoft.Web.WebView2.WinForms.dll `
    src\Program.cs src\Models.cs src\InstallerEngine.cs src\MainForm.cs src\WebViewForm.cs src\AddAppDialog.cs 2>&1
  $compileCode = $LASTEXITCODE
  Pop-Location

  if ($compileCode -eq 0 -and (Test-Path $tmpExe)) {
    Pass "C# 5 (.NET Framework 4.8 x64) clean compilation succeeded"

    # Load compiled assembly to run real runtime unit/contract tests!
    Add-Type -AssemblyName System.Web.Extensions
    $asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($tmpExe))

    # 3a. Verify WebViewForm.GetStringList with real JavaScriptSerializer (ArrayList trap!)
    $wvfType = $asm.GetType('Qinstall.WebViewForm')
    $getStrList = $wvfType.GetMethod('GetStringList', [System.Reflection.BindingFlags]'NonPublic,Static')
    $ser = New-Object System.Web.Script.Serialization.JavaScriptSerializer
    $payload = $ser.Deserialize('{"action":"installSelected","keys":["chrome|Google.Chrome","winutil|irm ''https://christitus.com/win'' | iex"],"onlineApps":[{"id":"test","name":"Test","target":"Test.Pkg"}]}', [System.Collections.Generic.Dictionary[string,object]])
    $parsedKeys = $getStrList.Invoke($null, @($payload, 'keys'))
    if ($parsedKeys.Count -eq 2 -and $parsedKeys[0] -eq 'chrome|Google.Chrome') {
      Pass "WebViewForm.GetStringList correctly reads JavaScriptSerializer ArrayList ($($parsedKeys.Count) items)"
    } else {
      Fail "WebViewForm.GetStringList failed on JavaScriptSerializer payload (returned count=$($parsedKeys.Count))"
    }

    # 3b. Verify InstallerEngine.IsVersionOlder edge cases
    $engType = $asm.GetType('Qinstall.InstallerEngine')
    $isOlder = $engType.GetMethod('IsVersionOlder', [System.Reflection.BindingFlags]'Public,Static')
    $v1 = $isOlder.Invoke($null, @('1.9.0', '1.10.0'))
    $v2 = $isOlder.Invoke($null, @('2026.3.0.1', '2025.3'))
    $v3 = $isOlder.Invoke($null, @('4.76.6', '4.76.6'))
    if ($v1 -eq $true -and $v2 -eq $false -and $v3 -eq $false) {
      Pass "InstallerEngine.IsVersionOlder passed numeric & equality edge cases"
    } else {
      Fail "InstallerEngine.IsVersionOlder failed (v1=$v1, v2=$v2, v3=$v3)"
    }

    # 3c. Verify AppCatalog.CreateDefault
    $catType = $asm.GetType('Qinstall.AppCatalog')
    $defCat = $catType.GetMethod('CreateDefault').Invoke($null, $null)
    if ($defCat.Apps.Count -gt 0) {
      Pass "AppCatalog.CreateDefault() returned $($defCat.Apps.Count) items cleanly"
    } else {
      Fail "AppCatalog.CreateDefault() returned 0 items"
    }

    # 3d. Always update Qinstall.exe directly when -UpdateExe is requested (hot-swap via %TEMP% if currently open)
    if ($UpdateExe) {
      $finalExe = Join-Path $ProjectRoot 'Qinstall.exe'
      try {
        if (Test-Path $finalExe) {
          try {
            Copy-Item $tmpExe $finalExe -Force -ErrorAction Stop
          } catch {
            $oldStage = Join-Path $env:TEMP ('Qinstall_old_' + (Get-Date -Format 'yyyyMMdd_HHmmss') + '.exe')
            Move-Item $finalExe $oldStage -Force -ErrorAction Stop
            Copy-Item $tmpExe $finalExe -Force -ErrorAction Stop
          }
        } else {
          Copy-Item $tmpExe $finalExe -Force -ErrorAction Stop
        }
        foreach ($dll in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll')) {
          $srcDll = Join-Path (Join-Path (Join-Path $ProjectRoot 'lib') 'webview2') $dll
          $dstDll = Join-Path $ProjectRoot $dll
          if ((Test-Path $srcDll) -and (-not (Test-Path $dstDll))) {
            Copy-Item $srcDll $dstDll -Force
          }
        }
        Pass "Qinstall.exe actualizado automaticamente en $finalExe"
      } catch {
        Fail "No se pudo actualizar Qinstall.exe: $($_.Exception.Message)"
      }
    }
  } else {
    Fail ("C# compilation failed:`n" + ($compileOut -join "`n"))
  }
} catch {
  Fail "Runtime assembly test error: $($_.Exception.Message)"
} finally {
  if (Test-Path $tmpExe) { Remove-Item $tmpExe -Force -ErrorAction SilentlyContinue }
}

# 4. Anti-pattern scan in C# (unsafe object[] cast on JSON deserialized values)
$csFiles = Get-ChildItem (Join-Path $ProjectRoot 'src') -Filter '*.cs'
$badCasts = Select-String -Path $csFiles.FullName -Pattern 'as\s+object\[\]'
if ($badCasts) {
  Fail ("Unsafe 'as object[]' cast detected (JavaScriptSerializer yields ArrayList):`n" + (($badCasts | ForEach-Object { "  $($_.Filename):$($_.LineNumber): $($_.Line.Trim())" }) -join "`n"))
} else {
  Pass "No unsafe 'as object[]' deserialization casts in src/*.cs"
}

# 5. JS <-> C# Bridge Contract Parity
$appJsPath = Join-Path $ProjectRoot 'ui\app.js'
$wvfPath = Join-Path $ProjectRoot 'src\WebViewForm.cs'
$htmlPath = Join-Path $ProjectRoot 'ui\index.html'
$appJs = Get-Content -Raw -Encoding UTF8 $appJsPath
$wvfCs = Get-Content -Raw -Encoding UTF8 $wvfPath
$indexHtml = Get-Content -Raw -Encoding UTF8 $htmlPath

$jsActions = [regex]::Matches($appJs, "postToHost\(\s*\{\s*action:\s*'([^']+)'") | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
$csCases = [regex]::Matches($wvfCs, 'case\s+"([^"]+)":') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
$missingActions = $jsActions | Where-Object { $_ -notin $csCases }
if ($missingActions.Count -eq 0) {
  Pass "All $($jsActions.Count) JS postToHost actions have matching C# handlers"
} else {
  Fail ("JS actions missing C# handler: " + ($missingActions -join ', '))
}

# 6. Single-quote escaping safety in ui/app.js onclick attributes
$rawEncInOnclick = [regex]::Matches($appJs, 'encodeURIComponent\((app\.key|c|app\.name)\)')
if ($rawEncInOnclick.Count -gt 0) {
  Fail "Found $($rawEncInOnclick.Count) raw encodeURIComponent() calls on keys/names (use encodeJsArg to escape single quotes)"
} else {
  Pass "All inline onclick/oncontextmenu key encodings safely escape single quotes"
}

# 7. Check inline HTML handlers exist in app.js
$definedFns = [regex]::Matches($appJs, 'function\s+([A-Za-z0-9_]+)\s*\(') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
$jsIgnore = @('if','for','while','switch','return','event','decodeURIComponent','encodeURIComponent','encodeJsArg')
$htmlHandlers = [regex]::Matches($indexHtml, 'on(?:click|change|input|contextmenu)="([^"]+)"') | ForEach-Object {
  $expr = $_.Groups[1].Value
  [regex]::Matches($expr, '(?<!\.)\b([A-Za-z_][A-Za-z0-9_]*)\s*\(') | ForEach-Object { $_.Groups[1].Value }
} | Where-Object { $_ -notin $jsIgnore } | Sort-Object -Unique
$missingFns = $htmlHandlers | Where-Object { $_ -notin $definedFns }
if ($missingFns.Count -eq 0) {
  Pass "All $($htmlHandlers.Count) HTML event handlers in index.html exist in app.js"
} else {
  Fail ("HTML handlers undefined in app.js: " + ($missingFns -join ', '))
}

# 8. 100% Offline UI check (no external CDN scripts/styles in index.html)
$cdnRefs = [regex]::Matches($indexHtml, '(?:src|href)="https?://')
if ($cdnRefs.Count -gt 0) {
  Fail "Found $($cdnRefs.Count) external HTTP(S) asset reference(s) in ui/index.html (breaks offline mode)"
} else {
  Pass "ui/index.html has zero external CDN dependencies (100% offline-safe)"
}

# 9. Icon assets non-empty check
$iconsDir = Join-Path $ProjectRoot 'ui\icons'
if (Test-Path $iconsDir) {
  $icons = Get-ChildItem $iconsDir -Filter '*.png'
  $emptyIcons = $icons | Where-Object { $_.Length -lt 100 }
  if ($emptyIcons.Count -gt 0) {
    Fail "Found $($emptyIcons.Count) corrupted/empty PNG icons in ui/icons"
  } else {
    Pass "ui/icons verified: $($icons.Count) valid PNGs ($([Math]::Round((($icons | Measure-Object Length -Sum).Sum / 1024),1)) KB total)"
  }
}

Write-Host "`n=== RESULT: PASSED=$passed | WARNINGS=$warnings | FAILED=$failed ===" -ForegroundColor Cyan
if ($failed -gt 0) { exit 1 } else { exit 0 }
