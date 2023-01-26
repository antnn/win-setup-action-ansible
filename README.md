# win-setup-action
Антон Валишин
Пример для вакансии junior DevOps
### Requirements
```
pip3 install pycdlib
ansible-galaxy collection install community.general
ansible-galaxy collection install community.libvirt
any_package_manager p7zip-plugins ansible qemu
```
```bash
ANSIBLE_LIBRARY=./ ansible-playbook example.yml \
--extra-vars "iso_output_path='/path/to/config.iso'"
```
### Запуск в qemu, нужен iso файл ОС
```bash
sudo mkdir /tmp/vnc/ && sudo chown $USER /tmp/vnc

qemu-img create -f qcow2 -o compression_type=zstd vm.qcow2 30G

qemu-system-x86_64  -enable-kvm -smp $(nproc) -cpu host -pidfile /tmp/guest.pid \
-cdrom '/iso/7600.16385.090713-1255_x86fre_enterprise_en-us_EVAL_Eval_Enterprise-GRMCENEVAL_EN_DVD.iso' \
-drive file=./vm.qcow2,if=virtio \
-net nic,model=virtio-net-pci \
-net user,hostfwd=tcp::3389-:3389,hostfwd=tcp::5985-:5985 \
-m 2048 -usb -device usb-ehci,id=ehci -device usb-tablet \
-drive file='/path/to/config.iso',index=1,media=cdrom \
-vnc unix:/tmp/vnc/vnc # можно воспользоватся gui от qemu
 
vncviewer /tmp/vnc/vnc
```
