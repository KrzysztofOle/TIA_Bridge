"""Offline contract checks of the real executable; no TIA Attach or mutation."""
import json
import pathlib
import subprocess
import sys
import tempfile

exe = pathlib.Path(sys.argv[1]).resolve()
root = pathlib.Path(tempfile.mkdtemp(prefix="recipe-editor-v19-"))

def run(mode, name, *args):
    output = root / name
    result = subprocess.run([str(exe), mode, str(output), *args], capture_output=True)
    return result, output

r, output = run("plan", "plan")
assert r.returncode == 0, r.stderr
plan = json.loads((output / "plan.json").read_text())
controls = plan["controls"]
assert plan["serialized_text"]["EDIT_Context"] == "<body><p/></body>", "Observed Siemens empty-paragraph serialization"
assert plan["serialized_text"]["EDIT_ApplyChanges"] == "<body><p>Zastosuj zmiany</p></body>"
assert len(controls) == len({x["Name"].casefold() for x in controls}) == 30
assert sum(x["Kind"] == "input" for x in controls) == 19
assert sum(x["Kind"] == "text" for x in controls) == 5
assert sum(x["Kind"] == "button" for x in controls) == 6
assert plan["events"]["EDIT_ApplyChanges/Tapped"] == "applyChanges"
assert plan["events"]["EDIT_DiscardChanges/Tapped"] == "discardChanges"
assert len(plan["events"]) == 11
r_policy, policy_output = run("compile-policy-tests", "policy")
assert r_policy.returncode == 0, r_policy.stderr
assert json.loads((policy_output / "policy-tests.json").read_text())["cases"] == 6
assert {k for k in plan["events"] if not k.endswith("/Tapped")} == {
    "T05_Recipes_Editor/Loaded", "T05_Recipes_Editor/Unloaded"
}
for x in controls:
    assert x["X"] >= 0 and x["Y"] >= 0
    assert x["X"] + x["W"] <= 1920
    assert x["Y"] + x["H"] <= 980
visible = [x for x in controls if x["Visible"]]
for index, x in enumerate(visible):
    for y in visible[index + 1:]:
        assert not (x["X"] < y["X"] + y["W"] and y["X"] < x["X"] + x["W"]
                    and x["Y"] < y["Y"] + y["H"] and y["Y"] < x["Y"] + x["H"])
for index, path in enumerate([
    r"C:\RMA\TIA_TEST\T05_RecipeSim_V19\T05_RecipeSim_V19.ap19",
    r"X:\OtherProject\project.ap19",
]):
    r, output = run("configure", f"deny-{index}", path, "unused", "unused")
    assert r.returncode == 2
    failure = json.loads((output / "failure.json").read_text())
    assert failure["status"] == "BLOCKED" and "LAB_ONLY" in failure["error"]
    assert not (output / "before.json").exists(), "Rejected before TIA discovery"
before = {p.name: p.read_bytes() for p in (root / "plan").iterdir()}
assert subprocess.run([str(exe), "plan", str(root / "plan")], capture_output=True).returncode == 2
assert before == {p.name: p.read_bytes() for p in (root / "plan").iterdir()}
print(f"PASS: plan, bounds, overlaps, event contract, target denial and output preservation; {root}")
