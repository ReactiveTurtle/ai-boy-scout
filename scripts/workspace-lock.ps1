# Shared FIFO protocol for the build/test and update locks. The main lock file
# remains compatible with processes started before queues were introduced.
function Open-WorkspaceLockFile {
    param([string]$Path, [System.IO.FileShare]$Share)

    try {
        return [System.IO.File]::Open($Path, [System.IO.FileMode]::OpenOrCreate,
            [System.IO.FileAccess]::ReadWrite, $Share)
    } catch [System.IO.IOException] {
        if (($_.Exception.HResult -band 0xFFFF) -notin @(32, 33)) { throw }
        return $null
    }
}

function Get-WorkspaceLockOwner {
    param([string]$Path)

    $reader = $null
    try {
        $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        $reader = [System.IO.StreamReader]::new($stream)
        $owner = $reader.ReadToEnd() | ConvertFrom-Json
        if ($null -eq $owner -or -not $owner.sessionId) {
            return 'Owner metadata is being written; retrying.'
        }
        return "Owner: session '$($owner.sessionId)', PID $($owner.processId), host '$($owner.hostName)', started $($owner.startedAt). Command: $($owner.command)"
    } catch {
        return 'Owner metadata is temporarily unavailable; retrying.'
    } finally {
        if ($null -ne $reader) { $reader.Dispose() }
    }
}

function Write-WorkspaceLockMetadata {
    param([System.IO.FileStream]$Stream, $Metadata)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes(($Metadata | ConvertTo-Json -Compress))
    $Stream.SetLength(0)
    $Stream.Write($bytes, 0, $bytes.Length)
    $Stream.Flush($true)
}

function Get-WorkspaceQueueTickets {
    param([string]$Directory)

    # Called only under the queue guard. A live ticket holds a write handle
    # that allows readers only. An abandoned ticket can be opened for writing.
    foreach ($path in ([System.IO.Directory]::GetFiles($Directory, '*.json') | Sort-Object)) {
        $probe = $null
        try {
            $probe = [System.IO.File]::Open($path, [System.IO.FileMode]::Open,
                [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        } catch [System.IO.IOException] {
            if (($_.Exception.HResult -band 0xFFFF) -notin @(32, 33)) { throw }
            $path
        } finally {
            if ($null -ne $probe) { $probe.Dispose() }
        }
        if ($null -ne $probe) { [System.IO.File]::Delete($path) }
    }
}

function Enter-WorkspaceQueueLock {
    param(
        [string]$LockPath,
        [string]$SessionId,
        [string]$Command,
        [string]$Description
    )

    $queueDirectory = "$LockPath.queue"
    [System.IO.Directory]::CreateDirectory($queueDirectory) | Out-Null
    $ticket = $null
    $ticketPath = $null
    $lock = $null
    $lastReport = $null
    try {
        while ($null -eq $lock) {
            $guard = Open-WorkspaceLockFile -Path "$LockPath.queue.lock" -Share None
            if ($null -eq $guard) {
                Start-Sleep -Milliseconds 100
                continue
            }
            try {
                $tickets = @(Get-WorkspaceQueueTickets -Directory $queueDirectory)
                if ($null -eq $ticket) {
                    # Registration is serialized. Monotonic names retain FIFO
                    # even if the wall clock moves backwards while tickets wait.
                    $sequence = [DateTime]::UtcNow.Ticks
                    if ($tickets.Count -gt 0) {
                        $lastSequence = [long]([System.IO.Path]::GetFileName($tickets[-1]).Split('-')[0])
                        $sequence = [Math]::Max($sequence, $lastSequence + 1)
                    }
                    $requestId = [Guid]::NewGuid().ToString('N')
                    $ticketPath = Join-Path $queueDirectory ("{0:D20}-{1}.json" -f $sequence, $requestId)
                    $ticket = [System.IO.File]::Open($ticketPath, [System.IO.FileMode]::CreateNew,
                        [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::Read)
                    Write-WorkspaceLockMetadata -Stream $ticket -Metadata ([ordered]@{
                        requestId = $requestId
                        sessionId = $SessionId
                        processId = $PID
                        hostName = [Environment]::MachineName
                        queuedAt = [DateTimeOffset]::UtcNow.ToString('o')
                        command = $Command
                    })
                    $tickets += $ticketPath
                    Write-Host "Session '$SessionId' joined the $Description queue at position $($tickets.Count) (request $requestId, PID $PID)."
                }

                $position = [Array]::IndexOf($tickets, $ticketPath) + 1
                if ($position -eq 1) {
                    $lock = Open-WorkspaceLockFile -Path $LockPath -Share Read
                }
                if ($null -ne $lock) {
                    Write-WorkspaceLockMetadata -Stream $lock -Metadata ([ordered]@{
                        sessionId = $SessionId
                        processId = $PID
                        hostName = [Environment]::MachineName
                        startedAt = [DateTimeOffset]::UtcNow.ToString('o')
                        command = $Command
                    })
                    $ticket.Dispose()
                    [System.IO.File]::Delete($ticketPath)
                    $ticket = $null
                    Write-Host "$Description lock acquired by session '$SessionId' (PID $PID)."
                } else {
                    $report = "Session '$SessionId' is waiting for the $Description lock. Queue position: $position of $($tickets.Count). $(Get-WorkspaceLockOwner -Path $LockPath)"
                    if ($report -ne $lastReport) {
                        Write-Host $report
                        $lastReport = $report
                    }
                }
            } finally {
                $guard.Dispose()
            }
            if ($null -eq $lock) { Start-Sleep -Milliseconds 500 }
        }
        $result = $lock
        $lock = $null
        return $result
    } finally {
        if ($null -ne $lock) { $lock.Dispose() }
        # No deletion outside the guard: after closing the handle another
        # participant may already be removing this abandoned ticket.
        if ($null -ne $ticket) { $ticket.Dispose() }
    }
}
