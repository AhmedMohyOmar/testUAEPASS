<#
.SYNOPSIS
  Build, commit, push and deploy the UAE PASS solution (GetOtpAPI container app and/or the callback Function app).

.EXAMPLE
  .\deploy.ps1 -Target Api -Message "Fix passcode selector"
  .\deploy.ps1 -Target Function -Message "Map gender field"
  .\deploy.ps1 -Target All -DryRun
#>
param(
    [ValidateSet('Api', 'Function', 'All')]
    [string]$Target = 'Api',
    [string]$Message,
    [switch]$DryRun,
    # Folder that holds the GetOtpAPI and AzureFunctionTest clones
    [string]$SolutionRoot
)

$ErrorActionPreference = 'Continue'

if (-not $SolutionRoot) { $SolutionRoot = $env:UAEPASS_SOLUTION_ROOT }
if (-not $SolutionRoot) {
    # Running from <solution>\GetOtpAPI\.claude\skills\deploy-uaepass → the solution is four levels up
    $fromRepo = Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..') -ErrorAction SilentlyContinue
    if ($fromRepo -and (Test-Path (Join-Path $fromRepo 'GetOtpAPI\GetOtpAPI.csproj'))) { $SolutionRoot = $fromRepo.Path }
    else { $SolutionRoot = 'D:\VS Projects\AzureFunctionTest' }
}
if (-not (Test-Path "$SolutionRoot\GetOtpAPI\GetOtpAPI.csproj")) {
    Write-Host "FAILED: GetOtpAPI not found under '$SolutionRoot' - pass -SolutionRoot or set UAEPASS_SOLUTION_ROOT" -ForegroundColor Red; exit 1
}

$ApiRepo        = "$SolutionRoot\GetOtpAPI"
$FuncRepo       = "$SolutionRoot\AzureFunctionTest"
$Subscription   = 'd4b5c0a7-4891-43ca-b375-efde3ad465ee'
$ResourceGroup  = 'RG_TDRA_UAE'
$Registry       = 'headlessbrowserdocker'
$ContainerApp   = 'getotpapi-ca'
$ApiGitHubUrl   = 'https://github.com/AhmedMohyOmar/testUAEPASS.git'
$ApiBaseUrl     = 'https://getotpapi-ca.thankfuldesert-e23e2261.uaenorth.azurecontainerapps.io'
$FunctionApp    = 'tdrauaepasspoc'

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }
function Fail($text) { Write-Host "FAILED: $text" -ForegroundColor Red; exit 1 }
function Ok($text)   { Write-Host "OK: $text" -ForegroundColor Green }

function Build-Project($dir, $csproj) {
    Step "Building $csproj"
    dotnet build "$dir\$csproj" -nologo -v q -clp:ErrorsOnly | Out-Host
    if ($LASTEXITCODE -ne 0) { Fail "dotnet build of $csproj" }
    Ok 'build succeeded'
}

# Commits all pending changes (the repos' .gitignore files keep bin/obj out) and pushes main.
function Commit-And-Push($dir) {
    Step "Git: $dir"
    $pending = git -C $dir status --short
    if ($pending) {
        $pending | Out-Host
        if (-not $Message) { Fail 'there are uncommitted changes; pass -Message "<commit message>"' }
        if ($DryRun) { Write-Host '[dry-run] would commit the files above and push main' }
        else {
            git -C $dir add -A
            git -C $dir commit -q -m $Message
            if ($LASTEXITCODE -ne 0) { Fail 'git commit' }
        }
    }
    else { Write-Host 'no uncommitted changes' }

    if (-not $DryRun) {
        git -C $dir push origin main
        if ($LASTEXITCODE -ne 0) { Fail 'git push' }
    }
    $sha = git -C $dir rev-parse --short HEAD
    Ok "HEAD is $sha"
    return $sha
}

