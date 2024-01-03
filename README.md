# win-setup-action

The provided code is a bash script that sets up a Docker container for running Ansible to configure a Windows system. Let's break down the code and explain each step.

Cloning the Repository

The script starts by cloning a GitHub repository called "win-setup-action-ansible" using the git clone command. Here's the command:

git clone https://github.com/antnn/win-setup-action-ansible


Building the Docker Image

Next, the script sets a variable CONTAINER_IMAGE_NAME and builds a Docker image using the docker build command. The image is built from the current directory (.) and tagged with the value of the CONTAINER_IMAGE_NAME variable. Here's the command:

CONTAINER_IMAGE_NAME="awesome_container_name"
docker build . -t "${CONTAINER_IMAGE_NAME}"


Setting Environment Variables

The script sets several environment variables that will be used during the Docker container run. These variables define paths, disk sizes, RAM size, and VNC settings. Here are the environment variables being set:

export OS_ISO_SOURCE='/path/to/iso/7600.16385.090713-1255_x86fre_enterprise_en-us_EVAL_Eval_Enterprise-GRMCENEVAL_EN_DVD.iso'
export VMDISK_DIR="/path/to/exported/disk/dir"
export VMDISK="vm.qcow2"
export DISK_SIZE="30G"
export RAM_SIZE=2048
export OS_ISO="/mnt/os.iso"
export VNC_SOCKET_DIR="/tmp/vnc"
export VNC_SOCKET="vnc"


SELinux Configuration

The script then creates a directory for the VNC socket, changes ownership to the current user, and sets the SELinux context to allow access from the container. Here's the command:

sudo bash -c "mkdir -p \"${VNC_SOCKET_DIR}\" ;  \
			chown $USER \"${VNC_SOCKET_DIR}\"  ;  \
			chcon -t container_file_t  -R  \"${VNC_SOCKET_DIR}\" "


Running the Docker Container

Finally, the script runs the Docker container using the docker run command. The container is run with the --rm flag to automatically remove it after it exits, the --device=/dev/kvm flag to enable KVM device access, and various environment variables passed using the -e flag. The container is also mounted with several bind mounts using the --mount flag to provide access to the VM disk, VNC socket directory, and OS ISO file. Here's the command:

docker run --rm -it --device=/dev/kvm  \
    -e "VMDISK=$VMDISK" -e "VMDISK_DIR=$VMDISK_DIR" -e "DISK_SIZE=$DISK_SIZE" \
    -e "RAM_SIZE=$RAM_SIZE" -e "OS_ISO=$OS_ISO"  -e "VNC_SOCKET_DIR=$VNC_SOCKET_DIR" \
    -e "VNC_SOCKET=$VNC_SOCKET" \
    --mount=type=bind,source="${VMDISK_DIR}",target="${VMDISK_DIR}",z \
    --mount=type=bind,target="${VNC_SOCKET_DIR}",z \
    --mount=type=bind,source="${OS_ISO_SOURCE}",target="${OS_ISO}",z "${CONTAINER_IMAGE_NAME}"


Opening VNC Viewer

After running the Docker container, the script suggests opening a VNC viewer in another terminal to connect to the VNC socket. Here's the command:

vncviewer /tmp/vnc/vnc


Please note that the script assumes you have the necessary dependencies installed and have the correct paths and filenames for the ISO, VM disk, and VNC socket directory.

I hope this explanation helps! Let me know if you have any further questions.
