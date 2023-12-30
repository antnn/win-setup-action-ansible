FROM fedora:37

ENV ANSIBLE_LIBRARY='/project'\
    CONFIG_ISO='/project/config.iso' \
    VMDISK_DIR="/project" \
    VMDISK="vm.qcow2" \
    DISK_SIZE="30G" \
    RAM_SIZE=2048 \
    OS_ISO="/mnt/os.iso" \
    VNC_SOCKET_DIR="/tmp/vnc" \
    VNC_SOCKET="vnc"

COPY . "${ANSIBLE_LIBRARY}"
RUN set -Eeuo pipefail ; set -o nounset ; set -o errexit ;  \
    dnf install -y --setopt=install_weak_deps=False --best \
    qemu-system-x86-core qemu-img ansible python3-pip p7zip-plugins \
    python3-pycdlib python3-libvirt ; \ 
    echo Installing ansible-galaxy community.general ; \
    ansible-galaxy collection install community.general ; \
    echo Installing ansible-galaxy community.libvirt ; \
    ansible-galaxy collection install community.libvirt ; \
    ansible-playbook /project/example.yml --extra-vars iso_output_path="${CONFIG_ISO}" ; \
    rm -rf /tmp/* ; 

ENTRYPOINT  /bin/bash  "${ANSIBLE_LIBRARY}/entrypoint.sh"

