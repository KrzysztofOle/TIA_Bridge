"""Evaluate actual results, never infer PASS from missing test output."""
from __future__ import annotations

import re
import xml.etree.ElementTree as ET

from .diagnostics import redact
from .loader import read_json, require

STATUSES = {"PASS", "FAIL", "BLOCKED", "NOT_RUN", "SKIPPED", "REVIEW_REQUIRED"}


def aggregate(steps):
    if any(s["status"] == "FAIL" for s in steps):
        return "FAIL"
    if any(s["status"] == "REVIEW_REQUIRED" for s in steps):
        return "REVIEW_REQUIRED"
    if any(s["status"] == "BLOCKED" or (s["required"] and s["status"] in {"NOT_RUN", "SKIPPED"}) for s in steps):
        return "BLOCKED"
    return "PASS" if steps and all(s["status"] == "PASS" or not s["required"] for s in steps) else "NOT_RUN"


def failure(category, message, test=None, stack=None, log=None):
    message = redact(str(message))[:2000]
    stack = redact(str(stack or ""))[:16000]
    location = re.search(r'(?:in |File ")([^\r\n]+?(?:\.cs|\.py))(?::line |", line |:)(\d+)', stack)
    return {"category": category, "message": message, "test": test, "source_location": ":".join(location.groups()) if location else None,
            "stack_trace": stack or None, "log_reference": log}


def test_results(task, output):
    if task["type"] == "python_test":
        value = read_json(output / "unittest.json", 8 * 1024 * 1024)
        require(value["total"] >= task.get("min_tests", 1), "No/minimum Python tests not executed")
        counts = {key: value[key] for key in ["passed", "failed", "skipped"]}
        failures = [failure("TEST_ASSERTION", item["message"], item["test"], item["stack_trace"]) for item in value["failures"]]
        return counts, failures
    counts = dict(passed=0, failed=0, skipped=0)
    failures = []
    paths = list(output.glob("*.trx"))
    require(paths, "Missing TRX; tests not executed")
    for path in paths:
        require(path.stat().st_size <= 32 * 1024 * 1024, "TRX exceeds parser limit")
        data = path.read_text(encoding="utf-8-sig")
        require("<!DOCTYPE" not in data.upper() and "<!ENTITY" not in data.upper(), "XML entities/DOCTYPE denied")
        model = ET.fromstring(data)
        local = dict(passed=0, failed=0, skipped=0)
        for result in model.findall(".//{*}UnitTestResult"):
            outcome = result.get("outcome")
            key = "passed" if outcome == "Passed" else "failed" if outcome == "Failed" else "skipped"
            counts[key] += 1
            local[key] += 1
            if key == "failed" and len(failures) < 200:
                message = result.find(".//{*}Message")
                stack = result.find(".//{*}StackTrace")
                failures.append(failure("TEST_ASSERTION", message.text if message is not None else "Failed TRX result", result.get("testName"), stack.text if stack is not None else None))
        counters = model.find(".//{*}Counters")
        require(counters is not None and int(counters.get("total", "0")) > 0, "Missing TRX execution counters")
        require(int(counters.get("total", "0")) == len(model.findall(".//{*}UnitTestResult")), "Incomplete TRX results")
        for key in ["passed", "failed"]:
            if key in counters.attrib:
                require(int(counters.get(key)) == local[key], "Contradictory TRX counters")
        summary = model.find(".//{*}ResultSummary")
        require(summary is not None and summary.get("outcome") in {"Completed", "Passed", "Failed"}, "Aborted/error TRX execution")
        require(summary.get("outcome") != "Failed" or local["failed"] > 0, "Failed TRX run without individual failure evidence")
    require(sum(counts.values()) >= task.get("min_tests", 1), "No/minimum .NET tests not executed")
    return counts, failures


def group_failures(steps):
    groups = {}
    for step in steps:
        for item in step.get("failures", []):
            key = (item["category"], item.get("test"), item["message"])
            if key not in groups:
                groups[key] = {**item, "occurrences": 0, "steps": []}
            groups[key]["occurrences"] += 1
            groups[key]["steps"].append(step["scenario_id"] + "/" + step["step_id"])
    return list(groups.values())[:100]
