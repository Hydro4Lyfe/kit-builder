Hardware drivers, installed by Scripts\specialize\01-drivers.ps1 during setup.

  Drivers\_Common\          installed on every machine
  Drivers\<Model name>\     installed only on matching machines
      match.txt             one model name per line (see below)
      <extracted driver pack: folders of .inf/.sys/.cat files>

Adding a model
  1. On the target machine (or one like it), run in PowerShell:
       Get-CimInstance Win32_ComputerSystem | Select-Object Manufacturer, Model
       Get-CimInstance Win32_ComputerSystemProduct | Select-Object Version
  2. Create Drivers\<Manufacturer Model>\ and a match.txt in it containing the
     Model value. Lines may use * as a wildcard. For Lenovo, Model is a machine
     type like 21HDCTO1WW, so add 21HD* (covers every variant of that type)
     and/or the Version value (the friendly name, e.g. ThinkPad T14 Gen 4).
  3. Download the manufacturer's driver pack for that model (Dell "Driver Packs
     for Enterprise", HP "Driver Packs", Lenovo "SCCM driver packs") and extract
     it into the folder. Installers (.exe) must be extracted, not just copied.
  4. Run tools\Test-Kit.ps1, then rebuild the USB.

Driver packs are not stored in git (too large). Keep the master copy on the
file share; git only tracks each folder's match.txt.

Drivers that Windows Setup itself needs to see the disk (Intel RST/VMD storage)
go in $WinPEDriver$\ instead.
