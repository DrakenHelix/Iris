param([switch]$Once)
$CommandDirectory = '#COMMAND_DIRECTORY#'
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    $managerType = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType=WindowsRuntime]
    $propertiesType = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties, Windows.Media.Control, ContentType=WindowsRuntime]
    $streamType = [Windows.Storage.Streams.IRandomAccessStreamWithContentType, Windows.Storage.Streams, ContentType=WindowsRuntime]
    $randomStreamType = [Windows.Storage.Streams.IRandomAccessStream, Windows.Storage.Streams, ContentType=WindowsRuntime]
    $referenceType = [Windows.Storage.Streams.IRandomAccessStreamReference, Windows.Storage.Streams, ContentType=WindowsRuntime]
    $asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
    function Await-WinRT($Operation, $ResultType) {
        $task = $asTask.MakeGenericMethod($ResultType).Invoke($null, @($Operation))
        if (-not $task.Wait(4000)) { throw 'Media request timed out' }
        return $task.Result
    }
    function Read-ThumbnailReference($Reference) {
        # WinRT returns a COM interface here. PowerShell cannot discover its inherited
        # stream members dynamically; invoke the declared interfaces explicitly.
        $operation = $referenceType.GetMethod('OpenReadAsync').Invoke($Reference, @())
        $stream = Await-WinRT $operation $streamType
        try {
            $size = [uint64]$randomStreamType.GetProperty('Size').GetValue($stream, $null)
            if ($size -eq 0 -or $size -ge 8388608) { return '' }
            $input = $randomStreamType.GetMethod('GetInputStreamAt').Invoke($stream, @([uint64]0))
            $readStream = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($input)
            $memory = New-Object IO.MemoryStream
            try { $readStream.CopyTo($memory); return [Convert]::ToBase64String($memory.ToArray()) }
            finally { $memory.Dispose(); $readStream.Dispose() }
        } finally {
            try { [IDisposable].GetMethod('Dispose').Invoke($stream, $null) | Out-Null } catch {}
        }
    }
    $manager = Await-WinRT ($managerType::RequestAsync()) $managerType
    $nextPoll = [DateTime]::MinValue
    $lastArtKey = $null
    $lastArtEmpty = $true
    $artAttempts = 0
    $nextArtRetry = [DateTime]::MinValue
    do {
        $session = $manager.GetCurrentSession()
        if ([IO.Directory]::Exists($CommandDirectory)) {
            foreach ($commandFile in @([IO.Directory]::GetFiles($CommandDirectory, '*.json') | Sort-Object)) {
                $command = $null
                try {
                    $command = [IO.File]::ReadAllText($commandFile) | ConvertFrom-Json
                    if ($null -eq $session) { throw 'No active media session' }
                    $success = $false
                    switch ($command.action) {
                        'toggle' { $success = Await-WinRT ($session.TryTogglePlayPauseAsync()) ([bool]) }
                        'next' { $success = Await-WinRT ($session.TrySkipNextAsync()) ([bool]) }
                        'previous' { $success = Await-WinRT ($session.TrySkipPreviousAsync()) ([bool]) }
                        'seek' { $success = Await-WinRT ($session.TryChangePlaybackPositionAsync([long]([double]$command.seconds * 10000000))) ([bool]) }
                    }
                    @{ kind='command'; id=$command.id; success=$success } | ConvertTo-Json -Compress
                    $nextPoll = [DateTime]::MinValue
                } catch { @{ kind='command'; id=$command.id; success=$false; error=$_.Exception.Message } | ConvertTo-Json -Compress }
                finally { [IO.File]::Delete($commandFile) }
            }
        }
        if ([DateTime]::UtcNow -ge $nextPoll) {
            $nextPoll = [DateTime]::UtcNow.AddMilliseconds(250)
            try {
                if ($null -eq $session) {
                    @{ available=$false } | ConvertTo-Json -Compress
                    $lastArtKey = $null
                    $lastArtEmpty = $true
                    $artAttempts = 0
                } else {
                    $properties = Await-WinRT ($session.TryGetMediaPropertiesAsync()) $propertiesType
                    $timeline = $session.GetTimelineProperties()
                    $playback = $session.GetPlaybackInfo()
                    $key = $session.SourceAppUserModelId + '|' + $properties.Title + '|' + $properties.Artist + '|' + $properties.AlbumTitle
                    if ($key -ne $lastArtKey) { $artAttempts=0; $lastArtEmpty=$true; $nextArtRetry=[DateTime]::MinValue }
                    $artChanged = ($key -ne $lastArtKey) -or ($lastArtEmpty -and [DateTime]::UtcNow -ge $nextArtRetry)
                    $art = ''
                    if ($artChanged) {
                        $lastArtKey = $key
                        try {
                            if ($null -ne $properties.Thumbnail) {
                                $art = Read-ThumbnailReference $properties.Thumbnail
                            }
                        } catch { $art = '' }
                        $artAttempts++
                        $lastArtEmpty = [String]::IsNullOrEmpty($art)
                        $nextArtRetry = [DateTime]::UtcNow.AddSeconds(3)
                    }
                    @{
                        available=$true; title=$properties.Title; artist=$properties.Artist; album=$properties.AlbumTitle
                        albumArtist=$properties.AlbumArtist; source=$session.SourceAppUserModelId; playing=($playback.PlaybackStatus.ToString() -eq 'Playing')
                        start=$timeline.StartTime.TotalSeconds
                        position=[Math]::Max(0,($timeline.Position-$timeline.StartTime).TotalSeconds); duration=[Math]::Max(0,($timeline.EndTime-$timeline.StartTime).TotalSeconds)
                        minSeek=[Math]::Max(0,($timeline.MinSeekTime-$timeline.StartTime).TotalSeconds); maxSeek=[Math]::Max(0,($timeline.MaxSeekTime-$timeline.StartTime).TotalSeconds)
                        updated=$timeline.LastUpdatedTime.UtcDateTime.ToString('o')
                        canSeek=$playback.Controls.IsPlaybackPositionEnabled
                        artChanged=$artChanged; thumbnail=$art
                    } | ConvertTo-Json -Compress
                }
            } catch { @{ available=$false; error=$_.Exception.Message } | ConvertTo-Json -Compress }
        }
        if (-not $Once) { Start-Sleep -Milliseconds 50 }
    } while (-not $Once)
} catch { @{ available=$false; error=$_.Exception.Message } | ConvertTo-Json -Compress }
