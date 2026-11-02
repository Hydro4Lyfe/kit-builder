Drivers Windows Setup needs before it can install, usually storage (Intel
RST/VMD) when Setup shows no disks.

Windows Setup looks for a folder with this exact name ($WinPEDriver$) at the
root of every drive. It loads the drivers into Setup and also adds them to the
installed Windows. No settings needed: extract the .inf/.sys/.cat files into
subfolders here.

Use this only for drivers Setup needs. Everything else goes in Drivers\, which
installs only what matches the machine.
