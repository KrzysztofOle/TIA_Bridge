"""Offline design-package checks. Does not load Siemens or connect to any service."""
from __future__ import annotations

import argparse
import copy
import datetime as dt
import json
from pathlib import Path
import re
import sys
from urllib.parse import unquote, urlsplit

try:
    from jsonschema import Draft202012Validator, FormatChecker
except ImportError:
    raise SystemExit("Missing optional QA dependency: jsonschema. See scripts/README.md.")

ROOT = Path(__file__).resolve().parents[1]
REQUIRED = [
    "README.md", "AGENTS.md", "Zmiany.md",
    "docs/01_AUDIT_OLD_BRIDGE.md", "docs/02_REQUIREMENTS.md",
    "docs/03_ARCHITECTURE.md", "docs/04_SYNCHRONIZATION.md",
    "docs/05_CODEX_INTERFACE.md", "docs/06_TIA_INTEGRATION.md",
    "docs/07_TEST_STRATEGY.md", "docs/08_IMPLEMENTATION_PLAN.md",
    "docs/09_RISKS_AND_DECISIONS.md",
]


def read_json(path: Path):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError(f"Duplicate JSON key in {path.name}: {key}")
            result[key] = value
        return result
    return json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=unique,
                      parse_constant=lambda value: (_ for _ in ()).throw(ValueError(value)))


def slug(text: str) -> str:
    text = re.sub(r"[`*_]", "", text.lower())
    text = re.sub(r"[^\w\s-]", "", text, flags=re.UNICODE)
    return re.sub(r"\s", "-", text)


