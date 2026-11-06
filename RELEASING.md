# Publishing an Iris release

1. Set the three-part version in `app/source/ReleaseVersion.cs` on a branch. For example, use `2.0.0` for Iris 2.0. Update `payload-files.txt` if the package contents change.
2. Open a pull request, wait for the Windows build and self-test, then merge it into `main`. Use English titles for changes that should appear in the generated release notes.
3. Tag the merged commit and push the tag from the official repository:

   ```powershell
   git switch main
   git pull --ff-only
   git tag -a v2.0.0 -m "Iris 2.0.0"
   git push origin v2.0.0
   ```

The [release workflow](.github/workflows/iris.yml) checks that the tag matches the app version and points to a commit on `main`. It builds the setup and portable ZIP on Windows, runs the self-test, and publishes both on the matching GitHub release. GitHub provides the SHA-256 asset digests Iris uses to verify downloads. Branch builds and manual workflow runs produce downloadable artifacts for testing.

Release filenames include all three version numbers, for example `Iris-2.0.1-Setup.exe` and `Iris-2.0.1-Portable.zip`. The workflow also attaches identical copies with the two-part names for in-app updates from Iris 1.0.0 and 1.0.1. Publish updates under a new tag. The original `v1.0.0` release predates this workflow.
