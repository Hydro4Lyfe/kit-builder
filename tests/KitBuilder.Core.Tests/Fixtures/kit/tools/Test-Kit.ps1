<#
.SYNOPSIS
    Checks the deployment kit for common mistakes before a build.

.DESCRIPTION
    Run it on its own, or let Build-Kit.ps1 run it. Prints every problem it finds
    and exits with code 1 if any of them is an error. Warnings do not stop a build.

.PARAMETER Format
    Text (default) prints the problems for a person. Json prints only
    { "errors": [...], "warnings": [...] }, for PT Kit Builder.

.EXAMPLE
    .\tools\Test-Kit.ps1
#>
[CmdletBinding()]
param(
    [string] $KitRoot = ( Split-Path -Parent $PSScriptRoot ),
    [ValidateSet( 'Text', 'Json' )] [string] $Format = 'Text'
);

# Bump together with PT Kit Builder when the kit layout changes incompatibly.
$supportedSchemaVersion = 1;

$KitRoot = ( Resolve-Path -LiteralPath $KitRoot ).ProviderPath;
$errors = [System.Collections.Generic.List[string]]::new();
$warnings = [System.Collections.Generic.List[string]]::new();

function Add-Problem {
    param(
        [ValidateSet( 'Error', 'Warning' )] [string] $Level,
        [string] $Message
    );
    if( $Level -eq 'Error' ) { $errors.Add( $Message ); } else { $warnings.Add( $Message ); }
}