function Deploy-Api {
    Build-Project $ApiRepo 'GetOtpAPI.csproj'
    $sha = Commit-And-Push $ApiRepo
    $image = "$Registry.azurecr.io/${ContainerApp}:$sha"

    if ($DryRun) { Write-Host "[dry-run] would build $image in ACR from $ApiGitHubUrl#$sha and deploy it to $ContainerApp"; return }

    # ACR pulls the source from GitHub itself; uploading from the office network gets reset
    Step "Building image $image in ACR (from GitHub commit $sha)"
    az acr build --registry $Registry --image "${ContainerApp}:$sha" --file Dockerfile "$ApiGitHubUrl#$sha" --no-logs --query status -o tsv
    if ($LASTEXITCODE -ne 0) { Fail 'az acr build' }
    Ok 'image built'

    Step "Deploying $image to $ContainerApp"
    $revision = az containerapp update -n $ContainerApp -g $ResourceGroup --image $image --query properties.latestRevisionName -o tsv
    if ($LASTEXITCODE -ne 0 -or -not $revision) { Fail 'az containerapp update' }
    Write-Host "new revision: $revision"

    $state = $null
    for ($i = 0; $i -lt 30; $i++) {
        $state = az containerapp revision show -n $ContainerApp -g $ResourceGroup --revision $revision --query "properties.runningState" -o tsv
        if ($state -and $state -ne 'Activating') { break }
        Start-Sleep -Seconds 10
    }
    if ($state -notin @('Running', 'RunningAtMaxScale')) { Fail "revision $revision is '$state' - check: az containerapp logs show -n $ContainerApp -g $ResourceGroup --revision $revision --type console" }
    Ok "revision $revision is $state"

    Step 'Smoke test'
    try {
        $swagger = Invoke-RestMethod "$ApiBaseUrl/swagger/v1/swagger.json" -TimeoutSec 60
        Ok ("endpoints: " + ($swagger.paths.PSObject.Properties.Name -join ', '))
    }
    catch { Fail "swagger not reachable: $($_.Exception.Message)" }
}

function Deploy-Function {
    Build-Project $FuncRepo 'AzureFunctionTest.csproj'
    $sha = Commit-And-Push $FuncRepo

    $work = Join-Path $env:TEMP "deploy-uaepass-func"
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force "$work\publish" | Out-Null

    Step 'Publishing Function app (Release)'
    dotnet publish "$FuncRepo\AzureFunctionTest.csproj" -c Release -o "$work\publish" -nologo -v q -clp:ErrorsOnly | Out-Host
    if ($LASTEXITCODE -ne 0) { Fail 'dotnet publish' }
    Compress-Archive -Path "$work\publish\*" -DestinationPath "$work\function.zip" -Force
    Ok ("package: {0:N1} MB" -f ((Get-Item "$work\function.zip").Length / 1MB))

    if ($DryRun) { Write-Host "[dry-run] would zip-deploy commit $sha to Function app $FunctionApp"; return }

    Step "Zip-deploying to $FunctionApp (Flex Consumption)"
    az functionapp deployment source config-zip -g $ResourceGroup -n $FunctionApp --src "$work\function.zip" -o none
    if ($LASTEXITCODE -ne 0) { Fail 'az functionapp deployment source config-zip' }

    $functions = az functionapp function list -g $ResourceGroup -n $FunctionApp --query "[].name" -o tsv
    Ok ("deployed functions: " + ($functions -join ', '))
}

Step 'Azure login'
az account set --subscription $Subscription
if ($LASTEXITCODE -ne 0) { Fail "not logged in to subscription $Subscription - run: az login" }
Ok (az account show --query "user.name" -o tsv)

if ($Target -in @('Api', 'All'))      { Deploy-Api }
if ($Target -in @('Function', 'All')) { Deploy-Function }

Write-Host "`nDone ($Target)$(if ($DryRun) { ' - dry run, nothing was pushed or deployed' })" -ForegroundColor Green
