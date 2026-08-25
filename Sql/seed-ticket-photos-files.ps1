<#
    Generates a small set of placeholder JPEG files on disk, matching the
    TicketPhotos rows Sql\seed-local-dev.sql inserts for TCKT-0001 (1 photo)
    and TCKT-0008 (4 photos) — run these two scripts together, by hand, in
    either order, on the same UTC calendar day (both compute "today"
    independently — SYSUTCDATETIME() there, DateTime.UtcNow here) so the DB
    rows and the files they point at actually line up.

    This only ever writes inside TicketPhotos\ under this repo's root. It
    does not touch the database, does not call sqlcmd, and (like its
    companion, seed-local-dev.sql) must never be wrapped in another script,
    task, pipeline, CI job, or any other automated invocation — run it by
    hand, deliberately, same as seed-local-dev.sql.

    Usage (path resolution is relative to this script's own location, not
    your current directory, so this can be run from anywhere):

        powershell -File Sql\seed-ticket-photos-files.ps1
#>

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$dateFolder = (Get-Date).ToUniversalTime().ToString('yyyy/MM/dd')
$targetDir = Join-Path $repoRoot ("TicketPhotos/" + $dateFolder)
New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

function New-PlaceholderJpeg {
    param(
        [Parameter(Mandatory)] [string] $Guid,
        [Parameter(Mandatory)] [string] $Label,
        [Parameter(Mandatory)] [string] $HexColor
    )

    $path = Join-Path $targetDir "$Guid.jpeg"
    $color = [System.Drawing.ColorTranslator]::FromHtml($HexColor)

    $bitmap = New-Object System.Drawing.Bitmap 400, 300
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear($color)

        $font = New-Object System.Drawing.Font('Segoe UI', 24, [System.Drawing.FontStyle]::Bold)
        $format = New-Object System.Drawing.StringFormat
        $format.Alignment = [System.Drawing.StringAlignment]::Center
        $format.LineAlignment = [System.Drawing.StringAlignment]::Center
        $rect = New-Object System.Drawing.RectangleF(0, 0, 400, 300)

        $graphics.DrawString($Label, $font, [System.Drawing.Brushes]::White, $rect, $format)
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Jpeg)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }

    Write-Host "Wrote $path"
}

# GUIDs here must match Sql\seed-local-dev.sql's TicketPhotos INSERT exactly.
New-PlaceholderJpeg -Guid '44444444-4444-4444-4444-444444444444' -Label "TCKT-0001`nPhoto 1" -HexColor '#2E7D32'

New-PlaceholderJpeg -Guid '55555555-5555-5555-5555-555555555555' -Label "TCKT-0008`nPhoto 1" -HexColor '#1565C0'
New-PlaceholderJpeg -Guid '66666666-6666-6666-6666-666666666666' -Label "TCKT-0008`nPhoto 2" -HexColor '#6A1B9A'
New-PlaceholderJpeg -Guid '77777777-7777-7777-7777-777777777777' -Label "TCKT-0008`nPhoto 3" -HexColor '#C62828'
New-PlaceholderJpeg -Guid '88888888-8888-8888-8888-888888888888' -Label "TCKT-0008`nPhoto 4" -HexColor '#EF6C00'

Write-Host "`nDone. 5 placeholder photos written under $targetDir"
Write-Host "Now also run Sql\seed-local-dev.sql (sqlcmd, by hand) if you haven't already, so the TicketPhotos rows exist to match these files."
