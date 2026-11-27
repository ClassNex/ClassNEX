"""Run a Windows review on a private desktop. Never switch the input desktop or attach to an existing window."""
import ctypes as ct
from ctypes import wintypes as w
import argparse
import msvcrt
import os
import pathlib
import shutil
import subprocess
import sys
import tempfile
import uuid


class Startup(ct.Structure):
    _fields_ = [("size", w.DWORD), ("reserved", w.LPWSTR), ("desktop", w.LPWSTR), ("title", w.LPWSTR),
                *[(n, w.DWORD) for n in ("x", "y", "cx", "cy", "cols", "rows", "attrs", "flags")],
                ("show", w.WORD), ("reservedSize", w.WORD), ("ptr", ct.c_void_p),
                ("stdin", w.HANDLE), ("stdout", w.HANDLE), ("stderr", w.HANDLE)]


class Process(ct.Structure):
    _fields_ = [("process", w.HANDLE), ("thread", w.HANDLE), ("pid", w.DWORD), ("tid", w.DWORD)]


user = ct.WinDLL('user32', use_last_error=True)
kernel = ct.WinDLL('kernel32', use_last_error=True)


def api(lib, name, result, *args):
    fn = getattr(lib, name); fn.restype = result; fn.argtypes = args
    return fn


def checked(result):
    if not result:
        raise ct.WinError(ct.get_last_error())
    return result


def run(command, timeout):
    name = 'ClassNEX-review-' + uuid.uuid4().hex
    executable = shutil.which(command[0])
    if not executable:
        raise FileNotFoundError(command[0])
    desktop = checked(api(user, 'CreateDesktopW', w.HANDLE, w.LPCWSTR, w.LPCWSTR, ct.c_void_p, w.DWORD, w.DWORD, ct.c_void_p)(name, None, None, 0, 0x1ff, None))
    close = api(kernel, 'CloseHandle', w.BOOL, w.HANDLE)
    job = None
    proc = Process()
    try:
        job = checked(api(kernel, 'CreateJobObjectW', w.HANDLE, ct.c_void_p, w.LPCWSTR)(None, None))
        with open(os.devnull, 'rb') as nul, tempfile.TemporaryFile() as log:
            info = Startup(); info.size = ct.sizeof(info); info.desktop = name; info.flags = 0x100
            info.stdin = msvcrt.get_osfhandle(nul.fileno()); info.stdout = info.stderr = msvcrt.get_osfhandle(log.fileno())
            for handle in (info.stdin, info.stdout): os.set_handle_inheritable(handle, True)
            argv = ct.create_unicode_buffer(subprocess.list2cmdline([executable, *command[1:]]))
            checked(api(kernel, 'CreateProcessW', w.BOOL, w.LPCWSTR, w.LPWSTR, ct.c_void_p, ct.c_void_p, w.BOOL, w.DWORD, ct.c_void_p, w.LPCWSTR, ct.POINTER(Startup), ct.POINTER(Process))(
                executable, argv, None, None, True, 0x08000004, None, str(pathlib.Path.cwd()), ct.byref(info), ct.byref(proc)))
            checked(api(kernel, 'AssignProcessToJobObject', w.BOOL, w.HANDLE, w.HANDLE)(job, proc.process))
            if api(kernel, 'ResumeThread', w.DWORD, w.HANDLE)(proc.thread) == 0xffffffff: raise ct.WinError(ct.get_last_error())
            result = api(kernel, 'WaitForSingleObject', w.DWORD, w.HANDLE, w.DWORD)(proc.process, timeout * 1000)
            if result != 0: raise TimeoutError('Private desktop did not finish (wait result %s)' % result)
            code = w.DWORD(); checked(api(kernel, 'GetExitCodeProcess', w.BOOL, w.HANDLE, ct.POINTER(w.DWORD))(proc.process, ct.byref(code)))
            log.seek(0); shutil.copyfileobj(log, sys.stdout.buffer); sys.stdout.buffer.flush()
            return code.value
    finally:
        if job:
            api(kernel, 'TerminateJobObject', w.BOOL, w.HANDLE, w.UINT)(job, 1); close(job)
        if proc.process:
            api(kernel, 'TerminateProcess', w.BOOL, w.HANDLE, w.UINT)(proc.process, 1); close(proc.thread); close(proc.process)
        api(user, 'CloseDesktop', w.BOOL, w.HANDLE)(desktop)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--timeout', type=int, default=240); parser.add_argument('command', nargs=argparse.REMAINDER)
    options = parser.parse_args(); command = options.command
    if command[:1] == ['--']: command = command[1:]
    if not command: parser.error('a review command is required')
    sys.exit(run(command, options.timeout))
