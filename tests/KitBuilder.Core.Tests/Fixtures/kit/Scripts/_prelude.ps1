# Dot-sourced by _system\Invoke-Phase.ps1 before every phase.
# Defines the helpers and variables that the customization scripts expect.

function Show-Header {
    param( [string] $Text );
    if( $Text ) { $Text; }
}

function Wait-IfNotSpecialize {
    # Intentionally a no-op: unattended setup must never block on input.
}

# $Settings: settings.json from the kit, copied here by the bootstrap.
# $Secrets:  secrets.json, same; deleted at the end of the firstlogon phase, so
#            scripts that need a password must run in specialize or firstlogon.
$_kitCopyRoot = 'C:\Windows\Setup\Scripts\Custom';
$Settings = $null;
$Secrets = $null;
foreach( $_pair in @( @( 'Settings', 'settings.json' ), @( 'Secrets', '_secrets.json' ) ) ) {
    $_path = Join-Path -Path $_kitCopyRoot -ChildPath $_pair[1];
    if( Test-Path -LiteralPath $_path ) {
        try {
            Set-Variable -Name $_pair[0] -Value ( Get-Content -LiteralPath $_path -Raw | ConvertFrom-Json );
        } catch {
            "*** WARNING: could not read {0}: {1}" -f $_path, $_.Exception.Message;
        }
    }
}

# Root of the kit drive (e.g. 'E:\'), or $null once the USB has been pulled.
# Same marker probe the bootstrap uses.
function Get-KitRoot {
    [System.IO.DriveInfo]::GetDrives() |
        Where-Object -FilterScript { $_.IsReady } |
        ForEach-Object -Process { $_.RootDirectory.FullName; } |
        Where-Object -FilterScript { Test-Path -LiteralPath ( Join-Path -Path $_ -ChildPath 'Scripts\_scripts.marker' ); } |
        Select-Object -First 1;
}
