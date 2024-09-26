#!/usr/bin/python3
from __future__ import absolute_import, division, print_function
import os

__metaclass__ = type

from ansible.plugins.action import ActionBase
from ansible.errors import (
    AnsibleError,
    AnsibleFileNotFound,
    AnsibleAction,
    AnsibleActionFail,
)


default_entry_point = "start.ps1"
default_main_code_file = "main.cs"
default_first_logon_cmd = "start powershell.exe -NoExit -ExecutionPolicy Bypass -File"
# Search for start.ps1
default_first_logon_cmd = (
    "cmd.exe /C for %%D in (A B C D E F G H I J K L M N O P Q R S T U V W X Y Z) do @(if exist %%D:\\%s ( %s %%D:\\%s & goto :break) else (echo Not found)) & :break"
    % (default_entry_point, default_first_logon_cmd, default_entry_point)
)
default_install_json_path = "install.json"


def image_name_xml_code(name):
    return f"""<InstallFrom>
            <MetaData wcm:action="add">
                <Key>/IMAGE/NAME</Key>
                <Value>{name}</Value>
            </MetaData>
        </InstallFrom>"""


# TODO refactor to utilize ansible template functions
def static_ip_xml_code(task, task_vars):
    static_ip_params = {
        "interface_identifier": "network_interface",
        "ip_address": "static_ip_address_cidr",
        "routes_prefix": "static_route_cidr",
        "next_hop_address": "static_gateway_ip",
        "dns_server_address": "static_dns_server",
        "secondary_dns_server": "secondary_dns_server"
    }

    for param, arg_name in static_ip_params.items():
        task_vars[param] = task._task.args.get(arg_name, None)

    # Check if any parameter is provided and if all are present
    if any(task_vars[param] is not None for param in static_ip_params):
        missing_params = [param for param, value in task_vars.items() if value is None]
        if missing_params:
            raise AnsibleAction(
                message="When configuring static IP, all related parameters must be provided. "
                f"Missing parameters: {', '.join(missing_params)}"
            )
    return f"""<component name="Microsoft-Windows-TCPIP" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
                <Interfaces>
                    <Interface wcm:action="add">
                        <Ipv4Settings>
                            <DhcpEnabled>false</DhcpEnabled>
                        </Ipv4Settings>
                        <Identifier>{task_vars['interface_identifier']}</Identifier>
                        <UnicastIpAddresses>
                            <IpAddress wcm:action="add" wcm:keyValue="1">{task_vars['ip_address']}</IpAddress>
                        </UnicastIpAddresses>
                        <Routes>
                            <Route wcm:action="add">
                                <Identifier>0</Identifier>
                                <Prefix>{task_vars['routes_prefix']}</Prefix>
                                <NextHopAddress>{task_vars['next_hop_address']}</NextHopAddress>
                            </Route>
                        </Routes>
                    </Interface>
                </Interfaces>
            </component>
            <component name="Microsoft-Windows-DNS-Client" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
                <Interfaces>
                    <Interface wcm:action="add">
                        <Identifier>{task_vars['interface_identifier']}</Identifier>
                        <DNSServerSearchOrder>
                            <IpAddress wcm:action="add" wcm:keyValue="1">{task_vars['dns_server_address']}</IpAddress>
                            <IpAddress wcm:action="add" wcm:keyValue="2">{task_vars['secondary_dns_server']}</IpAddress>
                        </DNSServerSearchOrder>
                    </Interface>
                </Interfaces>
            </component>"""


class ActionModule(ActionBase):
    _TEMPLATES_DIR = "%s/templates" % os.path.dirname(__file__)
    TRANSFERS_FILES = True
    _VALID_ARGS = frozenset(
        (
            "dest",
            "image_name",
            "admin_name",
            "admin_password",
            "user_name",
            "user_password",
            "install",
            "computer_name",
            "first_logon_cmd",
            "network_interface",
            "static_ip_address_cidr",
            "static_route_cidr",
            "static_gateway_ip",
            "static_dns_server",
            "static_secondary_dns_server"
        )
    )

    def run(self, tmp=None, task_vars=None):
        if task_vars is None:
            task_vars = dict()
        result = super(ActionModule, self).run(tmp, task_vars)
        del tmp  # tmp no longer has any effect

        dest = self._task.args.get("dest", None)
        install_arg = self._task.args.get("install", None)

        try:
            if dest is None:
                raise AnsibleAction(message="dest argument is not provided")
            if install_arg is None:
                raise AnsibleAction(message="install argument is not provided")

            _task_vars = dict()
            _task_vars["from_image_xml_code"] = image_name_xml_code(
                self._task.args.get("image_name", None)
            )
            _task_vars["admin_name"] = self._task.args.get("admin_name", None)
            _task_vars["admin_password"] = self._task.args.get("admin_password", None)
            _task_vars["user_name"] = self._task.args.get("user_name", None)
            _task_vars["user_password"] = self._task.args.get("user_password", None)
            _task_vars["computer_name"] = self._task.args.get("computer_name", None)
            # _task_vars['config_drive'] = self._task.args.get('config_drive', default_config_drive)
            _task_vars["first_logon_cmd"] = self._task.args.get(
                "first_logon_cmd", default_first_logon_cmd
            )

            _task_vars["static_ip_xml_code"] = static_ip_xml_code(self)

            _task_vars["entry_point"] = default_entry_point
            _task_vars["main_code"] = default_main_code_file
            _task_vars["install_json"] = default_install_json_path

            for k, v in _task_vars.items():
                if v is None:
                    raise AnsibleAction(message=("%s argument is not provided" % k))

            task_vars.update(_task_vars)
            # copy templates to destination rendered
            res = self.___template(dest, "autounattend.xml", task_vars)
            res = self.___template(dest, "start.ps1", task_vars)
            res = self.___template(dest, "main.cs", task_vars)

            _dest = "%s/install.json" % dest
            res = self.___copy(
                dest=_dest,  # output install from yml to json
                src=None,
                content=install_arg,
                task_vars=task_vars,
            )

            result.update(res)

        except any as e:
            result.update(e.result)
        return result

    def ___template(self, dest, filename, task_vars):
        template_args = dict()
        template_args["src"] = "%s/%s" % (self._TEMPLATES_DIR, filename)
        template_args["dest"] = "%s/%s" % (dest, filename)
        template_args["newline_sequence"] = "\r\n"

        template_task = self._task.copy()
        template_task.args = template_args

        _l = self._shared_loader_obj.action_loader
        template_action = _l.get(
            "ansible.legacy.template",
            task=template_task,
            connection=self._connection,
            play_context=self._play_context,
            loader=self._loader,
            templar=self._templar,
            shared_loader_obj=self._shared_loader_obj,
        )
        return template_action.run(task_vars=task_vars)

    def ___copy(self, src, dest, content, task_vars):
        copy_args = dict()
        copy_args["dest"] = dest
        if src is not None:
            copy_args["src"] = src
        else:
            copy_args["content"] = content

        copy_task = self._task.copy()
        copy_task.args = copy_args

        _l = self._shared_loader_obj.action_loader
        copy_action = _l.get(
            "ansible.legacy.copy",
            task=copy_task,
            connection=self._connection,
            play_context=self._play_context,
            loader=self._loader,
            templar=self._templar,
            shared_loader_obj=self._shared_loader_obj,
        )
        return copy_action.run(task_vars=task_vars)
