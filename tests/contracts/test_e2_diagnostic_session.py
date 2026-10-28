"""Pure guards only. Never constructs Session, launches Host or imports Siemens."""
import importlib.util
from pathlib import Path
import unittest
from test_e2_gate import dossier

spec = importlib.util.spec_from_file_location("session", Path(__file__).resolve().parents[2]/"scripts/e2_diagnostic_session.py")
session = importlib.util.module_from_spec(spec); spec.loader.exec_module(session)


class DiagnosticSessionTests(unittest.TestCase):
    def test_worker_scalar_and_core_value_object_bind_to_same_project(self):
        binding = dict(project_key="synthetic-key", project_path="synthetic.ap19")
        raw = dict(binding, project_name="T05_RecipeSim_V19_Testowy", is_modified_before=False, is_modified_after=False)
        discovery = dict(raw, project_key={"value": "synthetic-key"})
        session.check_project_artifacts(raw, discovery, binding)
        for bad in ("synthetic-key", {"value": "other"}, {"value": "synthetic-key", "extra": True}, None):
            with self.assertRaisesRegex(RuntimeError, "PROJECT_BINDING"):
                session.check_project_artifacts(raw, dict(discovery, project_key=bad), binding)
        for artifact, key, value in ((raw, "project_key", "other"), (raw, "project_path", "other.ap19"),
                                     (discovery, "project_name", "other"), (discovery, "is_modified_after", True),
                                     (raw, "is_modified_before", 0)):
            changed = dict(artifact, **{key: value})
            with self.assertRaises(RuntimeError):
                session.check_project_artifacts(changed if artifact is raw else raw,
                                                changed if artifact is discovery else discovery, binding)

    def test_entry_rejects_legacy_twenty_and_binding_or_gate_drift(self):
        b = dossier()["expected_binding"]
        gate = dict(status="READY_FOR_OPERATOR_AUTHORIZATION", task="E2-G-D", purpose="HANDLE_DIAGNOSTICS", maximum_cycles=3)
        auth = dict(b, schema_version="1.1", purpose="HANDLE_DIAGNOSTICS", plan_id="E2-B1-D", maximum_cycles=3, project_name="T05_RecipeSim_V19_Testowy", authorization_id="synthetic")
        config = dict(qualification_bootstrap=dict(authorization_id="synthetic"))
        session.check_entry(gate, auth, config, b)
        for key, value in (("maximum_cycles",20),("purpose","QUALIFICATION"),("project_key","other"),("schema_version","1.0")):
            with self.assertRaises(RuntimeError): session.check_entry(gate, dict(auth, **{key:value}), config, b)
        with self.assertRaises(RuntimeError): session.check_entry(dict(gate,status="BLOCKED"),auth,config,b)

    def test_missing_dispose_and_different_epoch_are_not_interpolated(self):
        phases = ["cycle.enter","enumeration.returned","attach.enter","attach.returned","project.checked","devices.enter","devices.returned",
                  "software.returned.present","device_items.traversed","device.classified","inventory.returned","dispose.enter","dispose.returned","cycle.returned","worker.idle"]
        records = [dict(operation_id="op",worker_epoch="epoch",worker_pid=1,worker_start_ticks=2,sequence=n+1,phase=p) for n,p in enumerate(phases)]
        session.check_phases(records,"op","epoch",1,2)
        with self.assertRaises(RuntimeError): session.check_phases(records,"op","other",1,2)
        records[12]["phase"] = "missing"
        with self.assertRaises(RuntimeError): session.check_phases(records,"op","epoch",1,2)
