# API releases

The API and frontend have independent versions and tags. Release Please uses the `simple` strategy to update `version.txt`, `.release-please-manifest.json` and `CHANGELOG.md`. `version.txt` is release metadata, not an override of .NET assembly versions or Docker image tags.

## First release

No API release has previously been published. `0.0.0` is an unpublished bootstrap marker; the first feature release is expected to be `0.1.0`. The bootstrap commit `36a04fdb52708c6a82cc627adeaf1e407272e379` excludes earlier history while including the already-deployed document deletion change. Do not tag `0.0.0` or invent historical release dates. The bootstrap SHA only applies before the first published release.

## Automated preparation

1. Allow GitHub Actions to create pull requests in this API repository's Settings → Actions → General. This setting is independent from the frontend repository.
2. Use Conventional Commit titles and squash-merge normal PRs. `feat:` increments the minor version; `fix:` increments the patch version. Breaking changes increment minor versions during `0.x`, and major versions after `1.0`. Hidden maintenance commits do not independently request a release.
3. On pushes to `main`, Release Please opens or updates a release PR. Manual retry is available through Actions → Release Please → Run workflow on `main`.
4. The workflow uses `GITHUB_TOKEN` and explicitly dispatches CI on the generated release branch because bot-created PRs do not automatically trigger PR workflows. No personal token or production secret is required. Empty PR outputs are handled safely.
5. Review the proposed version and notes. Move applicable `[Unreleased]` entries into the generated version section, remove duplicates and retain deployment caveats. Finish editorial changes after the last feature merge, as the bot can regenerate notes.

CI checks release metadata, restores/builds the .NET 10 solution, runs the API tests and publishes the API into a temporary directory. PostgreSQL/RabbitMQ integration tests remain skipped unless their documented `WIDA_QUEUE_TEST_POSTGRES` and `WIDA_QUEUE_TEST_RABBITMQ` test-service environment variables are configured. CI never uses the production services.

## Validate, tag and publish

Release Please prepares PRs only (`skip-github-release: true`). After CI passes, merge the reviewed release PR under the repository's normal protections. Wait for main CI on that exact merge commit.

```sh
git fetch origin main --tags
node scripts/check-release.mjs
node scripts/release-notes.mjs vX.Y.Z
git tag -a vX.Y.Z <validated-release-merge-commit> -m "Wida API vX.Y.Z"
git push origin vX.Y.Z
```

The tag workflow reruns CI, requires a commit on main, verifies the tag against `version.txt`, extracts only that version's dated changelog entry and publishes a GitHub Release. It then removes the matching release PR's `autorelease: pending` label so preparation can continue. The unpublished `0.0.0` marker has no dated notes and cannot pass publication.

Keep published tags immutable. If publication or label cleanup fails, inspect logs and rerun: existing published releases are reused, while existing drafts require explicit attention. If tagging a later commit that supersedes an unpublished release PR, reconcile that older PR's pending label only after successful publication.

## Deployment

A GitHub Release does not itself deploy the API. Render's existing Git integration can deploy when main changes. Verify the deployed commit and `/healthz`, and perform appropriate authenticated smoke tests before claiming an endpoint works in production. Document skipped checks honestly. Deploy backward-compatible API changes before dependent frontend features.

No migration, authentication configuration, production credentials or Render settings are changed by this release setup. Keep the previous successful Render deployment for rollback. Publish fixes under a new version instead of moving an existing tag.
