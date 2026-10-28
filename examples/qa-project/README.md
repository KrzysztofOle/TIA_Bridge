# Independent QA project

Copy this directory outside TIA_Bridge, then copy [qa.py](../../scripts/qa.py) and
the [qa_runner package](../../scripts/qa_runner/__init__.py) into its `tools/` directory.
Run `python tools/qa.py run fast` from that project. Python 3.12+ and its standard
library suffice; no Siemens, .NET, TIA_Bridge source, pip or environment activation.
Add `.qa-results/` to that project's ignore rules. Full integration instructions:
[QA Runner](../../docs/QA_RUNNER.md).
