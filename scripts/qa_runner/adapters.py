"""No shell and no scenario-supplied argv. Configuration and repository code are trusted."""
from __future__ import annotations

import os
import shutil
import signal
import subprocess
import sys
import threading
import time
from pathlib import Path

from .diagnostics import redact
from .loader import contained


def runtime(definition):
    errors = []
    for candidate in definition["candidates"]:
        candidate = os.path.expandvars(candidate.replace("{python}", sys.executable))
        executable = shutil.which(candidate)
        if not executable:
            errors.append(candidate + ": missing")
            continue
        modules = definition.get("modules")
        try:
            if modules is not None:
                probe = subprocess.run([executable, "-I", "-c", "import importlib.util,sys; print(sys.version.split()[0]); sys.exit(0 if sys.version_info >= (3,12) and all(importlib.util.find_spec(m) for m in sys.argv[1:]) else 1)", *modules],
                                       capture_output=True, text=True, timeout=15, check=False)
                if probe.returncode:
                    errors.append(candidate + ": Python 3.12+ or required modules missing")
                    continue
            return str(Path(executable).resolve())
        except (OSError, subprocess.SubprocessError) as error:
            errors.append(candidate + ": " + type(error).__name__)
    raise FileNotFoundError("Runtime unavailable: " + "; ".join(errors))


def expand(value, output, performance_profile):
    return value.replace("{artifacts}", str(output)).replace("{performance_profile}", performance_profile or "")


def command(task, executable, root, output, performance_profile):
    kind = task["type"]
    project = task.get("project", "")
    configuration = task.get("configuration", "Release")
    if kind == "dotnet_restore":
        feed = output / "empty-feed"
        feed.mkdir()
        argv = [executable, "restore", project, "--locked-mode", "--source", str(feed), "-p:NuGetAudit=false"]
    elif kind == "dotnet_build":
        argv = [executable, "build", project, "-c", configuration, "--no-restore", "-m:1"]
    elif kind == "dotnet_test":
        argv = [executable, "test", project, "-c", configuration, "--no-build", "--no-restore", "-m:1", "--logger", "trx", "--results-directory", str(output)]
        if "filter" in task:
            argv += ["--filter", task["filter"]]
    elif kind == "python_test":
        # Owned harness emits machine-readable unittest results, including subtest failures.
        harness = Path(__file__).with_name("unittest_harness.py")
        argv = [executable, "-B", str(harness), str(root), task["path"], task.get("pattern", "test_*.py"), str(output / "unittest.json")]
    elif kind == "python_script":
        argv = [executable, "-B", str(contained(root, task["path"]))]
    else:
        argv = [executable]
    argv += [expand(arg, output, performance_profile) for arg in task.get("args", [])]
    return argv


def terminate_owned(process):
    """Only our offline test subtree. Never enumerate/terminate by process name."""
    if os.name == "nt":
        # PID is the still-owned Popen child; no user-supplied PID or image name.
        subprocess.run([os.path.join(os.environ["SystemRoot"], "System32", "taskkill.exe"), "/PID", str(process.pid), "/T", "/F"],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=15, check=False)
    else:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
    try:
        process.wait(timeout=15)
    except subprocess.TimeoutExpired:
        raise OSError("Owned offline child termination unconfirmed")


def execute(argv, cwd, environment, log_path, timeout, cancelled):
    started = time.monotonic()
    drain_errors = []
    with log_path.open("x", encoding="utf-8", newline="\n") as log:
        options = {"creationflags": subprocess.CREATE_NEW_PROCESS_GROUP} if os.name == "nt" else {"start_new_session": True}
        with subprocess.Popen(argv, cwd=cwd, env=environment, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                              text=True, encoding="utf-8", errors="replace", **options) as process:
            def drain():
                try:
                    # Complete output on disk; bounded chunks avoid loading entire logs in memory.
                    for chunk in iter(lambda: process.stdout.readline(65536), ""):
                        log.write(redact(chunk))
                        log.flush()
                except OSError as error:
                    drain_errors.append(error)
            reader = threading.Thread(target=drain, daemon=True)
            reader.start()
            reason = None
            try:
                while process.poll() is None:
                    if cancelled.is_set():
                        reason = "INTERRUPTED"
                        break
                    if time.monotonic() - started >= timeout:
                        reason = "TEST_TIMEOUT"
                        break
                    time.sleep(0.05)
            finally:
                if process.poll() is None:
                    terminate_owned(process)
                reader.join(timeout=5)
            if reader.is_alive():
                raise OSError("Offline child output still open after process exit; review owned descendants")
            if drain_errors:
                raise drain_errors[0]
            return process.returncode, reason, round((time.monotonic() - started) * 1000, 3)
