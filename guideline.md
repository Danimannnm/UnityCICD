# Unity Android CI/CD Pipeline — Implementation Guideline

> Target executor: **Claude Sonnet** (coding agent)
> Project owner: Adnan
> Date created: 2026-04-23

---

## 1. Objective

Design and implement a production-grade CI/CD pipeline for a Unity project using **GitHub Actions** that:

1. Triggers on push to `main` and via manual `workflow_dispatch`.
2. Builds a Unity project targeting **Android**, producing both **APK** and **AAB** artifacts.
3. Uploads artifacts to GitHub Actions artifact storage.
4. Sends a Slack notification on successful build (and, as a bonus, on failure).
5. Handles all sensitive credentials via **GitHub Secrets**.

This file is the single source of truth for requirements, architecture, and phase breakdown. Sonnet MUST read this file before starting any phase.

---

## 1a. Prerequisites (owner-owned, one-time setup)

These are set up by Adnan **locally** before / alongside phase work. Sonnet does not own these.

| # | Item | How |
|---|---|---|
| P1 | Unity Hub installed | https://unity.com/download |
| P2 | Unity Editor **2022.3.62f3 LTS** + **Android Build Support** module (incl. OpenJDK, Android SDK & NDK) | via Unity Hub |
| P3 | Unity ID + Personal license activated | sign in to Hub; `.ulf` written to `C:\ProgramData\Unity\Unity_lic.ulf` |
| P4 | GitHub repo created (public, named `UnityCICD`) | github.com |
| P5 | GitHub CLI (`gh`) authenticated | `gh auth login` |
| P6 | Slack workspace + Incoming Webhook URL | Slack → Apps → Incoming Webhooks |
| P7 | Android debug keystore generated | `keytool` command (provided in Phase 4) |

Secrets derived from prerequisites — set via `gh secret set <NAME>` in Phase 4+:

| Secret | Source |
|---|---|
| `UNITY_LICENSE` | contents of `Unity_lic.ulf` |
| `ANDROID_KEYSTORE` | `base64 -w0 debug.keystore` |
| `ANDROID_KEYSTORE_PASS` | password chosen during `keytool -genkey` |
| `ANDROID_KEY_ALIAS` | alias name chosen during `keytool -genkey` |
| `ANDROID_KEY_ALIAS_PASS` | key password chosen during `keytool -genkey` |
| `SLACK_WEBHOOK_URL` | from P6 |

## 2. Hard Requirements (non-negotiable)

| # | Requirement | Source |
|---|---|---|
| R1 | Runner: `ubuntu-latest` | Spec |
| R2 | Unity version pinned explicitly (default: `2022.3.62f3` — LTS) | Spec |
| R3 | Build runs in **batchmode** / headless | Spec |
| R4 | Outputs written to `Builds/Android/` (APK + AAB) | Spec |
| R5 | Artifacts uploaded via `actions/upload-artifact@v4` | Spec |
| R6 | Slack message includes: repo name, branch, commit SHA, link to workflow run (artifact link) | Spec |
| R7 | No plaintext secrets anywhere in the repo, including workflow env blocks | Spec |
| R8 | Secrets used: `UNITY_LICENSE`, `ANDROID_KEYSTORE`, `ANDROID_KEYSTORE_PASS`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_ALIAS_PASS`, `SLACK_WEBHOOK_URL` | Spec |
| R9 | Workflow file location: `.github/workflows/` | Spec |
| R10 | README documenting architecture + setup | Spec |

### Bonus (scored)
- **B1** — Library cache (`actions/cache`) keyed on `Assets` + `Packages` + `ProjectSettings` hashes.
- **B2** — Graceful error handling: failure notification to Slack, build log upload on failure.
- **B3** — Extensibility: parameterized workflow inputs (build target, version name, build number) via `workflow_dispatch`.

---

## 3. Architecture Overview

```
┌─────────────────┐     push:main / manual dispatch     ┌──────────────────────────┐
│ Developer / Git │ ──────────────────────────────────▶ │ GitHub Actions Runner    │
└─────────────────┘                                     │ (ubuntu-latest)          │
                                                        │                          │
                                                        │  1. checkout             │
                                                        │  2. restore Library/     │
                                                        │  3. decode keystore      │
                                                        │  4. unity-builder        │
                                                        │     ├─ build APK         │
                                                        │     └─ build AAB         │
                                                        │  5. upload-artifact      │
                                                        │  6. slack notify         │
                                                        └──────────┬───────────────┘
                                                                   │
                                          ┌────────────────────────┼───────────────────────┐
                                          ▼                        ▼                       ▼
                                  ┌──────────────┐         ┌──────────────┐         ┌─────────────┐
                                  │ GH Artifacts │         │ Slack Channel│         │ Cache Store │
                                  │ (APK + AAB)  │         │ (webhook)    │         │ (Library/)  │
                                  └──────────────┘         └──────────────┘         └─────────────┘
