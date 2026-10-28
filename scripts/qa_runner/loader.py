"""Strict, bounded JSON inputs. Only project configuration defines executable tasks."""
from __future__ import annotations

import json
import math
import re
from pathlib import Path

ADAPTERS = {"dotnet_restore", "dotnet_build", "dotnet_test", "python_test", "python_script", "approved_command"}
IDENTIFIER = re.compile(r"[a-zA-Z0-9][a-zA-Z0-9_.-]{0,79}\Z")


class Invalid(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise Invalid(message)


def fields(value, required, optional=()):
    require(isinstance(value, dict), "Expected JSON object")
    require(not (set(required) - value.keys()), "Missing fields: " + str(sorted(set(required) - value.keys())))
    require(not (value.keys() - set(required) - set(optional)), "Unknown fields: " + str(sorted(value.keys() - set(required) - set(optional))))


def identifier(value):
    require(isinstance(value, str) and IDENTIFIER.fullmatch(value), "Invalid identifier")
    reserved = {"CON", "PRN", "AUX", "NUL", *["COM" + str(n) for n in range(1, 10)], *["LPT" + str(n) for n in range(1, 10)]}
    require(value.split(".", 1)[0].upper() not in reserved, "Reserved filename identifier")
    return value


def strings(value):
    require(isinstance(value, list) and len(value) <= 200 and all(isinstance(v, str) and len(v) <= 4096 for v in value), "Expected bounded string list")
    return value


def seconds(value):
    require(type(value) in (int, float) and math.isfinite(value) and 0 < value <= 86400, "Timeout must be >0 and <=86400 seconds")


def contained(root, relative):
    require(isinstance(relative, str) and relative and not any(c in relative for c in ("\\", ":", "\x00")), "Use safe relative paths with forward slashes")
    path = Path(relative)
    require(not path.is_absolute() and ".." not in path.parts, "Path traversal denied")
    root = root.resolve()
    result = (root / path).resolve()
    require(result.is_relative_to(root), "Path escapes root (including symlink/junction)")
    return result


def read_json(path, limit=1024 * 1024):
    require(path.stat().st_size <= limit, "JSON input too large")

    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, "Duplicate JSON key: " + key)
            result[key] = value
        return result

    try:
        value = json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=unique,
                           parse_constant=lambda _: require(False, "Nonfinite JSON number"))
        def depth(item, level=0):
            require(level <= 20, "JSON nesting limit exceeded")
            if isinstance(item, dict):
                for child in item.values():
                    depth(child, level + 1)
            elif isinstance(item, list):
                for child in item:
                    depth(child, level + 1)
        depth(value)
        return value
    except (json.JSONDecodeError, RecursionError, UnicodeError) as error:
        raise Invalid("Invalid JSON: " + str(error)) from error


def discover(start):
    for folder in [start.resolve(), *start.resolve().parents]:
        if (folder / "qa.project.json").is_file():
            return folder / "qa.project.json"
    raise Invalid("No qa.project.json. Run from the project folder or use --config <file>. See docs/QA_RUNNER.md.")


