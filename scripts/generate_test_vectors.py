"""B-01C independent synthetic golden vectors; Python standard library only.

Default verifies committed vectors. --write updates only the two named fixtures.
No Siemens, filesystem workspace materialization, network or synchronization planner.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def build():
    # Hand-defined presentations, not a second implementation of the production codec.
    names = [("Pump", "Pump"), ("pump", "pump"), ("CON", "_CON"),
             ("é", "é"), ("e\u0301", "é"), ("A/B", "A%2FB"),
             ("tail.", "_tail."), ("L" * 200, "L" * 86),
             ("😀" * 100, "😀" * 43), ("%" * 100, "%25" * 28)]
    vectors = []
    for index in range(100):
        name, presentation = names[index % 10]
        object_id = f"00000000-0000-4000-8000-{index + 4096:012x}"
        normalizer = f"synthetic-{index // 50 + 1}"
        model = {"case": index, "object_id": object_id, "sequence": [1, 2], "value": index % 5}
        canonical = json.dumps({"normalizer_version": normalizer, "comparison_profile": "golden-1", "model": model}, sort_keys=True, separators=(",", ":")).encode()
        vectors.append(dict(case_id=index, original_name=name, object_id=object_id,
                            expected_segment=presentation + "--" + object_id[-12:],
                            normalizer=normalizer, profile="golden-1", variant=index // 10 % 5,
                            model=model, model_sha256=hashlib.sha256(canonical).hexdigest(),
                            raw_sha256=hashlib.sha256(f"synthetic-{index}\r\n".encode()).hexdigest()))
    rules = []
    def rule(target, name, path=(), value=None, valid=False, remove=False):
        rules.append(dict(target=target, name=name, path=list(path), value=value, valid=valid, remove=remove))
    for target in ["request", "report", "error"]:
        rule(target, "valid", valid=True)
        rule(target, "unknown-feature", ["required_features"], ["future-required"])
    for target in ["request", "report"]:
        rule(target, "major-version", ["schema_version"], "2.0")
        rule(target, "minor-version", ["schema_version"], "1.1")
        rule(target, "unknown-method", ["method"], "vendor.invoke")
        rule(target, "missing-method", ["method"], remove=True)
    rule("request", "missing-key", ["idempotency_key"], remove=True)
    rule("request", "key-null", ["idempotency_key"], None)
    rule("request", "key-type", ["idempotency_key"], 123)
    rule("request", "params-type", ["params"], [])
    rule("request", "params-extra", ["params", "execute_shell"], "synthetic")
    rule("request", "key-unicode-limit", ["idempotency_key"], "😀" * 128, valid=True)
    rule("request", "key-unicode-over-limit", ["idempotency_key"], "😀" * 129)
    rule("report", "bad-id", ["operation_id"], "../project")
    rule("report", "null-id", ["operation_id"], None)
    rule("report", "result-type", ["result"], [])
    rule("report", "result-extra", ["result", "undeclared"], True)
    rule("report", "missing-result", ["result"], remove=True)
    rule("report", "missing-phase", ["phase"], remove=True)
    rule("report", "state-sync", ["state"], "SYNCED")
    rule("report", "unknown-success", ["outcome"], "UNKNOWN")
    rule("report", "negative-count", ["poll_after_ms"], -1)
    rule("report", "count-type", ["poll_after_ms"], "0")
    rule("error", "unknown-code", ["code"], "INVOKE_VENDOR")
    rule("error", "category-sync", ["category"], "SYNCED")
    rule("error", "missing-outcome", ["outcome"], remove=True)
    rule("error", "retry-type", ["retryable"], "true")
    rule("error", "message-null", ["message"], None)
    rule("error", "message-limit", ["message"], "x" * 2048, valid=True)
    rule("error", "message-over-limit", ["message"], "x" * 2049)
    rule("error", "phase-traversal", ["phase"], "../phase")
    return {"core-determinism.v1.json": vectors, "contract-mutations.v1.json": rules}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--write", action="store_true")
    args = parser.parse_args()
    for name, value in build().items():
        path = ROOT / "tests/fixtures" / name
        content = json.dumps(value, ensure_ascii=False, indent=2) + "\n"
        if args.write:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8")
        elif not path.exists() or path.read_text(encoding="utf-8") != content:
            raise SystemExit(f"Test vector drift: {name}")
    print("B-01C synthetic vectors: PASS (100 distinct cases)")


if __name__ == "__main__":
    main()
