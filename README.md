# win-setup-action
Антон Валишин
Пример для вакансии junior DevOps

### Запуск в qemu, нужен iso файл ОС
```bash
# alias docker=podman or etc
git clone https://github.com/antnn/win-setup-action-ansible
cd "win-setup-action-ansible/"

CONTAINER_IMAGE_NAME="awesome_container_name"
docker build . -t "${CONTAINER_IMAGE_NAME}"

OS_ISO_SOURCE='/path/to/iso/7600.16385.090713-1255_x86fre_enterprise_en-us_EVAL_Eval_Enterprise-GRMCENEVAL_EN_DVD.iso'
VMDISK_DIR="/path/to/exported/disk/dir"
VMDISK="vm.qcow2" 
DISK_SIZE="30G" 
RAM_SIZE=2048 
OS_ISO="/mnt/os.iso" 
VNC_SOCKET_DIR="/tmp/vnc"
VNC_SOCKET="vnc"

sudo mkdir -p "${VNC_SOCKET_DIR}" && sudo chown $USER "${VNC_SOCKET_DIR}"
sudo chcon -t container_file_t  -R  "${VNC_SOCKET_DIR}" # SELinux to help access from container

# Please note: `z` mount option (SELinux)
docker run --rm -it --device=/dev/kvm  \
    -e "VMDISK=$VMDISK" -e "VMDISK_DIR=$VMDISK_DIR" -e "DISK_SIZE=$DISK_SIZE" \
    -e "RAM_SIZE=$RAM_SIZE" -e "OS_ISO=$OS_ISO"  -e "VNC_SOCKET_DIR=$VNC_SOCKET_DIR" \
    -e "VNC_SOCKET=$VNC_SOCKET" \
    --mount=type=bind,source="${VMDISK_DIR}",target="${VMDISK_DIR}",z \
    --mount=type=bind,target="${VNC_SOCKET_DIR}",z \
    --mount=type=bind,source="${OS_ISO_SOURCE}",target="${OS_ISO}",z "${CONTAINER_IMAGE_NAME}"
    
# In abother terminal
vncviewer /tmp/vnc/vnc 
```

### [Демо YouTube](https://www.youtube.com/watch?v=CyjWnvdco3U)
