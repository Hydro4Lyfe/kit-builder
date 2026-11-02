<#
.SYNOPSIS
    Builds the deployment kit: renders autounattend.xml and stages the media files.

.DESCRIPTION
    1. Runs Test-Kit.ps1 and stops if it finds errors.
    2. Fills autounattend.template.xml from settings.json and secrets.json.
    3. Stages everything the install needs in out\kit\.
    4. Optionally builds an ISO, and/or copies the kit to a USB drive.

    Works in Windows PowerShell 5.1 and PowerShell 7 (including pwsh on Linux,
    where the ISO is built with xorriso).

.PARAMETER Iso
    Also build an ISO. Attach it to a VM as a second DVD drive next to a stock
    Windows 11 ISO; Setup finds autounattend.xml on any drive's root.

.PARAMETER IsoPath
    Where to write the ISO. Default: out\unattend.iso.

.PARAMETER UsbDrive
    Copy the kit to the root of this drive, e.g. E:. The kit folders on the drive
    are mirrored, so scripts deleted from the kit are deleted from the USB too.
    Everything else on the drive is left alone, including Windows install files
    and BitLockerKeys\.

.EXAMPLE
    .\tools\Build-Kit.ps1 -UsbDrive E:

.EXAMPLE
    .\tools\Build-Kit.ps1 -Iso
#>
[CmdletBinding()]
param(
    [switch] $Iso,
    [string] $IsoPath,
    [string] $UsbDrive
);

$ErrorActionPreference = 'Stop';

$kitRoot = Split-Path -Parent $PSScriptRoot;
$outRoot = Join-Path -Path $kitRoot -ChildPath 'out';
$stage = Join-Path -Path $outRoot -ChildPath 'kit';
if( -not $IsoPath ) {
    $IsoPath = Join-Path -Path $outRoot -ChildPath 'unattend.iso';
}
# $IsWindows does not exist in Windows PowerShell 5.1.
$onWindows = $env:OS -eq 'Windows_NT';

# Folders copied to the media root. Root files (autounattend.xml, settings.json,
# secrets.json) are written separately.
$kitFolders = @( 'Scripts', 'CustomAssets', 'Drivers', '$WinPEDriver$' );

