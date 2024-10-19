## Ansible action plugin
that automates the installation of prerequisites for Ansible to run on Windows and uses Ansible playbooks for configuration.</br>


Run </br>
```bash
ansible-playbook example-win7-x64.yml --extra-vars iso_output_path="/path/to/output_config.iso"
```
then attach it to vm with windows iso

### Windows Part
[in C#](https://github.com/antnn/win-setup-action-ansible/blob/main/action_plugins/templates/main.cs)

### Define the Playbook Hosts and Connection:
This playbook is designed to automate the setup and configuration of a Windows environment, including downloading necessary updates, installing applications, and creating a bootable ISO with all configurations and drivers pre-included.
The playbook is set to run on the local machine (127.0.0.1) with a local connection.</br>
 ##### Set Variables:</br>
 - pkg_dir: Directory name where packages will be stored: `toinstall`.</br>
 - drivers_dir: Directory name for drivers `$WinpeDriver$`.</br>
 
##### Tasks:</br>
- Create Temporary Work Directory:

- Create a temporary directory for building the ISO, storing the path in iso_temp_root_build_dir.</br>
- Create Temp ISO Root Directory:</br>
- Create a subdirectory named iso within the temporary directory.</br>
- Creating Base Configuration Files:</br>
-  Use win_setup to create base configuration files for Windows and Ansible, specifying admin and user credentials, and computer name.</br>
- Define various installation tasks for different updates and packages, each with an index and specific details (e.g., msu, exe, msi).</br>
- Create Package Directory:</br>
-   Create the directory toinstall within the iso directory.</br>
- Download Required Packages:</br>
- Download various Windows updates, OpenSSH, .NET framework, and other necessary files using ansible.builtin.get_url.</br>
- Download Virtio Drivers:</br>
- Download Virtio drivers ISO file.</br>
- Create Drivers Directory:</br>
- Create the drivers directory.</br>
- Extract Virtio Drivers:</br>
- Extract specific Virtio driver files from the downloaded ISO.</br>
- Create a Windows Config ISO File:</br>
- Create the final ISO image with Joliet support, including all required files and directories (drivers_dir, pkg_dir, start.ps1, main.cs, install.json, autounattend.xml). </br>


##### Key Elements:</br>
Variables:
- Used to simplify paths and maintain consistency.</br>
##### Modules:</br>
- ansible.builtin.tempfile: Create temporary files/directories.</br>
- ansible.builtin.file: Manage file and directory states.</br>
- win_setup: Specific to Windows setup tasks.</br>
- ansible.builtin.get_url: Download files from URLs.</br>
- community.general.iso_extract: Extract files from ISO images.</br>
- community.general.iso_create: Create ISO images.</br>


##### Notes:
- TLS 1.2 Update: </br>
- A note is included about TLS 1.2 and a specific update (kb3140245).</br>
##### Windows Configuration:</br>
- Admin and user credentials are hardcoded (should be changed after installation if necessary).</br>
##### Installation Tasks:
- Each installation task is defined with an index, description, type of installer (e.g., msu, exe, msi), and arguments.</br>

This playbook is designed to automate the setup and configuration of a Windows environment, including downloading necessary updates, installing applications, and creating a bootable ISO with all configurations and drivers pre-included.
