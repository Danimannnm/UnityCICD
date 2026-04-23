# Unity Android CI/CD Pipeline

A GitHub Actions pipeline for the [Danimannnm/UnityCICD](https://github.com/Danimannnm/UnityCICD)
repository that builds a Unity **2022.3.62f3 LTS** project for Android, producing a signed **APK**
and **AAB** on every push to `main` and on demand via `workflow_dispatch`. Both formats run as
parallel matrix legs on an `ubuntu-latest` runner using `game-ci/unity-builder@v4`. Artifacts are
uploaded to GitHub Actions storage with 14-day retention, the Unity `Library/` import cache is
preserved between runs, build logs are captured on failure, and a Slack Block Kit notification is
sent on both success and failure.

---

## Architecture

```
push:main  /  workflow_dispatch
           │
           ▼
┌──────────────────────────────────────────────────────┐
│  GitHub Actions Runner  (ubuntu-latest)              │
│                                                      │
│  build job — strategy: matrix [APK, AAB]             │
│  ┌────────────────────────────────────────────────┐  │
│  │  Each leg runs concurrently                    │  │
│  │  1. checkout  (lfs, full history)              │  │
│  │  2. free disk space  (~15 GB reclaimed)        │  │
│  │  3. restore Library/ cache  (per buildFormat)  │  │
│  │  4. game-ci/unity-builder@v4  → Builds/Android/│  │
│  │  5a. upload-artifact  (on success, 14 days)    │  │
│  │  5b. upload build logs  (on failure, 7 days)   │  │
│  └────────────────────────────────────────────────┘  │
│                                                      │
│  notify job  (needs: build, always())                │
└──────────────────────────┬───────────────────────────┘
                           │
           ┌───────────────┼────────────────┐
           ▼               ▼                ▼
    GH Artifacts      Slack Channel     Cache Store
    (APK + AAB)       (Block Kit        (Library/
                       webhook)          per format)
```

---

## Pipeline Stages

- **checkout** — full clone with LFS and complete history (`fetch-depth: 0`).
- **free disk space** — removes unused toolchains (dotnet, NDK, GHC) to reclaim ~15 GB for Unity and the Android SDK.
- **cache restore** — restores `Library/` keyed on `Assets/**`, `Packages/**`, `ProjectSettings/**` hashes, scoped per `buildFormat`; falls back to partial keys.
- **create Unity cache dir** — creates a workspace-local directory for Unity's import cache to avoid container root permission errors.
- **unity-builder** — invokes `game-ci/unity-builder@v4` in batchmode; calls `BuildScript.BuildAndroid`; signs the output with the project keystore via `with:` inputs.
- **list build outputs** — `ls -la Builds/` for diagnostics; always runs, never fails the job.
- **upload artifact** — uploads `Builds/Android/` as `android-APK` or `android-AAB`; fails the step if no files are found.
- **upload build logs (failure only)** — uploads `Builds/` and any `.log` files as `build-logs-{fmt}` for post-mortem diagnosis.
- **notify** — separate job that runs `always()`; determines pass/fail, fetches the commit message via the GitHub API, and sends a Slack Block Kit message containing repo, branch, short SHA, actor, event, status, and a deep link to the Actions run.

---

## Setup

### Prerequisites

| # | Item | Details |
|---|---|---|
| P1 | Unity Hub | [unity.com/download](https://unity.com/download) |
| P2 | Unity Editor **2022.3.62f3 LTS** + Android Build Support | Install via Unity Hub; module includes OpenJDK, Android SDK & NDK |
| P3 | Unity Personal license activated | Sign in to Hub; license file written to `C:\ProgramData\Unity\Unity_lic.ulf` (Windows) |
| P4 | GitHub repo (`UnityCICD`, public) | [github.com](https://github.com) |
| P5 | GitHub CLI (`gh`) authenticated | `gh auth login` |
| P6 | Slack workspace + Incoming Webhook URL | Slack → Apps → Incoming Webhooks → Add to Workspace |
| P7 | JDK available for `keytool` | Included with Unity Android Build Support (OpenJDK) |

### Secrets

Six secrets must be configured in **Settings → Secrets and variables → Actions** before the
workflow can produce signed artifacts.

#### Step 1 — Generate the Android keystore (one-time)

```bash
keytool -genkey -v \
  -keystore debug.keystore \
  -alias androiddebugkey \
  -keyalg RSA -keysize 2048 \
  -validity 10000 \
  -storepass your-storepass \
  -keypass your-keypass \
  -dname "CN=Android Debug,O=Android,C=US"
```

Replace `your-storepass` and `your-keypass` with strong passwords of your choice. Record both
values and the alias name — they become three of the six secrets below.

#### Step 2 — Set all six secrets

| Secret | Purpose | How to obtain the value | `gh secret set` command |
|---|---|---|---|
| `UNITY_LICENSE` | Unity Personal license (XML) | Contents of `Unity_lic.ulf` | `gh secret set UNITY_LICENSE < Unity_lic.ulf` |
| `ANDROID_KEYSTORE` | Signing keystore (Base64-encoded) | `base64 -w0 debug.keystore` (Linux / Git Bash) | `gh secret set ANDROID_KEYSTORE --body "$(base64 -w0 debug.keystore)"` |
| `ANDROID_KEYSTORE_PASS` | Keystore password | The `-storepass` value from `keytool` | `gh secret set ANDROID_KEYSTORE_PASS --body "your-storepass"` |
| `ANDROID_KEY_ALIAS` | Key alias inside the keystore | The `-alias` value from `keytool` | `gh secret set ANDROID_KEY_ALIAS --body "androiddebugkey"` |
| `ANDROID_KEY_ALIAS_PASS` | Key alias password | The `-keypass` value from `keytool` | `gh secret set ANDROID_KEY_ALIAS_PASS --body "your-keypass"` |
| `SLACK_WEBHOOK_URL` | Incoming Webhook URL | Slack App → Incoming Webhooks | `gh secret set SLACK_WEBHOOK_URL --body "https://hooks.slack.com/services/..."` |

> `UNITY_EMAIL` and `UNITY_PASSWORD` appear as optional env vars in the workflow and are supported
> by `unity-builder` as an alternative activation method. This repo uses `UNITY_LICENSE`
> (file-based activation); `UNITY_EMAIL` / `UNITY_PASSWORD` do not need to be set.

---

## Manual Trigger

Trigger a run from the command line:

```bash
# Build both APK and AAB (default)
gh workflow run android-build.yml \
  --ref main \
  -f build_target=both \
  -f version_name=1.2.0 \
  -f build_number=42

# Build APK only
gh workflow run android-build.yml \
  --ref main \
  -f build_target=APK \
  -f version_name=1.0.0 \
  -f build_number=1
```

| Input | Type | Default | Accepted values |
|---|---|---|---|
| `build_target` | choice | `both` | `both`, `APK`, `AAB` |
| `version_name` | string | `1.0.0` | Any semver string |
| `build_number` | string | `1` | Any integer |

`build_target` dynamically filters the matrix: choosing `APK` or `AAB` spawns a single leg;
`both` spawns two parallel legs.

---

## Design Decisions

- **Matrix over single-invocation dual build.** Each format (`APK`, `AAB`) runs as an independent
  matrix leg with `fail-fast: false`. Logs are separated, artifacts are independent, and the two
  legs run in parallel. A signing failure in one leg does not cancel the other.

- **`game-ci/unity-builder@v4` over a raw Unity shell invocation.** GameCI handles Unity license
  activation, Docker image selection (matching the requested Unity version), Android SDK wiring,
  and batchmode invocation. Replicating this from scratch requires several hundred lines of shell
  and version-specific branching.

- **Keystore passed via `with:` inputs, not `env:`.** `unity-builder` runs inside a Docker
  container; host-level `env:` variables are not forwarded into the container, whereas `with:`
  inputs are mapped to container `--env` flags by the action itself. Passing secrets through `env:`
  on the host was the root cause of signing failures before the Phase 4 fix (commit `a9d1018`).

- **`slackapi/slack-github-action@v2.0.0` over `@v1.27.0`.** Version 2 accepts `webhook` and
  `webhook-type` as `with:` inputs rather than environment variables, applying the same
  container-boundary principle and making the secret reference explicit in the action config.

- **Library/ cache key scoped per `matrix.buildFormat`.** APK and AAB export paths can diverge in
  their import cache state when the `androidExportType` changes how Unity processes certain assets.
  Separate keys prevent one leg from poisoning or restoring a mismatched cache for the other.

- **`fail-fast: false` in the matrix.** An APK signing failure (for example) should not cancel
  the AAB leg — both artifacts are independently useful for diagnosis and distribution.

- **Concurrency group cancels stale push runs.** Rapid successive pushes to `main` would otherwise
  queue multiple full builds. `cancel-in-progress: true` keeps only the latest run active, saving
  runner minutes without affecting pull-request or dispatch runs.

---


## Out of Scope

- iOS build target.
- Play Store upload or Fastlane integration.
- Unity test runner (`game-ci/unity-test-runner`) — can be layered on top of this pipeline.
- Multi-environment (dev / staging / prod) configuration.

---

## References

| Resource | URL |
|---|---|
| GameCI — GitHub Actions getting started | https://game.ci/docs/github/getting-started |
| Unity command-line arguments | https://docs.unity3d.com/Manual/CommandLineArguments.html |
| `actions/upload-artifact` v4 migration notes | https://github.com/actions/upload-artifact |
| `slackapi/slack-github-action` | https://github.com/slackapi/slack-github-action |
| `actions/cache` | https://github.com/actions/cache |
| GitHub Actions — encrypted secrets | https://docs.github.com/en/actions/security-guides/encrypted-secrets |
