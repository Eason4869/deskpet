# Native UI fixture: Chromium may open idle speculative sockets before its GET.
# Keep accepting connections while incomplete headers await data. Everything is
# bounded and owned by the background job whose termination closes the listener.
$previewListener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, $previewPort)
$previewListener.Start()
$previewConnections = New-Object 'System.Collections.Generic.List[object]'
[IO.File]::WriteAllText($previewReadyPath, 'ready')
Write-Output "http://user:secret@127.0.0.1:$previewPort/fixture?token=secret#fragment"
$previewBody = [Text.Encoding]::UTF8.GetBytes('<html><body><p id=fixture>DeskPet native job preview</p></body></html>')
$previewHeader = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 200 OK`r`nContent-Type: text/html; charset=utf-8`r`nContent-Length: $($previewBody.Length)`r`nConnection: close`r`n`r`n")
while ($true) {
    while ($previewListener.Pending()) {
        $previewClient = $previewListener.AcceptTcpClient()
        if ($previewConnections.Count -ge 32) { $previewClient.Close(); continue }
        $previewStream = $previewClient.GetStream()
        $previewBuffer = New-Object byte[] 8192
        $previewConnections.Add(@{
            Client = $previewClient; Stream = $previewStream; Buffer = $previewBuffer
            Read = $previewStream.ReadAsync($previewBuffer, 0, $previewBuffer.Length)
            Text = ''; Expires = [DateTime]::UtcNow.AddSeconds(60)
        })
        [IO.File]::WriteAllText($previewAcceptedPath, 'accepted')
    }
    for ($previewIndex = $previewConnections.Count - 1; $previewIndex -ge 0; $previewIndex--) {
        $previewEntry = $previewConnections[$previewIndex]
        $previewDrop = $false
        try {
            if ($previewEntry.Read.IsCompleted) {
                $previewCount = $previewEntry.Read.GetAwaiter().GetResult()
                if ($previewCount -eq 0) { $previewDrop = $true }
                else {
                    $previewEntry.Text += [Text.Encoding]::ASCII.GetString($previewEntry.Buffer, 0, $previewCount)
                    if ($previewEntry.Text.Length -gt 32768) { $previewDrop = $true }
                    elseif ($previewEntry.Text.Contains("`r`n`r`n")) {
                        $previewLine = $previewEntry.Text.Substring(0, $previewEntry.Text.IndexOf("`r`n"))
                        [IO.File]::AppendAllText($previewRequestsPath, $previewLine + [Environment]::NewLine)
                        $previewEntry.Stream.Write($previewHeader, 0, $previewHeader.Length)
                        $previewEntry.Stream.Write($previewBody, 0, $previewBody.Length)
                        $previewEntry.Stream.Flush()
                        $previewDrop = $true
                    }
                    else { $previewEntry.Read = $previewEntry.Stream.ReadAsync($previewEntry.Buffer, 0, $previewEntry.Buffer.Length) }
                }
            }
            if ([DateTime]::UtcNow -gt $previewEntry.Expires) { $previewDrop = $true }
        }
        catch { $previewDrop = $true }
        if ($previewDrop) { $previewEntry.Client.Close(); $previewConnections.RemoveAt($previewIndex) }
    }
    Start-Sleep -Milliseconds 10
}