function New-IsoFromFolder {
    param(
        [string] $Source,
        [string] $Path,
        [string] $VolumeName
    );
    # IMAPI2 is built into Windows, so no ADK/oscdimg install is needed. Its result
    # is a COM IStream, which PowerShell cannot write to a file without this helper.
    if( -not ( 'IsoStreamWriter' -as [type] ) ) {
        Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

public static class IsoStreamWriter
{
    public static void Write(object comStream, string path, int blockSize, int totalBlocks)
    {
        IStream input = (IStream)comStream;
        byte[] buffer = new byte[blockSize];
        IntPtr bytesRead = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            using (FileStream output = File.Create(path))
            {
                for (int i = 0; i < totalBlocks; i++)
                {
                    input.Read(buffer, blockSize, bytesRead);
                    output.Write(buffer, 0, Marshal.ReadInt32(bytesRead));
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(bytesRead);
        }
    }
}
'@;
    }
    $image = New-Object -ComObject 'IMAPI2FS.MsftFileSystemImage';
    # Blu-ray (BDR) defaults only to lift the DVD size cap for large driver packs.
    $image.ChooseImageDefaultsForMediaType( 18 );
    # ISO9660 + Joliet + UDF. Windows reads the UDF tree, so long driver file names survive.
    $image.FileSystemsToCreate = 7;
    $image.VolumeName = $VolumeName;
    $image.Root.AddTree( $Source, $false );
    $result = $image.CreateResultImage();
    [IsoStreamWriter]::Write( $result.ImageStream, $Path, $result.BlockSize, $result.TotalBlocks );
}

# --------------------------------------------------------------------------
# 1. Check
# --------------------------------------------------------------------------

& ( Join-Path -Path $PSScriptRoot -ChildPath 'Test-Kit.ps1' ) -KitRoot $kitRoot;
if( $LASTEXITCODE -ne 0 ) {
    throw 'Test-Kit found errors (listed above). Fix them and build again.';
}

# --------------------------------------------------------------------------
# 2. Render autounattend.xml
# --------------------------------------------------------------------------

$settings = Get-Content -LiteralPath ( Join-Path -Path $kitRoot -ChildPath 'settings.json' ) -Raw | ConvertFrom-Json;
$secrets = Get-Content -LiteralPath ( Join-Path -Path $kitRoot -ChildPath 'secrets.json' ) -Raw | ConvertFrom-Json;

$tokens = [ordered] @{
    UILanguage    = $settings.language.uiLanguage;
    InputLocale   = $settings.language.inputLocale;
    SystemLocale  = $settings.language.systemLocale;
    UserLocale    = $settings.language.userLocale;
    Edition       = $settings.edition;
    TimeZone      = $settings.timeZone;
    AdminPassword = $secrets.adminPassword;
};

$xml = Get-Content -LiteralPath ( Join-Path -Path $kitRoot -ChildPath 'autounattend.template.xml' ) -Raw;
foreach( $name in $tokens.Keys ) {
    # Escaped so a value containing & or < cannot break the XML.
    $xml = $xml.Replace( "{{$name}}", [System.Security.SecurityElement]::Escape( [string] $tokens[$name] ) );
}

$leftover = @( [regex]::Matches( $xml, '\{\{(\w+)\}\}' ) | ForEach-Object -Process { $_.Groups[1].Value; } | Sort-Object -Unique );
if( $leftover.Count -gt 0 ) {
    throw "autounattend.template.xml uses unknown placeholder(s): $( $leftover -join ', ' ). Add them to `$tokens in Build-Kit.ps1.";
}
try {
    [xml] $xml | Out-Null;
} catch {
    throw "The rendered autounattend.xml is not valid XML: $( $_.Exception.Message )";
}

# --------------------------------------------------------------------------
# 3. Stage out\kit\
# --------------------------------------------------------------------------

if( Test-Path -LiteralPath $stage ) {
    Remove-Item -LiteralPath $stage -Recurse -Force;
}
New-Item -Path $stage -ItemType 'Directory' -Force | Out-Null;

[System.IO.File]::WriteAllText( ( Join-Path -Path $stage -ChildPath 'autounattend.xml' ), $xml, [System.Text.UTF8Encoding]::new( $false ) );
Copy-Item -LiteralPath ( Join-Path -Path $kitRoot -ChildPath 'settings.json' ) -Destination $stage;
Copy-Item -LiteralPath ( Join-Path -Path $kitRoot -ChildPath 'secrets.json' ) -Destination $stage;
foreach( $folder in $kitFolders ) {
    $source = Join-Path -Path $kitRoot -ChildPath $folder;
    if( Test-Path -LiteralPath $source ) {
        Copy-Item -LiteralPath $source -Destination $stage -Recurse -Force;
    }
}

# Files copied in from Windows into WSL carry these "downloaded from the internet" markers.
Get-ChildItem -LiteralPath $stage -Recurse -File -Force |
    Where-Object -FilterScript { $_.Name -like '*:Zone.Identifier' } |
    Remove-Item -Force;

$size = ( Get-ChildItem -LiteralPath $stage -Recurse -File | Measure-Object -Property 'Length' -Sum ).Sum;
Write-Host ( 'Staged kit in {0} ({1:N1} MB).' -f $stage, ( $size / 1MB ) );

# --------------------------------------------------------------------------
# 4a. ISO
# --------------------------------------------------------------------------

if( $Iso ) {
    if( Test-Path -LiteralPath $IsoPath ) {
        Remove-Item -LiteralPath $IsoPath -Force;
    }
    if( $onWindows ) {
        New-IsoFromFolder -Source $stage -Path $IsoPath -VolumeName 'UNATTEND';
    } else {
        if( -not ( Get-Command -Name 'xorriso' -ErrorAction 'SilentlyContinue' ) ) {
            throw 'xorriso not found. Install it with: sudo apt install xorriso';
        }
        # -J/-joliet-long: Windows reads Joliet names; without them it sees 8.3
        # uppercase names and _scripts.marker would not be found.
        & xorriso -as mkisofs -quiet -o $IsoPath -V 'UNATTEND' -J -joliet-long -R $stage;
        if( $LASTEXITCODE -ne 0 ) {
            throw "xorriso failed with exit code $LASTEXITCODE.";
        }
    }
    Write-Host "Built $IsoPath";
}

# --------------------------------------------------------------------------
# 4b. USB
# --------------------------------------------------------------------------

if( $UsbDrive ) {
    if( -not $onWindows ) {
        throw '-UsbDrive only works on Windows.';
    }
    $drive = $UsbDrive.TrimEnd( '\' );
    if( $drive -notmatch '^[A-Za-z]:$' ) {
        throw "Give -UsbDrive as a drive letter, like E: (got '$UsbDrive').";
    }
    if( $drive -ieq $env:SystemDrive ) {
        throw "Refusing to write the kit to the system drive ($drive).";
    }
    $usbRoot = "$drive\";
    if( -not ( Test-Path -LiteralPath $usbRoot ) ) {
        throw "Drive $drive not found.";
    }

    $keyDir = Join-Path -Path $usbRoot -ChildPath 'BitLockerKeys';
    $keyCount = @( Get-ChildItem -LiteralPath $keyDir -File -ErrorAction 'SilentlyContinue' ).Count;
    if( $keyCount -gt 0 ) {
        Write-Warning "$keyCount BitLocker recovery key file(s) on $drive. Move them into the password manager, then delete them from the USB.";
    }

    foreach( $name in 'autounattend.xml', 'settings.json', 'secrets.json' ) {
        Copy-Item -LiteralPath ( Join-Path -Path $stage -ChildPath $name ) -Destination $usbRoot -Force;
    }
    foreach( $folder in $kitFolders ) {
        $source = Join-Path -Path $stage -ChildPath $folder;
        if( -not ( Test-Path -LiteralPath $source ) ) {
            continue;
        }
        # /MIR so a script or driver deleted from the kit does not keep running from an old USB.
        robocopy.exe $source ( Join-Path -Path $usbRoot -ChildPath $folder ) /MIR /NFL /NDL /NJH /NJS /NP | Out-Null;
        # robocopy exit codes 0-7 mean success; 8 and above mean something failed.
        if( $LASTEXITCODE -ge 8 ) {
            throw "Copying $folder to $drive failed (robocopy exit code $LASTEXITCODE).";
        }
    }
    Write-Host "Copied the kit to $usbRoot";
}
