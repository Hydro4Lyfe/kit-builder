# PLUMBING - you should not need to edit this. Add scripts to Scripts\<phase>\ instead.
#
# Runs every script in C:\Windows\Setup\Scripts\Custom\<phase>, in file-name order.
# That folder is copied off the USB by the bootstrap during the specialize pass,
# so later phases keep working even after the USB stick has been pulled.
#
# The caller sets $Phase, then pipes this file through Invoke-Expression, so no
# execution policy is involved.

$_customRoot = Join-Path -Path 'C:\Windows\Setup\Scripts\Custom' -ChildPath $Phase;

if( -not ( Test-Path -LiteralPath $_customRoot ) ) {
	"*** No custom scripts for phase `"{0}`"." -f $Phase;
} else {
	$_customPrelude = 'C:\Windows\Setup\Scripts\Custom\_prelude.ps1';
	if( Test-Path -LiteralPath $_customPrelude ) {
		Get-Content -LiteralPath $_customPrelude -Raw | Invoke-Expression;
	}

	# Some of the customization scripts branch on this.
	$isSpecializePhase = $Phase -eq 'specialize';

	foreach( $_customScript in Get-ChildItem -LiteralPath $_customRoot -File | Sort-Object -Property 'Name' ) {
		if( $_customScript.Name.StartsWith( '_' ) ) {
			continue;
		}
		"*** Custom [{0}] will now run `"{1}`"." -f $Phase, $_customScript.Name;
		$_customStart = [datetime]::Now;
		try {
			switch( $_customScript.Extension ) {
				'.ps1' {
					# Child scope on purpose: a bare `return` in a customization script
					# must end that script only, not this whole loop. It also stops
					# variables and preference changes leaking between scripts.
					& {
						Get-Content -LiteralPath $_customScript.FullName -Raw | Invoke-Expression;
					};
				}
				'.reg' {
					reg.exe import $_customScript.FullName;
				}
				{ $_ -in '.cmd', '.bat' } {
					cmd.exe /c "`"$( $_customScript.FullName )`"";
				}
				'.exe' {
					Start-Process -FilePath $_customScript.FullName -Wait;
				}
				default {
					"*** Skipped `"{0}`": unsupported extension." -f $_customScript.Name;
				}
			}
		} catch {
			"*** ERROR in {0}: {1}" -f $_customScript.Name, $_.Exception.Message;
		}
		"*** Finished {0} after {1:0} ms." -f $_customScript.Name, [datetime]::Now.Subtract( $_customStart ).TotalMilliseconds;
		"`r`n";
	}
}
