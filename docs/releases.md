# API releases with GitFlow

The API and frontend have independent versions. Production deploys from published release tags, not ordinary PR merges. GitFlow uses `develop` for integration and `main` for release history.

## Branches and merge rules

- `feature/*` starts from `develop` and returns to `develop` through a PR. Use Conventional Commit titles (`feat:`, `fix:`) and squash feature PRs.
- `release/*` starts from `develop` when the release scope is frozen. Only stabilization fixes and release metadata belong here; new features continue on `develop`.
- `hotfix/*` starts from `main` for urgent production fixes.
- Merge a completed release or hotfix into `main` with **Create a merge commit**, not squash or rebase. After publication, merge `main` back into `develop` with a merge commit, and into any active release branch as needed. This preserves ancestry and carries fixes, version files and changelog updates forward.
- Keep `main` and `develop` permanently. Delete feature/release/hotfix branches only after their work is merged back. There is no `production` branch.

Protect both long-lived branches with CI and reviews appropriate to the team. Ensure merge commits are enabled for release and back-merge PRs; a linear-history rule conflicts with this GitFlow history. The repository can retain `main` as its default branch; always select `develop` explicitly for feature PRs.

## Prepare a release

1. Finish feature PRs into `develop`, then create a release branch (for example `release/next`) from its tested tip and push it. Keep only one normal release in preparation at a time.
2. The Release Please workflow runs on `release/*` and `hotfix/*`, with that branch as its target. It opens a metadata PR **into the release/hotfix branch**, not into `develop` or `main`. It does not run on ordinary pushes to either long-lived branch.
3. Review the generated version and dated changelog. Conventional `feat:` and `fix:` commits determine the version; branch names do not set it. Add compatibility notes and move relevant Unreleased entries into the version section. Merge the metadata PR into the release branch once its CI passes. Do not manually publish or tag the metadata PR commit.
4. Stabilize and test the release branch, then open its PR into `main`. Finish stabilization before merging the metadata PR when possible. If fixes are added afterward, update that version’s changelog on the release branch before finishing; a merged metadata PR remains pending until publication.
5. Merge the release PR into `main` with a merge commit. Wait for CI on that exact merge commit. Do not merge the next release into `main` until this one's deployment is complete.

Release Please uses `GITHUB_TOKEN` and `skip-github-release: true`. Its generated metadata PR explicitly dispatches CI because token-created PRs do not trigger ordinary PR workflows. Manual preparation is available from Actions → Release Please → Run workflow, selecting the release/hotfix branch. Enable GitHub Actions PR creation in repository settings.

## Publish and deploy

```sh
git fetch origin main --tags
# Use the exact release merge SHA whose CI passed, not an arbitrary later main commit.
git tag -a vX.Y.Z <validated-release-merge-sha> -m "Wida API vX.Y.Z"
git push origin vX.Y.Z
```

The tag workflow reruns CI, verifies main ancestry, validates the version and dated changelog, and publishes the GitHub Release. It clears pending labels on merged Release Please metadata PRs included in the tag. A deployment job then checks the release is published (not draft/prerelease) and that the tagged SHA is still the current main tip before deploying that exact revision. A release event generated with `GITHUB_TOKEN` cannot trigger another ordinary Actions workflow, so deployment is part of the same tag workflow after publication.

Never move published tags. For a transient publication or deployment failure, rerun the failed job for the same tag. Deployment jobs are serialized. An older release whose SHA no longer equals main fails instead of rolling production back. A retry may create another hosting deployment of the same commit. For emergency rollback, restore the previous successful deployment in the host dashboard, then ship a hotfix release; do not force-push main or reuse a tag.

After publishing, open `main` → `develop` and merge it with a merge commit. Merge hotfixes into an active release branch too. This back-merge is required before preparing the next release so Release Please sees the previous version and history.

## Hosting setup

1. Render → wida-api → Settings: keep Branch `main` and set **Auto-Deploy → Off**. The Blueprint also declares `branch: main` and `autoDeployTrigger: off`; manually created services need the dashboard change.
2. Copy the Render service's Deploy Hook URL into the API repository's GitHub Actions secret `RENDER_DEPLOY_HOOK_URL`. Do not commit it or paste it in a PR.
3. The release job calls the hook with `ref=<tagged commit SHA>`. A successful hook response means accepted/queued, not that the service is live. Verify Render's deployed SHA and `/healthz`, then appropriate authenticated smoke tests.

See [Render deploy hooks](https://render.com/docs/deploy-hooks). No database or authentication settings change. The API's PostgreSQL/RabbitMQ integration tests remain skipped unless their documented test-service variables are configured. Release the backward-compatible API before a dependent frontend release.

## Activating this setup

Disable the host's automatic production deploys and add the required repository secrets before publishing the next release. Merge this infrastructure setup into main, then merge main into develop with a merge commit so future feature/release branches inherit it. This one-time setup PR is the exception to normal release-only main merges. Existing published tags contain the old workflow; rerunning them does not use this new deployment job. Verify the next newly published release end to end before declaring automated release deployment operational.
