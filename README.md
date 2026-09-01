# module.git-submodule-bootstrap.unity

Unity Editor integration that checks `git submodule status --recursive` once per
Editor session and runs `git submodule update --init --recursive` only when a
submodule is missing or differs from the adopting repository's gitlink. It keeps
submodule versions under the adopting repository's control: it does not use
`--remote`, rewrite `.gitmodules`, or move gitlinks.

## Features

- Non-blocking update after the Editor opens.
- No update command or progress window when every submodule already matches.
- Temporary progress window while missing or outdated submodules are processed.
- One automatic attempt per Editor session, including across domain reloads.
- Manual update at **Tools > Git Submodules > Update Now**.
- Per-project, per-user automatic-update toggle at
  **Tools > Git Submodules > Update on Project Open**.
- Non-interactive authentication so a background update cannot stall the Editor.
- Console diagnostics and an Asset Database refresh after success.
- No execution in Unity batch mode or player builds.

## Recommended installation

Install a released Git tag through Unity Package Manager so this bootstrap
package does not depend on the submodule operation that it performs:

```json
{
  "dependencies": {
    "com.crafty-racoon.git-submodule-bootstrap":
      "https://github.com/crafty-racoon/module.git-submodule-bootstrap.unity.git#v0.2.0"
  }
}
```

For active package development, the adopting repository may keep the checkout
at `module/module.git-submodule-bootstrap.unity` and use a local dependency:

```json
{
  "dependencies": {
    "com.crafty-racoon.git-submodule-bootstrap":
      "file:../module/module.git-submodule-bootstrap.unity"
  }
}
```

A missing local package cannot initialize itself. Clone development workspaces
with their submodules, run the Git command before opening Unity, or use the
released Git-tag installation for fresh-project bootstrap.

## Requirements and behavior

- Unity 6 or newer.
- `git` must be available on the process `PATH` inherited by Unity.
- Existing credential helpers may provide stored credentials, but the package
  sets `GIT_TERMINAL_PROMPT=0` and `GCM_INTERACTIVE=Never` for automatic safety.
- Submodule repository URLs and commits remain owned by the outer adopting
  repository.

