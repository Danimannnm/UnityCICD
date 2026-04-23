# UnityCICD

CI/CD pipeline for a Unity Android project using GitHub Actions.

Produces signed APK + AAB on every push to `main` and via manual dispatch,
uploads them as artifacts, and notifies Slack.

See [`guideline.md`](./guideline.md) for architecture, requirements, and phase plan.

Full setup instructions land in Phase 8.
