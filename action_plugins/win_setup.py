#!/usr/bin/python3
from __future__ import (absolute_import, division, print_function)
import os
__metaclass__ = type


from ansible.plugins.action import ActionBase
from ansible.errors import AnsibleError, AnsibleFileNotFound, AnsibleAction, AnsibleActionFail

drivers_dir = "$WinpeDriver$"
install_dir = "toinstall"


class ActionModule(ActionBase):
    _TEMPLATES_DIR = '%s/templates' % os.path.dirname(__file__)
    TRANSFERS_FILES = True
    _VALID_ARGS = frozenset(('dest', 'admin_name', 'admin_password',
                             'user_name', 'user_password', 'install', 'computer_name',))

    def run(self, tmp=None, task_vars=None):
        if task_vars is None:
            task_vars = dict()
        result = super(ActionModule, self).run(tmp, task_vars)
        del tmp  # tmp no longer has any effect

        dest = self._task.args.get('dest', None)
        install_arg = self._task.args.get('install', None)

        try:
            if dest is None:
                raise AnsibleAction(message="dest argument is not provided")
            if install_arg is None:
                raise AnsibleAction(message="install argument is not provided")
            
            _task_vars = dict()
            _task_vars['admin_name'] = self._task.args.get('admin_name', None)
            _task_vars['admin_password'] = self._task.args.get(
                'admin_password', None)
            _task_vars['user_name'] = self._task.args.get('user_name', None)
            _task_vars['user_password'] = self._task.args.get(
                'user_password', None)
            _task_vars['computer_name'] = self._task.args.get(
                'computer_name', None)

            for k, v in _task_vars.items():
                if v is None:
                    raise AnsibleAction(
                        message=("%s argument is not provided" % k))

            task_vars.update(_task_vars)

            res = self.___template(dest, "autounattend.xml", task_vars)
            res = self.___template(dest, "start.ps1", task_vars)

            res = self.___create_dir(
                dest=dest, dir=drivers_dir, task_vars=task_vars)
            res = self.___create_dir(
                dest=dest, dir=install_dir, task_vars=task_vars)
            _dest = "%s/install.json" % dest
            
            res = self.___copy(dest=_dest,
                               src=None, content=install_arg, task_vars=task_vars)
            _dest = "%s/main.ps1" % dest
            _src = "%s/main.ps1" % self._TEMPLATES_DIR
            res = self.___copy(dest=_dest, src=_src,
                               content=None, task_vars=task_vars)
            result.update(res)

        except any as e:
            result.update(e.result)
        return result

    def ___template(self, dest, filename, task_vars):
        template_args = dict()
        template_args['src'] = "%s/%s" % (self._TEMPLATES_DIR, filename)
        template_args['dest'] = "%s/%s" % (dest, filename)
        template_args['newline_sequence'] = "\r\n"

        template_task = self._task.copy()
        template_task.args = template_args

        _l = self._shared_loader_obj.action_loader
        template_action = _l.get('ansible.legacy.template',
                                 task=template_task,
                                 connection=self._connection,
                                 play_context=self._play_context,
                                 loader=self._loader,
                                 templar=self._templar,
                                 shared_loader_obj=self._shared_loader_obj)
        return template_action.run(task_vars=task_vars)

    def ___create_dir(self, dest, dir, task_vars):
        file_args = dict()
        file_args['dest'] = "%s/%s" % (dest, dir)
        file_args['state'] = "directory"
        return self._execute_module(module_name='ansible.legacy.file',
                                    module_args=file_args,
                                    task_vars=task_vars)

    def ___copy(self, src, dest, content, task_vars):
        copy_args = dict()
        copy_args['dest'] = dest
        if src is not None:
            copy_args['src'] = src
        else:
            copy_args['content'] = content

        copy_task = self._task.copy()
        copy_task.args = copy_args

        _l = self._shared_loader_obj.action_loader
        copy_action = _l.get('ansible.legacy.copy',
                             task=copy_task,
                             connection=self._connection,
                             play_context=self._play_context,
                             loader=self._loader,
                             templar=self._templar,
                             shared_loader_obj=self._shared_loader_obj)
        return copy_action.run(task_vars=task_vars)
