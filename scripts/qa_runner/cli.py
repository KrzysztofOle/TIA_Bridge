import argparse
from pathlib import Path
import sys

from .adapters import runtime
from .diagnostics import verify_manifest
from .loader import contained, discover, identifier, load_project, load_scenario, read_json
from .reports import compact
from .runner import run


def main(argv=None):
    parser = argparse.ArgumentParser(description="QA Runner v1 — approved offline tasks, JSON scenarios, immutable reports")
    parser.add_argument("--config", type=Path)
    commands = parser.add_subparsers(dest="command", required=True)
    execute = commands.add_parser("run")
    execute.add_argument("profile", nargs="?")
    execute.add_argument("--scenario")
    execute.add_argument("--performance-profile")
    execute.add_argument("--log-level", choices=["TRACE", "DEBUG", "INFO", "WARN", "ERROR"], default="INFO")
    report = commands.add_parser("report")
    target = report.add_mutually_exclusive_group(required=True)
    target.add_argument("--latest", action="store_true")
    target.add_argument("--run-id")
    report.add_argument("--failures-only", action="store_true")
    commands.add_parser("list")
    commands.add_parser("doctor")
    args = parser.parse_args(argv)
    try:
        config = args.config or discover(Path.cwd())
        root, project = load_project(config)
        if args.command == "run":
            if args.scenario and args.profile:
                parser.error("Use a profile or --scenario")
            summary = run(root, project, config, args.profile or ("scenario" if args.scenario else project["default_profile"]), args.scenario,
                          args.performance_profile, args.log_level)
            print(compact(summary))
            return 0 if summary["status"] == "PASS" else 1 if summary["status"] == "FAIL" else 2
        if args.command == "list":
            for name, entries in project["profiles"].items():
                print(name + ": " + ", ".join(entries))
            for path in sorted(contained(root, project["scenario_root"]).glob("*.json")):
                scenario = load_scenario(path, root, project)
                print(scenario["id"] + ": " + scenario["description"])
            return 0
        if args.command == "doctor":
            good = sys.version_info >= (3, 12)
            print("CONFIG: " + str(config.resolve()))
            print("RUNNER PYTHON: " + sys.version.split()[0])
            for name, definition in project["runtimes"].items():
                try:
                    print(name + ": " + runtime(definition))
                except OSError as error:
                    print(name + ": BLOCKED — " + str(error)); good = False
            for entries in project["profiles"].values():
                for entry in entries:
                    load_scenario(contained(contained(root, project["scenario_root"]), entry), root, project)
            lock = contained(root, project["results_root"]) / ".runner.lock"
            if lock.exists():
                print("BLOCKED: active/stale " + str(lock) + "; confirm owner stopped before manually removing lock")
                good = False
            print("OFFLINE ONLY; Engineering/Runtime authorization: NOT_RUN")
            return 0 if good else 2
        results = contained(root, project["results_root"])
        if args.run_id:
            identifier(args.run_id)
            folder = contained(results, args.run_id)
        else:
            paths = sorted(results.glob("*/summary.json"), key=lambda p: p.parent.name)
            if not paths:
                raise ValueError("No completed reports; run qa run fast first. Interrupted hard kills may leave logs only.")
            folder = contained(results, paths[-1].parent.name)
        manifest = verify_manifest(folder)
        summary = read_json(folder / "summary.json", 16 * 1024 * 1024)
        if summary["run_id"] != folder.name or manifest.get("run_id") != summary["run_id"]:
            raise ValueError("Report Run ID mismatch")
        print(compact(summary, args.failures_only))
        return 0
    except (OSError, ValueError, KeyError, TypeError) as error:
        print("QA BLOCKED: " + str(error), file=sys.stderr)
        return 2