# Kit-relative path for messages.
function Get-RelativePath {
    param( [string] $Path );
    $Path.Substring( $KitRoot.Length ).TrimStart( '\', '/' ) -replace '\\', '/';
}

function Read-Json {
    param( [string] $Name );
    $path = Join-Path -Path $KitRoot -ChildPath $Name;
    if( -not ( Test-Path -LiteralPath $path ) ) {
        return $null;
    }
    try {
        return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json;
    } catch {
        Add-Problem Error "$Name is not valid JSON: $( $_.Exception.Message )";
        return $null;
    }
}

# Forward slashes work on Windows too, and let this run under pwsh on Linux.
$phases = @( 'specialize', 'defaultuser', 'firstlogon', 'useronce' );
$runnable = @( '.ps1', '.reg', '.cmd', '.bat', '.exe' );

# --------------------------------------------------------------------------
# Layout
# --------------------------------------------------------------------------

foreach( $required in @(
    'autounattend.template.xml';
    'settings.json';
    'Scripts/_scripts.marker';
    'Scripts/_prelude.ps1';
    'Scripts/_system/Invoke-Phase.ps1';
) ) {
    if( -not ( Test-Path -LiteralPath ( Join-Path -Path $KitRoot -ChildPath $required ) ) ) {
        Add-Problem Error "Missing $required.";
    }
}

# --------------------------------------------------------------------------
# settings.json and secrets.json
# --------------------------------------------------------------------------

$settings = Read-Json 'settings.json';
if( $settings ) {
    if( $null -ne $settings.schemaVersion -and [int] $settings.schemaVersion -gt $supportedSchemaVersion ) {
        Add-Problem Error "settings.json: schemaVersion $( $settings.schemaVersion ) is newer than these tools support ($supportedSchemaVersion). Update the kit's tools folder.";
    }
    foreach( $name in @( 'language.uiLanguage', 'language.inputLocale', 'language.systemLocale', 'language.userLocale', 'edition', 'timeZone' ) ) {
        $value = $settings;
        foreach( $part in $name.Split( '.' ) ) {
            $value = if( $value ) { $value.$part; } else { $null; };
        }
        if( [string]::IsNullOrWhiteSpace( [string] $value ) ) {
            Add-Problem Error "settings.json: '$name' is missing or empty.";
        }
    }
    if( $settings.wifi -and $settings.wifi.enabled -and -not $settings.wifi.ssid ) {
        Add-Problem Error "settings.json: wifi is enabled but 'wifi.ssid' is empty.";
    }
    foreach( $name in @( 'startPins', 'removeApps', 'removeCapabilities', 'removeFeatures' ) ) {
        if( $null -ne $settings.$name -and $settings.$name -isnot [array] ) {
            Add-Problem Error "settings.json: '$name' must be a list in square brackets.";
        }
    }
    # firstlogon\50-windows-hello-face.ps1 installs this; removing it in specialize
    # first just forces a re-download (and fails without network).
    if( @( $settings.removeCapabilities ) -like 'Hello.Face*' ) {
        Add-Problem Error "settings.json: removeCapabilities lists Hello.Face, but firstlogon/50-windows-hello-face.ps1 installs it. Keep one or the other.";
    }
}

$secretsPath = Join-Path -Path $KitRoot -ChildPath 'secrets.json';
if( -not ( Test-Path -LiteralPath $secretsPath ) ) {
    Add-Problem Error 'Missing secrets.json. Copy secrets.example.json to secrets.json and fill in the passwords.';
} else {
    $secrets = Read-Json 'secrets.json';
    if( $secrets ) {
        if( -not $secrets.adminPassword ) {
            Add-Problem Error "secrets.json: 'adminPassword' is empty.";
        }
        if( $settings -and $settings.wifi -and $settings.wifi.enabled -and -not $secrets.wifiPassword ) {
            Add-Problem Error "secrets.json: 'wifiPassword' is empty but wifi is enabled in settings.json.";
        }
    }
}

# --------------------------------------------------------------------------
# Scripts
# --------------------------------------------------------------------------

$scriptsRoot = Join-Path -Path $KitRoot -ChildPath 'Scripts';
$allFiles = @( Get-ChildItem -LiteralPath $scriptsRoot -Recurse -File -ErrorAction 'SilentlyContinue' |
    Where-Object -FilterScript { $_.Name -notlike '*:Zone.Identifier' } );

foreach( $file in $allFiles ) {
    $rel = Get-RelativePath $file.FullName;
    $phase = Split-Path -Leaf $file.DirectoryName;
    $inPhase = $phase -in $phases;
    $willRun = $inPhase -and -not $file.Name.StartsWith( '_' );

    if( $willRun -and $file.Extension -notin $runnable ) {
        Add-Problem Error "${rel}: unsupported file type, it will be skipped at install. Prefix it with _ if another script uses it.";
        continue;
    }
    if( $willRun -and $file.Name -notmatch '^\d+-' ) {
        Add-Problem Warning "${rel}: no number prefix (e.g. 60-), so it runs after all numbered scripts.";
    }

    if( $file.Extension -in '.ps1', '.reg' ) {
        # Windows PowerShell 5.1 reads BOM-less files as ANSI, and the bootstrap does
        # the same when converting .reg files, so non-ASCII text needs a UTF-8 BOM.
        $bytes = [System.IO.File]::ReadAllBytes( $file.FullName );
        $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF;
        if( -not $hasBom -and @( $bytes -gt 127 ).Count -gt 0 ) {
            Add-Problem Error "${rel}: contains non-ASCII characters but has no UTF-8 BOM. Re-save it as 'UTF-8 with BOM'.";
        }
    }

    if( $file.Extension -eq '.ps1' ) {
        $tokens = $null;
        $parseErrors = $null;
        $ast = [System.Management.Automation.Language.Parser]::ParseFile( $file.FullName, [ref] $tokens, [ref] $parseErrors );
        foreach( $parseError in $parseErrors ) {
            Add-Problem Error "${rel}:$( $parseError.Extent.StartLineNumber ): $( $parseError.Message )";
        }
        if( $willRun ) {
            $exits = $ast.FindAll( { param( $node ) $node -is [System.Management.Automation.Language.ExitStatementAst]; }, $true );
            foreach( $exit in $exits ) {
                Add-Problem Error "${rel}:$( $exit.Extent.StartLineNumber ): 'exit' aborts the whole phase. Use 'return', or the wrapper pattern in Scripts/README.md.";
            }
        }
    }

    if( $file.Extension -eq '.reg' -and $inPhase ) {
        $lines = @( Get-Content -LiteralPath $file.FullName );
        $first = $lines | Where-Object -FilterScript { $_.Trim() } | Select-Object -First 1;
        if( $first -ne 'Windows Registry Editor Version 5.00' ) {
            Add-Problem Error "${rel}: first line must be 'Windows Registry Editor Version 5.00'.";
        }
        $keys = $lines | Where-Object -FilterScript { $_ -match '^\[-?HKEY_' };
        foreach( $key in $keys ) {
            $isDefaultUser = $key -match '^\[-?HKEY_USERS\\DefaultUser\\';
            if( $phase -eq 'defaultuser' -and -not $isDefaultUser ) {
                Add-Problem Error "${rel}: keys in defaultuser/ must start with HKEY_USERS\DefaultUser\ - found $key";
            } elseif( $phase -ne 'defaultuser' -and $isDefaultUser ) {
                Add-Problem Error "${rel}: HKEY_USERS\DefaultUser only exists during the defaultuser phase - found $key";
            }
        }
    }
}

# --------------------------------------------------------------------------
# Drivers
# --------------------------------------------------------------------------

$driverRoot = Join-Path -Path $KitRoot -ChildPath 'Drivers';
foreach( $dir in Get-ChildItem -LiteralPath $driverRoot -Directory -ErrorAction 'SilentlyContinue' ) {
    $rel = Get-RelativePath $dir.FullName;
    $hasInf = [bool] ( Get-ChildItem -LiteralPath $dir.FullName -Filter '*.inf' -Recurse -File | Select-Object -First 1 );
    if( $dir.Name -ne '_Common' ) {
        $matchFile = Join-Path -Path $dir.FullName -ChildPath 'match.txt';
        $patterns = if( Test-Path -LiteralPath $matchFile ) {
            @( Get-Content -LiteralPath $matchFile | Where-Object -FilterScript { $_.Trim() } );
        } else {
            @();
        };
        if( $patterns.Count -eq 0 ) {
            Add-Problem Error "${rel}: needs a match.txt with at least one model name, or it is never installed.";
        }
    }
    $hasContent = [bool] ( Get-ChildItem -LiteralPath $dir.FullName -Recurse -File |
        Where-Object -FilterScript { $_.Name -notin 'match.txt', 'README.txt' } | Select-Object -First 1 );
    if( ( $hasContent -or $dir.Name -ne '_Common' ) -and -not $hasInf ) {
        Add-Problem Warning "${rel}: no .inf files. Extract the driver pack (.exe/.cab/.zip) into this folder.";
    }
}

# --------------------------------------------------------------------------
# Assets that scripts expect
# --------------------------------------------------------------------------

if( -not ( Test-Path -LiteralPath ( Join-Path -Path $KitRoot -ChildPath 'CustomAssets/admin.png' ) ) ) {
    Add-Problem Warning 'CustomAssets/admin.png is missing; the Administrator keeps the default picture.';
}
if( -not ( Test-Path -LiteralPath ( Join-Path -Path $scriptsRoot -ChildPath 'useronce/_WorldNationalParks.deskthemepack' ) ) ) {
    Add-Problem Warning 'Scripts/useronce/_WorldNationalParks.deskthemepack is missing; machines will not be themed. See Scripts/README.md.';
}

# --------------------------------------------------------------------------
# Report
# --------------------------------------------------------------------------

if( $Format -eq 'Json' ) {
    # Plain stdout only, so the caller can parse it.
    [ordered] @{ errors = @( $errors ); warnings = @( $warnings ); } | ConvertTo-Json -Depth 3;
} else {
    foreach( $warning in $warnings ) {
        Write-Warning $warning;
    }
    foreach( $problem in $errors ) {
        Write-Host "ERROR: $problem" -ForegroundColor 'Red';
    }
    Write-Host ( 'Test-Kit: {0} error(s), {1} warning(s).' -f $errors.Count, $warnings.Count );
}
if( $errors.Count -gt 0 ) {
    exit 1;
}
exit 0;