def check_package() -> dict:
    errors: list[str] = []
    for relative in REQUIRED:
        if not (ROOT / relative).is_file():
            errors.append(f"Missing required document: {relative}")
    markdown = sorted(p for p in ROOT.rglob("*.md")
                      if not any(part in {".git", ".tia-bridge", "node_modules"} for part in p.parts))
    local_links = 0
    external_links: set[str] = set()
    for path in markdown:
        content = path.read_text(encoding="utf-8-sig")
        if len(re.findall(r"^```", content, flags=re.M)) % 2:
            errors.append(f"Unclosed code fence: {path.relative_to(ROOT)}")
        table_width = None
        for line in content.splitlines():
            if line.startswith("| "):
                width = len(re.findall(r"(?<!\\)\|", line))
                if table_width is not None and width != table_width:
                    errors.append(f"Table column mismatch: {path.relative_to(ROOT)}: {line[:90]}")
                table_width = width
            else:
                table_width = None
        # Inline links used by this package. Ignore code blocks before checking.
        plain = re.sub(r"^```[^\n]*\n.*?^```[^\n]*$", "", content, flags=re.M | re.S)
        for target in re.findall(r"\[[^\]\n]+\]\(([^)\n]+)\)", plain):
            target = target.strip().strip("<>")
            if urlsplit(target).scheme in {"http", "https", "mailto"}:
                external_links.add(target)
                continue
            local_links += 1
            relative, _, anchor = unquote(target).partition("#")
            resolved = (path.parent / relative).resolve() if relative else path
            if not resolved.is_relative_to(ROOT):
                errors.append(f"Local link escapes package: {path.name}: {target}")
                continue
            if not resolved.exists():
                errors.append(f"Broken link: {path.relative_to(ROOT)} -> {target}")
            elif anchor and resolved.suffix == ".md":
                headings = re.findall(r"^#{1,6}\s+(.+)$", resolved.read_text(encoding="utf-8-sig"), re.M)
                if anchor not in {slug(h) for h in headings}:
                    errors.append(f"Missing anchor: {path.name} -> {target}")

    base = ROOT / "contracts/v1"
    schema = read_json(base / "operation-request.schema.json")
    result_schema = read_json(base / "operation.schema.json")
    Draft202012Validator.check_schema(schema)
    Draft202012Validator.check_schema(result_schema)
    request_validator = Draft202012Validator(schema, format_checker=FormatChecker())
    result_validator = Draft202012Validator(result_schema, format_checker=FormatChecker())
    registry = read_json(base / "operations.json")["operations"]
    names = [row["name"] for row in registry]
    branches = {row["properties"]["method"]["const"]: row for row in schema["oneOf"]}
    doc = (ROOT / "docs/05_CODEX_INTERFACE.md").read_text(encoding="utf-8")
    table = {name: line for line in doc.splitlines()
             if (match := re.match(r"\| `([a-z]+\.[a-z]+)` \|", line))
             for name in [match.group(1)]}
    if len(names) != 19 or len(set(names)) != 19:
        errors.append("Expected 19 unique operations")
    if set(names) != set(branches) or set(names) != set(table):
        errors.append("Registry/schema/interface operation names differ")
    for row in registry:
        name = row["name"]
        if not (0 < row["default_timeout_s"] <= row["max_timeout_s"]):
            errors.append(f"Invalid timeout interval: {name}")
        if not row["permissions"] or not row["error_codes"]:
            errors.append(f"Missing permission/errors: {name}")
        if row["required_params"] != branches[name]["properties"]["params"]["required"]:
            errors.append(f"Required fields differ: {name}")
        pair = f'{row["default_timeout_s"]}/{row["max_timeout_s"]}'
        if pair not in table.get(name, ""):
            errors.append(f"Document timeout differs: {name}")
    if set(result_schema["properties"]["method"]["enum"]) != set(names):
        errors.append("Result schema method enum differs")

    examples = ROOT / "examples/contracts"
    positive = 0
    for path in sorted(examples.glob("*.json")):
        validator = request_validator if path.name.startswith("request.") else result_validator
        found = list(validator.iter_errors(read_json(path)))
        if found:
            errors.append(f"Invalid example: {path.name}: {found[0].message[:160]}")
        positive += 1
    apply = read_json(examples / "request.sync-apply.json")
    plc = read_json(examples / "request.test.plc.json")
    unknown = read_json(examples / "operation.unknown.json")
    negatives = []
    item = copy.deepcopy(apply); del item["params"]["authorization_ref"]
    negatives.append(("Apply without authorization", request_validator, item))
    item = copy.deepcopy(apply); item["params"]["expected_plan_sha256"] = "not-a-sha"
    negatives.append(("Invalid hash", request_validator, item))
    item = copy.deepcopy(apply); item["params"]["force"] = True
    negatives.append(("Unknown force param", request_validator, item))
    item = copy.deepcopy(apply); del item["project_key"]
    negatives.append(("Missing project binding", request_validator, item))
    item = copy.deepcopy(plc); item["params"]["allow_writes"] = True
    negatives.append(("PLC write without grant", request_validator, item))
    item = copy.deepcopy(plc); item["params"]["transport"] = "auto-fallback"
    negatives.append(("Implicit transport fallback", request_validator, item))
    item = copy.deepcopy(unknown); item["state"] = "SUCCEEDED"
    negatives.append(("False success with UNKNOWN outcome", result_validator, item))
    item = copy.deepcopy(unknown); item["outcome"] = "CONFIRMED"
    negatives.append(("Reconciliation with confirmed outcome", result_validator, item))
    for label, validator, value in negatives:
        if validator.is_valid(value):
            errors.append(f"Negative contract accepted: {label}")

    audit = (ROOT / "docs/01_AUDIT_OLD_BRIDGE.md").read_text(encoding="utf-8")
    test = (ROOT / "docs/07_TEST_STRATEGY.md").read_text(encoding="utf-8")
    plan = (ROOT / "docs/08_IMPLEMENTATION_PLAN.md").read_text(encoding="utf-8")
    for i in range(1, 25):
        if not re.search(rf"\| OB-{i:02d} /", audit):
            errors.append(f"Missing audit row OB-{i:02d}")
    for i in range(1, 17):
        if f"| RT-{i:02d} |" not in test:
            errors.append(f"Missing resilience scenario RT-{i:02d}")
    covered = {int(n) for n in re.findall(r"OB-(\d+)", plan)}
    for first, last in re.findall(r"OB-(\d+)[–-](?:OB-)?(\d+)", plan):
        covered.update(range(int(first), int(last) + 1))
    if not set(range(1, 25)).issubset(covered):
        errors.append("Implementation plan does not map all audit issues")
    stages = {int(n) for n in re.findall(r"^## Etap (\d+)\b", plan, re.M)}
    if stages != set(range(12)):
        errors.append("Implementation stages must cover 0..11")
    evidence = read_json(ROOT / "docs/evidence/source-index.json")
    for source in evidence["sources"]:
        if not re.fullmatch(r"[a-f0-9]{64}", source["sha256"]):
            errors.append(f'Invalid source evidence hash: {source["path"]}')
    return {"schema": "tia-bridge2-design-qa/1", "status": "PASS" if not errors else "FAIL",
            "checked_at_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
            "markdown_files": len(markdown), "required_documents": len(REQUIRED),
            "local_links": local_links, "external_links_recorded_not_rechecked": len(external_links),
            "operations": len(names), "positive_examples": positive,
            "negative_contract_cases": len(negatives), "audit_issues": 24,
            "resilience_scenarios": 16, "implementation_stages": len(stages),
            "source_evidence_entries": len(evidence["sources"]), "errors": errors,
            "limits": "Offline document/schema checks only. No Siemens, live app, TLS, licence or Runtime validation."}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", type=Path, help="Optional output beneath ignored .tia-bridge/")
    args = parser.parse_args()
    report = check_package()
    if args.report:
        destination = args.report.resolve()
        allowed = (ROOT / ".tia-bridge").resolve()
        if destination == allowed or not destination.is_relative_to(allowed):
            parser.error("Report must be below this repository's ignored .tia-bridge/")
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if report["status"] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
