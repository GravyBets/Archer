# Isolated verification — owner setup and operating contract

Archer has an opt-in second check for published draft commits. GitHub CI remains independently required. This runner is Linux-only; it doesn't execute on the Windows desktop. **Provision a dedicated, disposable Linux VM/account with no unrelated personal data, production credentials, host sockets, mounts, or privileged hooks.** Rootless containers reduce risk but are not a VM security boundary; don't use the personal PC or a production service host to execute untrusted code. A dedicated worker/remote transport and production secrets service are future work. The present implementation requires Mission Control to run on the Linux runner host; it inherits no integration environment into Podman and does not mount Archer's data. The Archer process itself still needs its repository and model credentials, so use a separate isolated VM for pilot execution and restrict its credentials.

## Prepare a trusted verifier image

The owner supplies a prebuilt, immutable **local image ID** (sha256 plus 64 lowercase hex characters). The image must contain an executable `/usr/local/bin/archer-verify` that accepts a GitHub gzip tar archive on stdin, extracts inside /work only, and independently builds/tests the repo. No model-provided image or command is accepted. No credentials in image layers, no mounted repositories, no image pulls during execution, no network or package downloads during the run.

The example in sandbox/dotnet uses an owner-selected digest-pinned SDK and an offline NuGet package directory. On a trusted provisioning machine, prepare the package files for the particular project, place the .nupkg files in sandbox/dotnet/packages (not tracked), choose fixed build/test target paths, then build the image. For example, substitute the real SDK digest, application/test targets and local tag for a project you own:

```sh
podman build --pull=never --network=none \
  --build-arg SDK_IMAGE='mcr.microsoft.com/dotnet/sdk@sha256:<verified-digest>' \
  --build-arg BUILD_TARGET='MyApp/MyApp.csproj' \
  --build-arg TEST_TARGET='MyApp.Tests/MyApp.Tests.csproj' \
  -t localhost/archer-myapp-verifier sandbox/dotnet
podman image inspect --format '{{.Id}}' localhost/archer-myapp-verifier
```

The SDK image and packages must be reviewed and preloaded separately. Don't put feeds with credentials into the image. The owner must verify that the selected targets run real, independently maintained tests, including discovery of new tests. The template is not a tested image for any particular real repository or dependency set. Non-.NET stacks can implement the same fixed entrypoint contract with their own trusted offline image.

## Enable for one project

1. Install rootless Podman at /usr/bin/podman, configure subordinate UID/GID ranges and cgroup v2 limits. Validate that the dedicated account has no implicit container mounts (including subscription secret mounts), untrusted OCI hooks or host network access through other mechanisms.
2. In Archer's environment set `ARCHER_SANDBOX_ENABLED=true` and the project's `<credentialPrefix>_SANDBOX_IMAGE=sha256:<local-image-id>`. The prefix is shown in the Managed projects panel. Existing scoped read/write credentials, allowlists and AI budget still apply.
3. Register a project with **Require isolated local Linux build and test verification** enabled. Existing profiles default off. Missing Linux runtime or pinned image ID prevents authorizing this project's missions.
4. Archer publishes a scoped draft as before. It retrieves the exact recorded commit archive (maximum compressed 32 MiB), using the scoped reader only at api.github.com and never forwarding Authorization to codeload.github.com. A local Podman creation request uses a fixed entrypoint, no host bind mounts, no network, no inherited proxy or application secrets, read-only root, nonroot UID/GID, no capabilities, no-new-privileges, bounded tmpfs, 1.5 GiB memory, two CPUs, 128 PIDs and an eight-minute container timeout. Container metadata is checked before starting. No code runs directly on the host. The archive is streamed into the container, never expanded on the host.
5. The entrypoint returns 0 for reported success, 10 for build failure, 20 for test failure, or any other code for infrastructure failure. Only recognized compiler error codes (CS/RZ/TS) are persisted; raw logs and signed archive URLs are not fed to models, stored in memory, or returned to the UI. A failure may consume the existing one permitted repair attempt. A blocked/infrastructure result stops the batch; no fallback to unisolated execution. Cleanup failure blocks the batch; an abandoned named container after an unexpected process/host interruption needs operator inspection. Runtime timeout limits execution even if the Archer process exits.
6. After a successful container verdict, the existing exact-head GitHub workflow must still pass. The verdict records the commit SHA and image ID. Generated code can misrepresent its exit status or attempt to interfere with tests, so sandbox success isn't security assurance, semantic proof, or release authorization. No auto-merge or deployment.

## Tests

DevelopmentBatchChecks exercises fixed arguments, sanitized environment, inspection refusal, bounded archive and redirect policy, and evidence redaction via synthetic responses. The separate isolated-runtime workflow compiles a trusted static fixture from scratch (no image pull), starts it through the actual runner and verifies basic UID, stdin and filesystem constraints, then removes the container. This is a smoke test, not a penetration test or a real managed application build. Real project image provisioning and live pilot must be validated separately. 
