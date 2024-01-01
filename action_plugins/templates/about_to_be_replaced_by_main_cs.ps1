$ErrorActionPreference = "Stop"
$CONFIGDRIVE = "{{config_drive}}"
$DoneList = "$env:SystemDrive\ansible-win-setup-done-list.log"
$ONE_INSTANCE_LOCKFILE_PATH = "$env:TEMP\mainps1.lock"

$MAIN_CLASS = "C:\Users\Virt\Desktop\auto.cs";
$code = [System.IO.File]::ReadAllText($MAIN_CLASS)
$scriptAssembly = "System.Web.Extensions, Version=3.5.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"
Add-Type -ReferencedAssemblies $scriptAssembly -TypeDefinition $code -Language CSharp


function OneInstance($path) {
    # It will start second time after first user login by autostart.
    try {
        return [System.IO.File]::Open($path, 'Create', 'Write')
    }
    catch {
        Write-Error "Only one instance of main.ps1 is allowed"
        exit
    }
}

function Main() {
    $ONE_INSTANCE_LOCKFILE = OneInstance($ONE_INSTANCE_LOCKFILE_PATH)
    
    $main_ps1_autostart = [PSCustomObject]@{
        name        = "start.ps1"
        sourceDir   = "$CONFIGDRIVE"
        autoStart   = $True
        interpreter = "powershell.exe -NoExit -ExecutionPolicy Bypass -File"
        destination = "$CONFIGDRIVE"
    }
    try {
        Get-ItemProperty -Path "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run" `
            -Name "start.ps1" > $null
    }
    catch {
        # NOTE: Win7 SP1 installation forces reboot disregarding "/norestart" option
        # https://social.technet.microsoft.com/Forums/ie/en-US/c4b7c3fc-037c-4e45-ab11-f6f64837521a/how-to-disable-reboot-after-sp1-installation-distribution-as-exe-via-sccm?forum=w7itproinstall
        # It should continue installing after reboot skiping installed packages
        _AutoStart($main_ps1_autostart)
    }

    $installJson = Get-Content "${CONFIGDRIVE}\install.json"
    # Pass JSON array [{....}] Deserialize<object[]>
    $installJson = [JSON]::Sort([JSON]::Deserialize($installJson))
    foreach ($item in $installJson) {
        $_done = IsDone($item)
        if (-Not $_done) {
            _dispatch($item)
            AppendToDoneList($item)
            if ($item.restart) {
                Restart-Computer -Force
                shutdown /r /t 0
                exit
            }
        }
    }
    
    Enable-WinRM

    CleanUp $ONE_INSTANCE_LOCKFILE $main_ps1_autostart
    Stop-Computer -Force
    shutdown /s /t 0
}

function CleanUp ($ONE_INSTANCE_LOCKFILE, $main_ps1_autostart) {
    net user administrator /active:no
    Remove-ItemProperty -Force -Path "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run" `
        -Name $main_ps1_autostart.name
    $ONE_INSTANCE_LOCKFILE.Close()
    Remove-Item -Path $ONE_INSTANCE_LOCKFILE.Name -Force
    $ONE_INSTANCE_LOCKFILE.Dispose()
    Remove-Item -Path $DoneList  -Force
}

function _dispatch($item) {
    if ($item.win_pass) {
        return #WinPE
    }
    if ($item.file) {
        _File($item.file)
        return
    }
    if ($item.zip) {
        _Zip($item.zip)
        return
    }
    if ($item.copy) {
        _Copy($item.copy)
        return
    }
    if ($item.cmd) {
        _RunCMD($item.cmd)
        return
    }
    if ($item.registry) {
        _Registry($item.registry)
        return
    }
    if ($item.addToPath) {
        _AddToPath($item.path)
        return
    }
}



$code = @"
using System;
using System.Collections.Generic;
using _JSON=System.Web.Script.Serialization.JavaScriptSerializer; // keep FullName, otherwise - undefined reference
using Dict = System.Collections.Generic.Dictionary<string, object>;
public class JSON
{
    public static object[] Deserialize(string data)
    {
        _JSON serializer = new _JSON();
        return serializer.Deserialize<object[]>(data);
    }
    public static string Serialize(object[] data)
    {
        _JSON serializer = new _JSON();
        return serializer.Serialize(data);
    }
    public static object[] Sort(object[] array)
    {
        IComparer<Object> jsonComparer = new JSONComparer();
        Array.Sort(array, jsonComparer);
        return array;
    }

}
public class JSONComparer : IComparer<object>
{
    public int Compare(object a, object b)
    {
        Int32 _a = (Int32)((Dict)a)["index"];
        Int32 _b = (Int32)((Dict)b)["index"];
        return (_a.CompareTo(_b));
    }
}
"@
#[System.Reflection.Assembly]::LoadWithPartialName("System.Web.Extensions").FullName
$scriptAssembly = "System.Web.Extensions, Version=3.5.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"
Add-Type -ReferencedAssemblies $scriptAssembly -TypeDefinition $code -Language CSharp



Function Wait-Process($name) {
    Do {
        Start-Sleep 2
        $instanceCount = (Get-Process | Where-Object { $_.Name -eq $name } | Measure-Object).Count
    } while ($instanceCount -gt 0)
}


Function _AddToPath ([string]$path) {
    $path = _ExpandString($path);
    $_path = [Environment]::GetEnvironmentVariable('PATH', 'Machine')
    [Environment]::SetEnvironmentVariable('PATH', "$_path;$path", 'Machine')
}


function Get-Ext($path) {
    $path.split(".")[-1];
}

function mkdir_-p($path) {
    if (Test-Path $path) {
        return
    }
    New-Item -Force -ItemType File -Path "$path\file"
    Remove-Item -Force -Path "$path\file"
}

Function _ExpandString($str) {
    return $ExecutionContext.InvokeCommand.ExpandString($str) # Interpolate strings in install.json: "$INSTALLDIR", "$env:..."
}

function _package($pkg) {
    $pkg.path = _ExpandString($pkg.path)
    $pkg.args = _ExpandString($pkg.args)
    $ext = Get-ext($pkg.path)
    if ( $ext -eq "msi") {
        _Msi($pkg)
        return
    }
    if ( $ext -eq "exe") {
        _Exe($pkg)
        return
    }
    if ( $ext -eq "msu") {
        _Wusa($pkg)
        return
    }
    if ( $ext -eq "cab") {
        _Dism($pkg)
        return
    }
}

Function _registry($item){
    if (! $item.state) {
        return;
    }
    $path = _ExpandString($item.path)
    $value = $item.value
    if ($item.state -eq "present") {
        New-Item -Force:$item.force  -Path $path -Value $value -ItemType $item.type
        return
    }
    if ($item.state -eq "property") {
        New-ItemProperty -Force:$item.force -Path $path -Value $value -ItemType $item.type
        return
    }
    if ($item.state -eq "absent") {
        Remove-Item -Recurse:$item.recurse -Path $path
        return
    }
}

Function _File($file) {
    if (! $file.state) {
        return;
    }
    $path = _ExpandString($file.path)
    if ($file.state -eq "directory") {
        Write-Host -ForegroundColor DarkGreen  "Creating a path: $path"
        if ($file.parents){
            mkdir_-p($path) 
        } else {
            New-Item -Force:$item.force -ItemType Directory -Path $path
        }
        return
    }
    if ($file.state -eq "touch") {
        New-Item -Force:$item.force -Path $path
        return
    }
    if ($file.state -eq "present") {
        New-Item -Force:$item.force  -Path $path -Value $file.$value
        return
    }
    if ($file.state -eq "absent") {
        Remove-Item -Recurse:$item.recurse -Path $path
        return
    }

}
Function _Copy($item) {
    if ($item.src -eq $item.dest) { return }
    $item.src = _ExpandString($item.src)
    $item.dest = _ExpandString($item.dem.srst)
    Write-Host -ForegroundColor DarkGreen  "Copying: " $itec " to " $item.dest
    Copy-Item -Force:$item.force -Path $item.src -Destination $item.dest
}
function _Exe($pkg) {
    Write-Host -ForegroundColor DarkGreen  "Running: " $pkg.path
    Start-Process $pkg.path -Wait -ArgumentList $pkg.args
}
function _Msi($pkg) {
    Write-Host -ForegroundColor DarkGreen  "Installing: " $pkg.path
    Start-Process msiexec.exe -Wait -ArgumentList  "/I $($pkg.path) $($pkg.args)"
}
function _Wusa($pkg) {
    Write-Host -ForegroundColor DarkGreen  "Installing updates (wusa): $($pkg.path)"
    Wait-Process -name wusa
    Start-Process wusa.exe -Wait -ArgumentList "$($pkg.path) $($pkg.args)"
    Wait-Process -name wusa
}
function _Dism($pkg) {
    Write-Host -ForegroundColor DarkGreen  "Installing updates (dism): $($pkg.path)"
    Wait-Process -name dism
    Start-Process dism.exe -Wait -ArgumentList "/Online /Add-Package /PackagePath: $($pkg.path) $($pkg.args)"
    Wait-Process -name dism
}

function _unzip([string]$path, [string]$dest) {
    $shell = New-Object -ComObject Shell.Application
    $zip_src = $shell.NameSpace($path)
    if (!$zip_src) {
        throw "Cannot find file: $path"
    }
    $zip_dest = $shell.NameSpace($dest)
    $zip_dest.CopyHere($zip_src.Items(), 1044)
}
function _Zip($zip) {
    $zip.path = _ExpandString($zip.path)
    $zip.dest = _ExpandString($zip.dest)
    if (-Not $zip.dest) {
        return
    }
    Write-Host -ForegroundColor DarkGreen  "Extracting: $($zip.path) to $($zip.dest)"
    _unzip -path $zip.path -dest $zip.dest
}
# use wisely
function _RunCMD($cmd) {
    $command = _ExpandString($cmd)
    cmd /C $command
}
function AppendToDoneList($item) {
    $item.index | Out-File -Append $DoneList
}

function _AutoStart($item) {
    $entry = $item.name
    $interpreter = $item.interpreter
    $_args = $item.args
    $dest = $item.destination
    $dest = _ExpandString($dest)
    $value = "cmd /C $interpreter `"${dest}\${entry}`" $_args"
    New-ItemProperty -Force -Path "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run" `
        -PropertyType String -Name $item.name -Value $value
}


function Enable-WinRM {
    [Program]::SetNetworksLocationToPrivate()
    <# $networkListManager = [Activator]::CreateInstance(`
            [Type]::GetTypeFromCLSID([Guid]"{DCB00C01-570F-4A9B-8D69-199FDBA5723B}"))
    $connections = $networkListManager.GetNetworkConnections()
    $connections | ForEach-Object {
        $_.GetNetwork().SetCategory(1)
    } #>
    # Enable-PSRemoting -Force Only works under Administrator account 
    # E.g: Start-Process powershell.exe -Credential $Credential 
    Enable-PSRemoting -Force
    winrm quickconfig -q

    winrm set winrm/config/client/auth '@{Basic="true"}'
    winrm set winrm/config/service/auth '@{Basic="true"}'
    winrm set winrm/config/service '@{AllowUnencrypted="true"}'
    winrm set winrm/config/winrs '@{MaxMemoryPerShellMB="2048"}'
    Restart-Service -Name WinRM
    netsh advfirewall firewall add rule name="WinRM-HTTP" dir=in `
        localport=5985 protocol=TCP action=allow

}


function Enable-RDP() {
    Write-Host -ForegroundColor DarkGreen  "Enabling Remote desktop"
    Set-ItemProperty -Path 'HKLM:\System\CurrentControlSet\Control\Terminal Server'-name "fDenyTSConnections" -Value 0
    Start-Process netsh -ArgumentList "advfirewall firewall set rule group=`"remote desktop`" new enable=yes"
}

##################################
# Entrypoint

function IsDone($item) {
    return [bool] ($_doneList -match $item.index)
}


function AppendToDoneList($item) {
    $item.index | Out-File -Append $DoneList
}
# Skip already installed packages
Write-Output $null >> $DoneList
$_doneList = Get-Content $DoneList
Main



