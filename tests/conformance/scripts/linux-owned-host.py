"""Own and reap a Linux test host's descendants, including orphaned children."""

import ctypes
import errno
import os
from pathlib import Path
import signal
import subprocess
import sys
import time


stopping = False


def stop(_signal, _frame):
    global stopping
    stopping = True


signal.signal(signal.SIGTERM, stop)
signal.signal(signal.SIGINT, stop)
libc = ctypes.CDLL(None, use_errno=True)
# Adopt orphaned descendants before launching any host code. PID 1 need not reap.
if libc.prctl(36, 1, 0, 0, 0) != 0:  # PR_SET_CHILD_SUBREAPER
    raise OSError(ctypes.get_errno(), "Cannot own test-host descendants")

try:
    host = subprocess.Popen(sys.argv[1:], start_new_session=True)
except OSError as error:
    print("NETWIV_HOST_SPAWN_ERROR " + errno.errorcode.get(error.errno, "spawn failed"), flush=True)
    sys.exit(127)

try:
    # Keep the exited host waitable until its process group has been signalled,
    # so its PID cannot be reused for an unrelated process group.
    while not stopping and os.waitid(os.P_PID, host.pid, os.WEXITED | os.WNOHANG | os.WNOWAIT) is None:
        time.sleep(0.01)
finally:
    try:
        os.killpg(host.pid, signal.SIGKILL)
    except ProcessLookupError:
        pass
    code = host.wait()
    # Reap until ECHILD, independently of PID 1 and the optional procfs
    # task/children entry (which requires CONFIG_CHECKPOINT_RESTORE).
    while True:
        try:
            reaped, _ = os.waitpid(-1, os.WNOHANG)
        except ChildProcessError:
            break
        if reaped:
            continue
        # A live adopted child may have created a separate session. Standard
        # procfs stat exposes PPID even when task/children is unavailable. Only
        # signal our children; their PIDs cannot be reused until we reap them.
        for entry in Path('/proc').iterdir():
            if not entry.name.isdecimal():
                continue
            try:
                fields = (entry / 'stat').read_bytes().rpartition(b')')[2].split()
                if int(fields[1]) == os.getpid():
                    os.kill(int(entry.name), signal.SIGKILL)
            except (FileNotFoundError, ProcessLookupError, PermissionError):
                # Unrelated processes can disappear or be hidden by procfs.
                pass
        time.sleep(0.01)

sys.exit(code if code >= 0 else 128 - code)
