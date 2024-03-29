$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Definition
$driveLetter = $scriptPath.Substring(0, 2)


$installJson = "$driveLetter\{{install_json}}"
$startupPath = "$driveLetter\{{entry_point}}";
$MainCodeFile = "$driveLetter\{{main_code}}";
$adminUserName = "{{admin_name}}"
$adminPassword = "{{admin_password}}"
function Start-App() {
    if (-not (Test-Administrator)) {
        Start-ElevatedProcess
        exit
    }
    Import-DotNetAssembly
    [WinImageBuilderAutomation]::EnableAdministratorAccount($adminUserName)
    [WinImageBuilderAutomation]::AddToAutoStart($startupPath)
    [WinImageBuilderAutomation]::Main2( $installJson, $driveLetter)
    if (-not (Test-RemoteManagementEnabled)) {
        Enable-RemoteManagement
    }
    exit
}

function Start-ElevatedProcess() {
    $PWord = ConvertTo-SecureString -String $adminPassword -AsPlainText -Force
    $adminCredential = New-Object -TypeName System.Management.Automation.PSCredential `
        -ArgumentList $adminUserName, $PWord
    Start-Process powershell.exe -Credential $adminCredential `
        -ArgumentList "-NoExit -ExecutionPolicy Bypass $startupPath"

}

function Import-DotNetAssembly() {
    $sourceCode = [System.IO.File]::ReadAllText($MainCodeFile)
    $scriptAssembly = Get-NamesOfAssembliesToLoad @("System.Web.Extensions", 
            "System.Management")
    $osVersion = [System.Environment]::OSVersion
    if ($osVersion.Version.Major -eq 6 -and $osVersion.Version.Minor -eq 1) {
        $language = "CSharpVersion3"
    } else {
        $language = "CSharp"
    }
    Add-Type -ReferencedAssemblies $scriptAssembly -TypeDefinition $sourceCode -Language $language -IgnoreWarnings
}

function Get-NamesOfAssembliesToLoad {
    param(
        [string[]] $PartialNames
    )
    $fullNames = @()
    foreach ($name in $PartialNames) {
        $result = [System.Reflection.Assembly]::LoadWithPartialName($name)
        if ($result) {
            $fullNames += $result.FullName
        }
    }
    return $fullNames
}

function Test-Administrator {
    $currentUser = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $currentUser.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
function Test-RemoteManagementEnabled {
    return (Get-Service WinRM).Status -eq "Running"
}

function Enable-RemoteManagement {
    [WinImageBuilderAutomation]::SetNetworksLocationToPrivate()
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


try {
    Start-App
} catch  {
    # Log the error to the error log file
    $errorMessage = $_.Exception.Message
    $fullErrorMessage = $_.Exception.ToString()
    $errorTime = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logEntry = "$errorTime - Error: $errorMessage, $fullErrorMessage"
    Add-Content -Path "C:\ansible-action-setup.log" -Value $logEntry
    exit 1
}
