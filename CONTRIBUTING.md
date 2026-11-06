# Contributing to Iris

Create a branch from `main` for each fix or feature. If you do not have write access, fork the repository and create the branch in your fork.

```powershell
git switch main
git pull --ff-only
git switch -c feature/your-change
./build.ps1
```

Commit your changes and open a pull request against `main`. GitHub Actions builds both Windows packages and runs the app's self-test for each pull request. Use English for pull request titles and descriptions. Keep personal Iris data, credentials, local build outputs, and screenshots out of commits.

Maintainers merge reviewed changes into `main` and use the [release workflow](RELEASING.md) to publish a stable version.
