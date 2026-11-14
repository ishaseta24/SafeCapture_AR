# SafeCapture — Claude Rules

## Git

- **Always ask before committing** to any branch.
- **Always ask before merging** or pushing to any branch other than the one explicitly named.
- Never auto-push to `develop`, `main`, or any feature branch unless the user says so for that specific action.

## Secrets — never read or push

Never read, print, log, or include in any output:
- `MODEL_API_KEY` (Meta AI API key)
- `RELAY_TOKEN`
- Any value from `relay/.env`
- Any value from `Assets/StreamingAssets/config.json` or `persistentDataPath/config.json`

## Files that must NEVER be committed or pushed

Before every `git add` / commit, warn the user if any of these are staged:

| File / pattern | Why |
|---|---|
| `relay/.env` | Contains MODEL_API_KEY and RELAY_TOKEN |
| `relay/.env.*` (except `.env.example`) | Same |
| `relay/prompts.json` | May contain custom prompt text |
| `relay/debug/` | Camera frames — may contain private content |
| `relay/logs/` | Call logs with frame metadata |
| `relay/node_modules/` | Build artifact |
| `Assets/StreamingAssets/config.json` | Contains relayToken for Quest |
| `Assets/XR/Settings/AuthBuildSettings.asset` | NSDK access token |
| `Assets/Samples/VPS2/NsdkCredentials.asset` | NSDK credentials |
| `*.apk` / `*.aab` | Build artifacts |

All of the above are covered by `.gitignore`, but if one ever appears in `git status`, **stop and warn before proceeding**.
