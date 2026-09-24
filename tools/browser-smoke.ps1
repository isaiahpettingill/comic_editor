param([int]$Port = 8098, [int]$DebugPort = 9234, [string]$WebRoot, [switch]$CheckPreferences, [switch]$CheckRecovery, [switch]$CheckPalettes)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $workspace 'artifacts'
if (!$WebRoot) { $WebRoot = Join-Path $artifactRoot 'browser/wwwroot' }
$profile = Join-Path $artifactRoot ('chrome-smoke-' + [Guid]::NewGuid().ToString('N'))
$server = Start-Process python -ArgumentList @('-m','http.server',"$Port",'--bind','127.0.0.1','--directory',"`"$webRoot`"") -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $artifactRoot 'web-server.log') -RedirectStandardError (Join-Path $artifactRoot 'web-server-error.log')
$chrome = Start-Process 'C:/Program Files/Google/Chrome/Application/chrome.exe' -ArgumentList @('--headless=new',"--remote-debugging-port=$DebugPort","--user-data-dir=$profile",'--no-first-run','--no-default-browser-check','--enable-unsafe-swiftshader','--window-size=1280,800',"http://127.0.0.1:$Port") -PassThru -WindowStyle Hidden
$socket = [System.Net.WebSockets.ClientWebSocket]::new()
$script:commandId = 0
$script:errors = [System.Collections.Generic.List[string]]::new()
function Invoke-Cdp([string]$method, [hashtable]$parameters = @{}) {
    $script:commandId++
    $bytes = [Text.Encoding]::UTF8.GetBytes((@{id=$script:commandId;method=$method;params=$parameters} | ConvertTo-Json -Depth 20 -Compress))
    $socket.SendAsync([ArraySegment[byte]]::new($bytes),[Net.WebSockets.WebSocketMessageType]::Text,$true,[Threading.CancellationToken]::None).GetAwaiter().GetResult()
    while ($true) {
        $message = [IO.MemoryStream]::new()
        do {
            $buffer = [byte[]]::new(65536)
            $received = $socket.ReceiveAsync([ArraySegment[byte]]::new($buffer),[Threading.CancellationToken]::None).GetAwaiter().GetResult()
            $message.Write($buffer,0,$received.Count)
        } while (!$received.EndOfMessage)
        $response = [Text.Encoding]::UTF8.GetString($message.ToArray()) | ConvertFrom-Json
        $message.Dispose()
        if ($response.method -eq 'Runtime.exceptionThrown') { $script:errors.Add(($response.params | ConvertTo-Json -Depth 15 -Compress)) }
        if ($response.method -eq 'Runtime.consoleAPICalled' -and $response.params.type -eq 'error') { $script:errors.Add(($response.params.args | ConvertTo-Json -Depth 15 -Compress)) }
        if ($response.id -eq $script:commandId) { return $response }
    }
}
try {
    $page = $null
    for ($attempt=0; $attempt -lt 30 -and !$page; $attempt++) {
        try { $page = (Invoke-RestMethod "http://127.0.0.1:$DebugPort/json") | Where-Object type -eq 'page' | Select-Object -First 1 } catch { }
        if (!$page) { Start-Sleep -Milliseconds 500 }
    }
    if (!$page) { throw 'Chrome debugging endpoint did not start.' }
    $socket.ConnectAsync([Uri]$page.webSocketDebuggerUrl,[Threading.CancellationToken]::None).GetAwaiter().GetResult()
    $null = Invoke-Cdp 'Runtime.enable'
    for ($attempt=0; $attempt -lt 90; $attempt++) {
        Start-Sleep -Seconds 1
        $state = Invoke-Cdp 'Runtime.evaluate' @{expression='JSON.stringify({canvases:document.querySelectorAll("canvas").length,text:document.body.innerText})';returnByValue=$true}
        if ($state.result.result.value -match '"canvases":[1-9]') { Start-Sleep -Seconds 3; break }
        if ($script:errors.Count -gt 0) { break }
    }
    if ($CheckPreferences) {
        $null = Invoke-Cdp 'Runtime.evaluate' @{expression='globalThis.comicEditorSession?.save(null)';awaitPromise=$true}
        $null = Invoke-Cdp 'Runtime.evaluate' @{expression='localStorage.setItem("comic-editor.preferences", JSON.stringify({CanvasWidth:384,CanvasHeight:216,Tool:10,Color:999,Language:"unknown",FontSize:27,Tools:{Spray:{Size:25,SprayDensity:32}}}))'}
        $null = Invoke-Cdp 'Page.reload'
        $preferences = $null
        for ($attempt=0; $attempt -lt 45; $attempt++) {
            Start-Sleep -Seconds 1
            $stored = Invoke-Cdp 'Runtime.evaluate' @{expression='localStorage.getItem("comic-editor.preferences")';returnByValue=$true}
            if ($stored.result.result.value) { $preferences = $stored.result.result.value | ConvertFrom-Json }
            # The .NET reader clamps color and resolves unknown languages, then saves through JS interop.
            if ($preferences.Color -eq 127 -and $preferences.Language -eq 'en') { break }
        }
        if ($preferences.CanvasWidth -ne 384 -or $preferences.CanvasHeight -ne 216 -or $preferences.Tool -ne 10 -or $preferences.Color -ne 127 -or $preferences.Language -ne 'en' -or $preferences.Tools.Spray.Size -ne 25) { throw 'Browser preference read/write round trip failed.' }
        'Browser preferences survived reload and passed through the .NET storage bridge.'
        Start-Sleep -Seconds 2
    }
    if ($CheckRecovery) {
        $snapshot = $null
        for ($attempt=0; $attempt -lt 20; $attempt++) {
            Start-Sleep -Seconds 1
            $stored = Invoke-Cdp 'Runtime.evaluate' @{expression='globalThis.comicEditorSession.load()';awaitPromise=$true;returnByValue=$true}
            if ($stored.result.result.value) { $snapshot = $stored.result.result.value | ConvertFrom-Json; break }
        }
        if (!$snapshot.Project) { throw 'The browser did not write a project recovery snapshot.' }
        $null = Invoke-Cdp 'Runtime.evaluate' @{expression='(async()=>{const s=JSON.parse(await comicEditorSession.load());s.FileName="browser-recovery.cutscene";s.Dirty=true;s.Frame=999;await comicEditorSession.save(JSON.stringify(s))})()';awaitPromise=$true}
        $null = Invoke-Cdp 'Page.reload'
        for ($attempt=0; $attempt -lt 30; $attempt++) {
            Start-Sleep -Seconds 1
            $stored = Invoke-Cdp 'Runtime.evaluate' @{expression='globalThis.comicEditorSession?.load()';awaitPromise=$true;returnByValue=$true}
            if ($stored.result.result.value) { $snapshot = $stored.result.result.value | ConvertFrom-Json }
            # The .NET restore clamps the selected frame before its next recovery write.
            if ($snapshot.Frame -eq 0 -and $snapshot.FileName -eq 'browser-recovery.cutscene' -and $snapshot.Dirty) { break }
        }
        if ($snapshot.Frame -ne 0 -or $snapshot.FileName -ne 'browser-recovery.cutscene' -or !$snapshot.Dirty) { throw 'Browser project recovery did not survive reload through .NET.' }
        'Browser project recovery survived reload with unsaved work intact.'
    }
    if ($CheckPalettes) {
        $null = Invoke-Cdp 'Runtime.evaluate' @{expression='comicEditorPalettes.write("Smoke.gpl", "GIMP Palette\nName: Smoke\n0 0 0 Black\n255 255 255 White\n")';awaitPromise=$true}
        $null = Invoke-Cdp 'Page.reload'
        Start-Sleep -Seconds 6
        $palettes = Invoke-Cdp 'Runtime.evaluate' @{expression='(async()=>JSON.stringify({files:await comicEditorPalettes.list(),text:await comicEditorPalettes.read("Smoke.gpl")}))()';awaitPromise=$true;returnByValue=$true}
        $saved = $palettes.result.result.value | ConvertFrom-Json
        if ($saved.files -notcontains 'Smoke.gpl' -or $saved.text -notmatch '255 255 255 White') { throw 'Browser palette library did not survive reload.' }
        'Browser GPL palette library survived reload.'
    }
    $capture = Invoke-Cdp 'Page.captureScreenshot' @{format='png'}
    [IO.File]::WriteAllBytes((Join-Path $artifactRoot 'browser-smoke.png'),[Convert]::FromBase64String($capture.result.data))
    $script:errors | Set-Content (Join-Path $artifactRoot 'browser-console-errors.json')
    $state.result.result.value
    if ($script:errors.Count -gt 0) { throw ($script:errors -join "`n") }
    if ($state.result.result.value -notmatch '"canvases":[1-9]') { throw 'Application did not create a canvas.' }
} finally {
    $socket.Dispose()
    if (!$chrome.HasExited) { Stop-Process -Id $chrome.Id -ErrorAction SilentlyContinue }
    if (!$server.HasExited) { Stop-Process -Id $server.Id -ErrorAction SilentlyContinue }
}
