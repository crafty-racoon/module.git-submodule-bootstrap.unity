# Changelog

All notable changes to this package are documented in this file.

## [0.2.0] - 2026-09-01

### Added

- A preflight status check that skips Git updates when all submodules already
  match the parent repository gitlinks.
- A temporary Unity utility window while missing or outdated submodules are
  initialized or updated.

### Changed

- Asset Database auto-refresh is suspended during the Git update and resumed
  before the final refresh, preventing partial imports while checkouts change.

## [0.1.0] - 2026-09-01

### Added

- Automatic once-per-session submodule initialization in the Unity Editor.
- Manual update and automatic-update toggle menu items.
- Non-blocking Git execution, non-interactive credential behavior, diagnostics,
  and Asset Database refresh.

