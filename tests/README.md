# Tests

Planned test layout:

- core unit tests using synthetic objects and file trees;
- serialization and path-safety tests;
- adapter integration tests in a separately prepared, non-production Windows environment.

There is no application test suite yet. The current offline design-package validator is [scripts/audit_quality.py](../scripts/audit_quality.py), with synthetic contract examples and negative schema cases. Future unit, integration, resilience, OPC UA, Snap7 and Playwright tests follow [the test strategy](../docs/07_TEST_STRATEGY.md) and [the implementation plan](../docs/08_IMPLEMENTATION_PLAN.md). Never include customer TIA projects or credentials in fixtures.
