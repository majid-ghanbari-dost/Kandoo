# Continuous Integration

## Where to read the result

The repository's GitHub **Actions** tab contains the `CI` workflow. A push to the Arena branch or a pull request starts one `build-and-test` job for each of `ubuntu-latest` and `windows-latest`. Each job restores the solution, builds it in Release mode, runs the xUnit tests, writes a TRX file, and uploads the result as an artifact.

The CI result is the verification evidence for the .NET solution. A local .NET installation is not required to interpret the workflow result.

## Status meanings

- **Green / success:** Every matrix job completed successfully, including restore, build, and tests.
- **Red / failure:** At least one job failed. Open the failing job and read the first failed step; a test failure is not a successful verification even if another operating system is green.
- **Yellow / in progress or queued:** GitHub is still scheduling or running the workflow. Wait for a terminal status before treating the result as evidence.

## Test-results artifacts

1. Open the workflow run in **Actions → CI**.
2. Select the run associated with the pushed commit SHA.
3. Scroll to **Artifacts** and download `test-results-ubuntu-latest` or `test-results-windows-latest`.
4. Extract the archive and open the `.trx` file. The TRX XML contains the test outcome, test names, durations, and failure messages. It can be opened in Visual Studio's Test Results window or inspected as XML.

The upload step runs even when a preceding test step fails, so a red run should still provide the available TRX diagnostics. The artifact is diagnostic evidence; it does not replace the green workflow status.
