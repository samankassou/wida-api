# Changelog

User-visible API changes. API releases are versioned independently from the frontend. This repository has no previously published release history; `0.0.0` is only a bootstrap marker, not a historical release.

## 0.1.0 (2026-09-19)


### Added

* **documents:** add owner-scoped document deletion ([7d88474](https://github.com/samankassou/wida-api/commit/7d884743372692bee3d4c8eaa877d496842e4e93))
* **release:** automate API changelogs and release preparation ([e89fdfe](https://github.com/samankassou/wida-api/commit/e89fdfe01cc86caafae054c9dfdf04641182ea38))

## [Unreleased]

### Added

- Delete an owned document through `DELETE /api/documents/{id}`, including its invoice, invoice lines, processing runs and extracted fields.
- Return 404 for missing or other users' documents, and 409 while analysis is pending or running. Serialize deletion with upload and queue admission.

### Maintenance

- Prepare version and changelog updates through Release Please, and verify .NET builds, tests and publication output in CI.

### Deployment

- Document deletion requires no new migration or environment variable. Analysis credits already consumed are not refunded.
- Original-file deletion follows database commit; failed storage cleanup is logged for operator follow-up.
- The first release records changes since commit `36a04fd`, immediately before document deletion. It does not reconstruct older API history.