```

### Tooling choices
- **`game-ci/unity-builder@v4`** — industry-standard GitHub Action for Unity. Handles Unity licensing + Android SDK + batchmode invocation.
- **`game-ci/unity-activate@v2`** — (only if personal license route is used).
- **`actions/upload-artifact@v4`** — current stable; v3 is deprecated.
- **`actions/cache@v4`** — cache Unity `Library/` folder.
- **`slackapi/slack-github-action@v1.27.0`** — official Slack GH Action (webhook mode, block kit payload).

### Build strategy
Two Unity invocations in a **matrix** (one per target type) OR a **single invocation with a custom build script** that produces both APK + AAB sequentially. We'll default to **matrix** (cleaner logs, parallel builds, independent artifacts). Job names: `build-apk`, `build-aab`.

---

## 4. Repository Layout (target end state)

```
UnityCICD/
├── .github/
│   └── workflows/
│       └── android-build.yml        # main CI workflow
├── Assets/                          # Unity assets (minimal scaffold)
├── Packages/                        # Unity package manifest
├── ProjectSettings/                 # Unity project settings (incl. ProjectVersion.txt)
├── Editor/
│   └── BuildScript.cs               # custom build method invoked by unity-builder
├── Builds/                          # .gitignored; runtime output dir
├── .gitignore                       # Unity-flavored
├── README.md                        # setup + architecture + screenshots
├── guideline.md                     # this file
└── LICENSE                          # optional
```

---

## 5. Secret Contract

All secrets are consumed via `${{ secrets.NAME }}` — **never** echoed, never written to logs.

| Secret | Purpose | Format |
|---|---|---|
| `UNITY_LICENSE` | Unity Personal license (`Unity_v*.ulf` file contents) | XML text |
| `ANDROID_KEYSTORE` | Signing keystore | **Base64-encoded** `.keystore` file |
| `ANDROID_KEYSTORE_PASS` | Keystore password | plain |
| `ANDROID_KEY_ALIAS` | Key alias inside keystore | plain |
| `ANDROID_KEY_ALIAS_PASS` | Key alias password | plain |
| `SLACK_WEBHOOK_URL` | Incoming webhook URL | `https://hooks.slack.com/...` |

Keystore handling: decode once at the start of the job into `android.keystore` in a runner-local path (NOT the repo), pass path via `ANDROID_KEYSTORE_NAME` / `ANDROID_KEYSTORE_BASE64` inputs to `unity-builder`.

---

## 6. Phase Breakdown

Each phase is designed to be:
- **Independently verifiable** (we test & commit at the end).
- **Small enough** to review in one sitting.
- **Ordered** — later phases depend on earlier ones.

| Phase | Title | Goal | Deliverable |
|---|---|---|---|
| 1 | Unity project scaffold | Add repo hygiene (`.gitignore`, stub README) + validate the Unity project Adnan generated via Hub | `.gitignore`, stub `README.md`, validation report |
| 2 | Build script | Write `Editor/BuildScript.cs` with `BuildAndroid()` methods for APK and AAB | `Editor/BuildScript.cs` |
| 3 | Core workflow (unsigned) | Workflow that builds APK + AAB in batchmode without signing | `.github/workflows/android-build.yml` (v0) |
| 4 | Signing + secrets wiring | Inject keystore + passwords into builds | Updated workflow |
| 5 | Artifact upload | Structured artifact upload (APK + AAB, separate or grouped) | Updated workflow |
| 6 | Slack notifications | Success notify (+ failure notify as bonus) with rich formatting | Updated workflow |
| 7 | Caching + polish | Library cache, workflow_dispatch inputs, failure log upload | Updated workflow |
| 8 | README + submission | Final README, setup instructions, design-decisions section, screenshot placeholders | `README.md` |

