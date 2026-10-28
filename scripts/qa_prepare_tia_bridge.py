"""TIA_Bridge integration only: create empty QA workspace roots before canonical preflight.

Python 3.12 standard library. No state DB, grants, ACL changes or Siemens access.
"""
import json
from pathlib import Path


def prepare(root):
    root = root.resolve()
    created = []
    for relative in [".tia-bridge/resilience/workspaces", ".tia-bridge/b02c/workspaces"]:
        target = root / relative
        if not target.resolve().is_relative_to(root):
            raise ValueError("QA workspace path escapes project")
        for ancestor in [target, *target.parents]:
            if ancestor == root:
                break
            if ancestor.is_symlink() or ancestor.is_junction():
                raise ValueError("QA workspace reparse path denied")
        target.mkdir(parents=True, exist_ok=True)
        created.append(relative)
    return created


if __name__ == "__main__":
    print(json.dumps({"prepared_directories": prepare(Path(__file__).resolve().parents[1])}))