def load_project(path):
    root = path.resolve().parent
    value = read_json(path)
    fields(value, ["schema_version", "project_id", "default_profile", "scenario_root", "results_root", "runtimes", "tasks", "profiles"], ["performance_profiles", "environment"])
    require(value["schema_version"] == "1.0", "Unsupported project schema_version")
    identifier(value["project_id"])
    contained(root, value["scenario_root"])
    contained(root, value["results_root"])
    require(value["results_root"] != ".", "results_root must be a dedicated directory")
    require(isinstance(value["runtimes"], dict) and value["runtimes"], "Missing runtimes")
    for name, runtime in value["runtimes"].items():
        identifier(name)
        fields(runtime, ["candidates"], ["modules"])
        require(strings(runtime["candidates"]), "Missing runtime candidates")
        for module in strings(runtime.get("modules", [])):
            require(re.fullmatch(r"[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*", module), "Invalid module name")
    require(isinstance(value["tasks"], dict) and 0 < len(value["tasks"]) <= 200, "Missing/too many tasks")
    for name, task in value["tasks"].items():
        identifier(name)
        fields(task, ["type", "offline", "runtime", "timeout_seconds"],
               ["project", "configuration", "filter", "path", "pattern", "args", "expected_exit_code", "env", "collect", "receipt", "min_tests"])
        require(task["type"] in ADAPTERS, "Unknown adapter: " + str(task["type"]))
        require(task["offline"] is True, "v1 accepts offline tasks only; live requires existing authorization and a separately implemented adapter")
        require(task["runtime"] in value["runtimes"], "Unknown runtime")
        seconds(task["timeout_seconds"])
        require(task.get("expected_exit_code", 0) == 0, "v1 requires exit code 0; express negative assertions inside tests")
        require(type(task.get("min_tests", 1)) is int and task.get("min_tests", 1) > 0, "min_tests must be positive")
        for key in ["project", "path"]:
            if key in task:
                contained(root, task[key])
        if task["type"].startswith("dotnet_"):
            require("project" in task, "dotnet adapter requires project")
        if task["type"].startswith("python_"):
            require("path" in task, "python adapter requires path")
        for key in ["configuration", "filter", "pattern"]:
            if key in task:
                require(isinstance(task[key], str) and 0 < len(task[key]) <= 4096, "Invalid task parameter")
        strings(task.get("args", []))
        require(isinstance(task.get("env", {}), dict), "env must be object")
        for key, item in task.get("env", {}).items():
            require(re.fullmatch(r"[A-Za-z_]\w*", key) and isinstance(item, str), "Invalid environment entry")
        for pattern in strings(task.get("collect", [])):
            contained(root, pattern)
        if "receipt" in task:
            receipt = task["receipt"]
            fields(receipt, ["pattern", "status_field", "layers"], ["bundle", "counts", "diagnostics"])
            contained(root, receipt["pattern"])
            identifier(receipt["status_field"])
            require(isinstance(receipt["layers"], dict), "receipt layers must be object")
            for layer, field in receipt["layers"].items():
                identifier(layer); identifier(field)
            require(type(receipt.get("bundle", False)) is bool, "bundle must be boolean")
            for layer, field in receipt.get("counts", {}).items():
                identifier(layer); identifier(field)
            if "diagnostics" in receipt:
                fields(receipt["diagnostics"], ["path", "fields"])
                contained(root, receipt["diagnostics"]["path"])
                for field in strings(receipt["diagnostics"]["fields"]):
                    identifier(field)
    require(isinstance(value["profiles"], dict) and value["profiles"], "Missing profiles")
    for name, entries in value["profiles"].items():
        identifier(name)
        require(strings(entries), "Empty profile")
        for entry in entries:
            contained(contained(root, value["scenario_root"]), entry)
    require(value["default_profile"] in value["profiles"], "Unknown default profile")
    if "performance_profiles" in value:
        perf = value["performance_profiles"]
        fields(perf, ["default", "allowed", "definition"])
        require(perf["default"] in strings(perf["allowed"]), "Invalid default performance profile")
        contained(root, perf["definition"])
    if "environment" in value:
        environment = value["environment"]
        fields(environment, ["clear_prefixes", "values"])
        for prefix in strings(environment["clear_prefixes"]):
            require(re.fullmatch(r"[A-Za-z_]\w*", prefix), "Invalid environment prefix")
        require(isinstance(environment["values"], dict), "Environment values must be object")
        for key, item in environment["values"].items():
            require(re.fullmatch(r"[A-Za-z_]\w*", key) and isinstance(item, str), "Invalid project environment entry")
    return root, value


def load_scenario(path, root, project):
    require(path.resolve().is_relative_to(contained(root, project["scenario_root"])), "Scenario must be within scenario_root")
    value = read_json(path)
    fields(value, ["schema_version", "id", "description", "tags", "mode", "timeout_seconds", "requires", "on_failure", "steps"])
    require(value["schema_version"] == "1.0", "Unsupported scenario schema_version")
    identifier(value["id"])
    require(isinstance(value["description"], str) and 0 < len(value["description"]) <= 2000, "Missing description")
    strings(value["tags"])
    require(value["mode"] == "offline", "Live scenarios denied in v1")
    seconds(value["timeout_seconds"])
    require(value["on_failure"] in {"stop", "continue"}, "Invalid on_failure")
    for runtime in strings(value["requires"]):
        require(runtime in project["runtimes"], "Unknown required runtime")
    require(isinstance(value["steps"], list) and 0 < len(value["steps"]) <= 100, "Missing/too many steps")
    seen = set()
    for step in value["steps"]:
        fields(step, ["id", "type", "task", "timeout_seconds", "required"])
        identifier(step["id"])
        require(step["id"].casefold() not in seen, "Duplicate step id (case-insensitive)")
        seen.add(step["id"].casefold())
        require(step["type"] in ADAPTERS, "Unknown step type: " + str(step["type"]))
        require(step["task"] in project["tasks"], "Task is not approved by project configuration")
        task = project["tasks"][step["task"]]
        require(step["type"] == task["type"], "Step/approved task adapter mismatch")
        seconds(step["timeout_seconds"])
        require(step["timeout_seconds"] <= task["timeout_seconds"], "Step timeout exceeds approved task limit")
        require(type(step["required"]) is bool, "required must be boolean")
    require(any(s["required"] for s in value["steps"]), "At least one step must be required")
    return value
