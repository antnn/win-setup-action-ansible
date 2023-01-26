# Note: autounattend.xml also contains setup commands
# Activate admin account to run across reboots with admin rights
net user administrator /active:yes
$User = "{{admin_name}}" 
$PWord = ConvertTo-SecureString -String "{{admin_password}}" -AsPlainText -Force
$Credential = New-Object -TypeName System.Management.Automation.PSCredential `
    -ArgumentList $User, $PWord
Start-Process powershell.exe -Credential $Credential `
    -ArgumentList "-ExecutionPolicy Bypass D:\main.ps1"
