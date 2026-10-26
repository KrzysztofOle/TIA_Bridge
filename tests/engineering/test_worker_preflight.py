"""Opt-in V19 startup/loader checks. No Attach, project access, or vendor operation.

Set TIA_BRIDGE_V19_WORKER to the exact deployed EXE of an owned X-01 run.
This directory is deliberately outside offline contracts discovery.
"""
import ctypes
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import unittest
import uuid


class WorkerPreflightTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if os.name != "nt" or "TIA_BRIDGE_V19_WORKER" not in os.environ:
            raise RuntimeError("Explicit Windows Engineering worker path required; see README.md.")
        cls.worker = Path(os.environ["TIA_BRIDGE_V19_WORKER"])
        cls.root = cls.worker.parent.parent
        base = Path(os.environ["LOCALAPPDATA"]) / "TIA_Bridge2/dev/x01"
        if not cls.worker.is_absolute() or cls.worker.name != "TiaBridge.Worker.V19.exe" or not cls.root.is_relative_to(base):
            raise RuntimeError("Owned deployed X-01 worker required.")
        for part in [cls.worker, *cls.worker.parents]:
            if ctypes.windll.kernel32.GetFileAttributesW(str(part)) & 0x400:
                raise RuntimeError("Reparse point is forbidden.")
        if (cls.root / ".tia-bridge2-storage-v1").read_text().strip() != "TIA_BRIDGE2_STORAGE_V1":
            raise RuntimeError("Storage ownership marker required.")
        filesystem = ctypes.create_unicode_buffer(32)
        if not ctypes.windll.kernel32.GetVolumeInformationW(cls.root.anchor, None, 0, None, None, None, filesystem, 32) or filesystem.value != "NTFS" or ctypes.windll.kernel32.GetDriveTypeW(cls.root.anchor) != 3:
            raise RuntimeError("Local fixed NTFS required.")

    def run_worker(self, argument, worker=None):
        return subprocess.run([str(worker or self.worker), argument], capture_output=True,
                              text=True, timeout=15, creationflags=subprocess.CREATE_NO_WINDOW, check=False)

    def test_x64_runtime_starts_without_loading_vendor(self):
        result = self.run_worker("--self-test")
        self.assertEqual(result.returncode, 0)
        self.assertEqual(json.loads(result.stdout), {"net48_x64": True, "vendor_loaded": False})

    def test_loader_uses_exact_protected_v19_assembly_and_hash(self):
        result = self.run_worker("--loader-check")
        self.assertEqual(result.returncode, 0)
        observed = json.loads(result.stdout)
        approved = Path(os.environ["ProgramFiles"]) / "Siemens/Automation/Portal V19/PublicAPI/V19/Siemens.Engineering.dll"
        self.assertEqual(Path(observed["location"]), approved)
        self.assertEqual(observed["identity"], "Siemens.Engineering, Version=19.0.0.0, Culture=neutral, PublicKeyToken=d29ec89bac048f84")
        self.assertEqual(observed["sha256"], hashlib.sha256(approved.read_bytes()).hexdigest())
        self.assertTrue(observed["x64"])

    def test_vendor_shadow_is_rejected_before_loading_or_attach(self):
        evidence = self.root / ("loader-negative-" + uuid.uuid4().hex)
        evidence.mkdir()
        for source in self.worker.parent.iterdir():
            if source.is_file():
                self.assertFalse(source.name.startswith("Siemens"))
                shutil.copy2(source, evidence / source.name)
        (evidence / "Siemens.Engineering.dll").write_bytes(b"synthetic invalid library; never executed")
        result = self.run_worker("--loader-check", evidence / self.worker.name)
        self.assertEqual(result.returncode, 74)
        self.assertEqual(json.loads(result.stderr)["error_type"], "System.IO.FileLoadException")
        # Retain only our synthetic evidence. Never delete a pre-existing or project directory.
        (evidence / "result.json").write_text(json.dumps({"status": "PASS", "synthetic_shadow_rejected": True}), encoding="utf-8")

    def test_unknown_argument_fails_without_executing_an_operation(self):
        result = self.run_worker("--not-a-worker-command")
        self.assertEqual(result.returncode, 74)
        self.assertEqual(json.loads(result.stderr)["error_type"], "System.ArgumentException")


if __name__ == "__main__":
    unittest.main()
