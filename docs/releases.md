# API releases

The API and frontend have independent versions and tags. Release Please uses the `simple` strategy to update `version.txt`, `.release-please-manifest.json` and `CHANGELOG.md`. `version.txt` is release metadata, not an override of .NET assembly versions or Docker image tags.

## First release

The API release history starts at `0.1.0`. `0.0.0` was an unpublished bootstrap marker. The bootstrap commit `36a04fdb52708c6a82cc627adeaf1e407272e379` excludes earlier history while including the already-deployed document deletion change. Do not tag `0.0.0` or invent historical release dates. The bootstrap SHA only applies before the first published release.

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

Production follows the `production` branch. Feature and release PRs still target `main`; merging them does not deploy production once the hosting settings below are configured. After tag CI and GitHub publication succeed, the Release workflow advances `production` to the exact tagged commit. Render then builds that commit. Verify the deployed commit and `/healthz`, and perform appropriate authenticated smoke tests before claiming an endpoint works in production. Deploy backward-compatible API releases before dependent frontend releases.

This workflow does not change migrations, authentication configuration or production credentials. Keep the previous successful Render deployment for rollback. Publish fixes under a new version instead of moving an existing tag.

## Production branch setup

The `production` branch is a deployment pointer, not an integration branch. Do not merge PRs into it, push feature commits to it, or delete it during stale-branch cleanup. It starts at the last published release. Keep `main` as the repository default branch. Allow the release workflow to update `production`; do not require PR-only updates for that branch unless the workflow has an appropriate bypass.

Set Render → wida-api → Settings → Branch to `production`, with Auto-Deploy set to On Commit. The checked-in Blueprint declares the same branch; manually created services must be updated in the dashboard. Until that setting is changed, merges to `main` can still deploy.

Promotion runs in the existing tag workflow after publication, not a separate `release: published` workflow: events created with `GITHUB_TOKEN` do not start another ordinary Actions workflow. The existing hosting Git integration handles the branch update, so no hosting API token or deploy-hook secret is needed.

Promotions are serialized and fast-forward only. A retry at the current production commit is a no-op; an older or divergent release fails instead of rolling production backward. If publication succeeds but promotion fails, rerun the failed job. If the host build fails after promotion, retry that exact commit in the host dashboard. For an emergency rollback, restore a previous successful deployment in the host, then publish a new patch release containing the fix or revert; never force-push production or move release tags. Manual hosting deployments and rollbacks remain operator overrides.

For the first release after this setup, confirm the workflow's promotion succeeds and the hosting deployment is triggered for the same SHA. The workflow reports branch promotion only; host build completion and application checks must be verified separately.
