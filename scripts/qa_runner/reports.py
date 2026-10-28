"""Small console view, standalone Markdown and immutable evidence inventory."""
from .diagnostics import inventory, write_json


def comparison(summary, previous):
    if previous is None:
        return {"previous_run_id": None, "new_failures": [], "resolved_failures": []}
    def identities(value):
        return {f["category"] + ": " + (f.get("test") or f["message"]) for f in value.get("failures", [])}
    before, after = identities(previous), identities(summary)
    return {"previous_run_id": previous["run_id"], "previous_status": previous["status"],
            "new_failures": sorted(after - before), "resolved_failures": sorted(before - after),
            "duration_delta_ms": round(summary["duration_ms"] - previous["duration_ms"], 3)}


def compact(summary, failures_only=False):
    counts = summary["tests"]
    lines = [f"PROJECT: {summary['project_id']}  PROFILE: {summary['profile']}  RESULT: {summary['status']}",
             f"RUN ID: {summary['run_id']}", f"TESTS: {counts['passed']} PASS / {counts['failed']} FAIL / {counts['skipped']} SKIPPED",
             "LAYERS: " + "; ".join(f"{k}={v}" for k, v in summary["layers"].items())]
    if not failures_only:
        for s in summary["steps"]:
            counts = s.get("tests")
            tests = f"; {counts['passed']} PASS/{counts['failed']} FAIL/{counts['skipped']} SKIPPED" if counts else ""
            lines.append(f"{s['scenario_id']}/{s['step_id']}: {s['status']} ({s['duration_ms']/1000:.2f}s{tests})")
    lines += [f"{f['category']}: {f.get('test') or f['message']} (x{f['occurrences']}; {f.get('log_reference')})" for f in summary["failures"][:10]]
    if "diagnostics" in summary:
        d = summary["diagnostics"]
        if d["status"] == "AVAILABLE":
            lines.append(f"DIAGNOSTICS: {d['count']} events; {d['warn_error']} WARN/ERROR; {len(d['invalid_records'])} invalid; {d['without_correlation_ids']} without IDs")
            lines.append("DIAGNOSTICS FILE: " + d["normalized_reference"])
        else:
            lines.append("DIAGNOSTICS: NOT_RUN (" + d["reason"] + ")")
    if len(summary["failures"]) > 10:
        lines.append("Additional failure groups: see failures.json")
    lines += ["REPORT: " + summary["report_path"]]
    return "\n".join(lines)


def markdown(summary):
    def cell(value):
        return str(value).replace("|", "\\|").replace("\n", " ").replace("\r", "")
    git = summary["git"]
    lines = [f"# QA — {summary['project_id']}", "", f"Date (UTC): {summary['started_at']}",
             f"Run ID: `{summary['run_id']}`", f"Profile: **{summary['profile']}**; result: **{summary['status']}**",
             f"Git SHA: `{git['sha']}`; sources: {git['state']}", f"Duration: {summary['duration_ms']/1000:.2f} s",
             f"Tests: {summary['tests']['passed']} PASS / {summary['tests']['failed']} FAIL / {summary['tests']['skipped']} SKIPPED", "",
             "## Evidence layers", "", *[f"- {key}: **{value}**" for key, value in summary["layers"].items()], "",
             f"Performance profile: `{summary.get('performance_profile')}`; definition SHA-256: `{summary.get('performance_definition_sha256')}`", "",
             "## Stages", "", "| Scenario / step | Required | Result | Tests P/F/S | Seconds | Log |", "|---|---|---|---|---|---|"]
    for s in summary["steps"]:
        counts = s.get("tests")
        tests = f"{counts['passed']}/{counts['failed']}/{counts['skipped']}" if counts else "—"
        lines.append(f"| {cell(s['scenario_id'])}/{cell(s['step_id'])} | {s['required']} | {s['status']} | {tests} | {s['duration_ms']/1000:.3f} | {cell(s.get('log_reference'))} |")
    lines += ["", "## Failures and blockers", ""]
    if not summary["failures"]:
        lines.append("None.")
    for f in summary["failures"]:
        lines += [f"- **{f['category']}** ({f['occurrences']} occurrences): {cell(f.get('test') or f['message'])}",
                  f"  First failure: {cell(f['message'])}", f"  Source: {cell(f.get('source_location'))}; log: {cell(f.get('log_reference'))}"]
    if "diagnostics" in summary:
        d = summary["diagnostics"]
        if d["status"] != "AVAILABLE":
            lines += ["", "## Diagnostics", "", "Status: **NOT_RUN** — no structured diagnostic logs were collected. This is not a PASS result."]
        else:
            lines += ["", "## Diagnostics", "", f"Events: {d['count']}; WARN/ERROR: {d['warn_error']}; without correlation IDs: {d['without_correlation_ids']}; invalid records: {len(d['invalid_records'])}.",
                  "Normalized events: `diagnostics.jsonl`. Raw log references are retained in each normalized record.", "", "Grouped errors:"]
            lines += [f"- {cell(key)}: {count}" for key, count in d["error_groups"].items()] or ["- None"]
            lines += ["", "Important events:"]
            lines += [f"- {cell(e.get('timestamp_utc'))} **{cell(e.get('level'))} {cell(e.get('component'))}/{cell(e.get('event_id'))}**: {cell(e.get('message'))} (`{cell(e.get('log_reference'))}`)" for e in d["top_events"]] or ["- None"]
            lines += ["", "Invalid records:"]
            lines += [f"- {cell(e['log_reference'])} record {e['record']}: {e['reason']}" for e in d["invalid_records"][:20]] or ["- None"]
            lines += ["", "Operation flows use only explicit operation IDs and recognized component values; unobserved stages remain UNKNOWN."]
            for operation, flow in list(d["operation_flows"].items())[:10]:
                lines.append("- `" + cell(operation) + "`: " + " → ".join(f"{stage}={cell(value.get('event_id') if isinstance(value, dict) else value)}" for stage, value in flow.items()))
    remaining = [s for s in summary["steps"] if s["status"] in {"NOT_RUN", "SKIPPED"}]
    if remaining:
        lines += ["", "Not executed: " + ", ".join(s["scenario_id"] + "/" + s["step_id"] for s in remaining)]
    lines += ["", "## Comparison", "", f"Previous compatible run: `{summary['comparison']['previous_run_id']}`",
              "New failures: " + cell(summary["comparison"]["new_failures"]),
              "Resolved failures: " + cell(summary["comparison"]["resolved_failures"]), "", "## Next action", "",
              "Inspect the first failure and its log, correct the cause, then create a new run." if summary["status"] != "PASS" else "No functional failure in this scope. Formal qualification and Engineering require their own evidence.",
              "", "## Evidence", "", "`summary.json`, `execution.json`, `failures.json`, `logs/`, `artifacts/`, `evidence-manifest.json` in this run directory.",
              "The manifest covers every file except itself. Full logs are retained locally. Receipt bundles preserve their original manifests.", ""]
    return "\n".join(lines)


def finalize(folder, summary, execution):
    write_json(folder / "execution.json", execution)
    write_json(folder / "summary.json", summary)
    write_json(folder / "failures.json", {"schema_version": "1.0", "run_id": summary["run_id"], "groups": summary["failures"]})
    with (folder / "report.md").open("x", encoding="utf-8", newline="\n") as stream:
        stream.write(markdown(summary))
    write_json(folder / "evidence-manifest.json", {"schema_version": "1.0", "run_id": summary["run_id"], "files": inventory(folder)})
