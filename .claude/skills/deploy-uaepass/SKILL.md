---
name: deploy-uaepass
description: Build, commit, push and deploy the UAE PASS solution (GetOtpAPI + AzureFunctionTest repos) - the GetOtpAPI Playwright API (container app getotpapi-ca) and/or the callbackFunctioncs Function app (tdrauaepasspoc). Use when the user says deploy, redeploy, ship, push to Azure, or "deploy my changes" for GetOtpAPI, GetPasscode, the UAE PASS API, the callback function, or this solution.
---

# Deploy the UAE PASS solution

Invoking this skill is the user's go-ahead to commit, push to `main` and deploy. Don't ask again,
but do show what will be committed before running for real.

## What gets deployed where

| Target | Local repo (under the solution folder) | GitHub | Azure |
|---|---|---|---|
| `Api` | `<solution>\GetOtpAPI` | AhmedMohyOmar/testUAEPASS (public) | Container App `getotpapi-ca`, image in ACR `headlessbrowserdocker` |
| `Function` | `<solution>\AzureFunctionTest` | AhmedMohyOmar/RepoForUbntu (private) | Function App `tdrauaepasspoc` (Flex Consumption, zip deploy) |

Subscription `d4b5c0a7-4891-43ca-b375-efde3ad465ee`, resource group `RG_TDRA_UAE`.

The script finds the solution folder in this order: `-SolutionRoot`, the `UAEPASS_SOLUTION_ROOT`
environment variable, four levels above the script when it runs from
`<solution>\GetOtpAPI\.claude\skills\deploy-uaepass`, then `D:\VS Projects\AzureFunctionTest`.
In the steps below, `<script>` is this skill's `deploy.ps1` (in the repo copy:
`<solution>\GetOtpAPI\.claude\skills\deploy-uaepass\deploy.ps1`; personal copy: `$HOME\.claude\skills\deploy-uaepass\deploy.ps1`).

## Steps

1. Pick the target from what changed: run `git -C <repo> status --short` in both repos.
   Changes in only one repo → that target; both → `All`. If the user named one, use that.
2. If there are uncommitted changes, write a one-line commit message describing them (read the diff).
3. Run a dry run first - it builds, shows the files to commit and checks the Azure login, without pushing:
   ```
   powershell -ExecutionPolicy Bypass -File "<script>" -Target Api -Message "<msg>" -DryRun
   ```
4. If the dry run is clean, run the same command without `-DryRun` (timeout 600000 ms - the ACR build takes ~3-5 min).
5. Report: commit SHA, new revision name / deployed functions, and the smoke-test result.
   Remind the user that a real end-to-end test means calling
   `POST https://getotpapi-ca.thankfuldesert-e23e2261.uaenorth.azurecontainerapps.io/api/uaepass/Step1_GetPasscode?PhoneNumber=971...`
   and approving on the phone; never call it yourself (it starts a real UAE PASS login).

## How the script deploys (and why)

- **Api**: pushes, then `az acr build ... https://github.com/AhmedMohyOmar/testUAEPASS.git#<sha>` so ACR pulls
  the source from GitHub. Uploading the local folder (`az acr build <folder>`) is reset by the office network.
  Then `az containerapp update --image ...:<sha>`, waits for the revision to be Running, checks swagger.
- **Function**: `dotnet publish -c Release`, zip, `az functionapp deployment source config-zip`.
  The repo's GitHub workflow (`build-ubuntu.yml`) builds a container image and does not match the
  Flex Consumption plan - don't rely on it.
- The testUAEPASS GitHub workflow also deploys the API on push, but its `GETOTPAPICA_AZURE_CREDENTIALS`
  secret expired (Nov 2026); a failed run there is expected until it is renewed and does not affect this script.

## Troubleshooting

- `not logged in` → user runs `az login` (account with access to the subscription above).
- Revision not Running → `az containerapp logs show -n getotpapi-ca -g RG_TDRA_UAE --revision <rev> --type console --tail 100`.
- `Step1_GetPasscode` times out at `navigating to ... stg-id.uaepass.ae` → UAE PASS drops `HeadlessChrome`
  user-agents; every `NewContextAsync` must pass `UserAgent = playwrightController.BROWSER_USER_AGENT`.
- Roll back the API to the previous image:
  `az containerapp revision list -n getotpapi-ca -g RG_TDRA_UAE -o table`, then
  `az containerapp update -n getotpapi-ca -g RG_TDRA_UAE --image <previous image>`.