### Verification loop (each phase)
1. Sonnet implements phase.
2. Owner reviews diff.
3. Owner runs / triggers workflow where applicable.
4. Owner commits with a message in the format: `phase(N): <short description>`.

---

## 7. Workflow Skeleton (reference — Sonnet refines)

```yaml
name: Android Build

on:
  push:
    branches: [main]
  workflow_dispatch:
    inputs:
      version_name:
        description: "Version name (e.g. 1.0.0)"
        required: false
        default: "1.0.0"
      build_number:
        description: "Build number"
        required: false
        default: "1"

jobs:
  build:
    name: Build ${{ matrix.targetType }}
    runs-on: ubuntu-latest
    strategy:
      fail-fast: false
      matrix:
        targetType: [Android]        # could extend later
        buildFormat: [APK, AAB]
    steps:
      - uses: actions/checkout@v4
        with: { lfs: true }

      - uses: actions/cache@v4
        with:
          path: Library
          key: Library-${{ matrix.targetType }}-${{ hashFiles('Assets/**', 'Packages/**', 'ProjectSettings/**') }}
          restore-keys: |
            Library-${{ matrix.targetType }}-
            Library-

      - name: Decode keystore
        run: echo "${{ secrets.ANDROID_KEYSTORE }}" | base64 -d > $RUNNER_TEMP/user.keystore

      - name: Build with Unity
        uses: game-ci/unity-builder@v4
        env:
          UNITY_LICENSE: ${{ secrets.UNITY_LICENSE }}
          ANDROID_KEYSTORE_BASE64: ${{ secrets.ANDROID_KEYSTORE }}
          ANDROID_KEYSTORE_PASS: ${{ secrets.ANDROID_KEYSTORE_PASS }}
          ANDROID_KEYALIAS_NAME: ${{ secrets.ANDROID_KEY_ALIAS }}
          ANDROID_KEYALIAS_PASS: ${{ secrets.ANDROID_KEY_ALIAS_PASS }}
        with:
          unityVersion: 2022.3.62f3
          targetPlatform: Android
          androidAppBundle: ${{ matrix.buildFormat == 'AAB' }}
          buildMethod: BuildScript.BuildAndroid
          buildsPath: Builds

      - uses: actions/upload-artifact@v4
        with:
          name: android-${{ matrix.buildFormat }}
          path: Builds/Android/
          retention-days: 14
          if-no-files-found: error

  notify:
    needs: build
    if: always()
    runs-on: ubuntu-latest
    steps:
      - name: Slack notify
        uses: slackapi/slack-github-action@v1.27.0
        with:
          webhook-type: incoming-webhook
          webhook: ${{ secrets.SLACK_WEBHOOK_URL }}
          payload: |
            { "blocks": [ ... ] }
```

> The skeleton above is indicative. Phase prompts will instruct Sonnet on the exact final shape.

---

## 8. Definition of Done

- [ ] Workflow triggers on push to `main` AND via manual dispatch.
- [ ] Successful run produces APK + AAB visible in Actions → run → Artifacts.
- [ ] Slack message received with repo, branch, SHA, run URL.
- [ ] No secret value appears in any log line.
- [ ] README explains architecture + how to add the 6 secrets.
- [ ] Screenshot of green run included in README.
- [ ] All commits follow `phase(N): ...` convention.

---

## 9. Out of Scope

- iOS build target.
- Play Store upload / Fastlane.
- Unit test pass (GameCI `unity-test-runner`) — can be added later.
- Multi-environment (dev/staging/prod) configs.

---

## 10. References

- GameCI docs: https://game.ci/docs/github/getting-started
- Unity Command-line arguments: https://docs.unity3d.com/Manual/CommandLineArguments.html
- `actions/upload-artifact` v4 migration notes: https://github.com/actions/upload-artifact
- Slack GH Action: https://github.com/slackapi/slack-github-action
