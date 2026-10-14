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
    # Only our direct/adopted children are inspected. Repeating handles a child
    # that created its own session and descendants adopted as its parent exits.
    children = Path(f"/proc/self/task/{os.getpid()}/children")
    while True:
        owned = children.read_text().split()
        if not owned:
            break
        for child in owned:
            try:
                os.kill(int(child), signal.SIGKILL)
            except ProcessLookupError:
                pass
        while True:
            try:
                reaped, _ = os.waitpid(-1, os.WNOHANG)
            except ChildProcessError:
                break
            if reaped == 0:
                break
        time.sleep(0.01)

sys.exit(code if code >= 0 else 128 - code)
