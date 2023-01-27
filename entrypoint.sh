#!/bin/bash
DISKPATH="${VMDISK_DIR}/${VMDISK}"
if [ ! -f "${DISKPATH}" ]; then
    qemu-img create -f qcow2 -o compression_type=zstd  "${DISKPATH}"  "${DISK_SIZE}" 
fi
qemu-system-x86_64  -enable-kvm -smp $(nproc) -cpu host -pidfile /tmp/guest.pid \
    -drive file="${DISKPATH}",if=virtio   \
    -net nic,model=virtio-net-pci \
    -net user,hostfwd=tcp::3389-:3389,hostfwd=tcp::5985-:5985 \
    -m ${RAM_SIZE} -usb -device usb-ehci,id=ehci -device usb-tablet   \
    -cdrom "${OS_ISO}"   \
    -drive file="${CONFIG_ISO}",index=1,media=cdrom \
    -vnc unix:"${VNC_SOCKET_DIR}/${VNC_SOCKET}"