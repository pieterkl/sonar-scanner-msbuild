<#

.SYNOPSIS
This script controls the build process on the CI server.

#>

[CmdletBinding(PositionalBinding = $false)]
param (
    # SonarQube related parametersZ
    [string]$sonarCloudUrl = "",
    [string]$sonarCloudToken = "",

    #If you want to analyse on a local SonarQube instance.
    [string]$branchName = "",
    [string]$buildNumber = "",

    # Others
    [string]$appDataPath = $env:APPDATA
)

Set-StrictMode -version 2.0
$ErrorActionPreference = "Stop"

if ($PSBoundParameters['Verbose'] -Or $PSBoundParameters['Debug']) {
    $global:DebugPreference = "Continue"
}

function Get-DotNetVersion() {
    [xml]$versionProps = Get-Content "${PSScriptRoot}\version\Version.props"
    $fullVersion = $versionProps.Project.PropertyGroup.MainVersion + "." + $versionProps.Project.PropertyGroup.BuildNumber

    Write-Debug ".Net version is '${fullVersion}'"

    return $fullVersion
}

function Set-DotNetVersion() {
    Write-Header "Updating version in .Net files"

    $githubSha1 = git -C "${PSScriptRoot}\.." rev-parse HEAD

    Write-Debug "Setting build number ${buildNumber}, sha1 ${githubSha1} and branch ${branchName}"

    Invoke-InLocation (Join-Path $PSScriptRoot "version") {
        Write-Debug "Setting build number ${buildNumber}, sha1 ${githubSha1} and branch ${branchName}"
        $versionProperties = "Version.props"
        (Get-Content $versionProperties) `
                -Replace '<Sha1>.*</Sha1>', "<Sha1>${githubSha1}</Sha1>" `
                -Replace '<BuildNumber>.*</BuildNumber>', "<BuildNumber>${buildNumber}</BuildNumber>" `
                -Replace '<BranchName>.*</BranchName>', "<BranchName>${branchName}</BranchName>" `
            | Set-Content $versionProperties

        Invoke-MSBuild "Current" "ChangeVersion.proj" `
        /p:Sha1=$githubSha1 `
        /p:BranchName=$branchName `
        /p:BuildNumber=$buildNumber `
        /p:BuildConfiguration="Release"

        $version = Get-DotNetVersion
        Write-Host "Version successfully set to '${version}'"
    }
}

function Get-LeakPeriodVersion() {
    [xml]$versionProps = Get-Content "${PSScriptRoot}\version\Version.props"
    $mainVersion = $versionProps.Project.PropertyGroup.MainVersion

    Write-Debug "Leak period version is '${mainVersion}'"

    return $mainVersion
}

function Get-ScannerMsBuildPath() {
    if(-Not (Test-Path "${PSScriptRoot}\..\dogfoodExecutable"))
    {
        New-Item -Path "${PSScriptRoot}\..\dogfoodExecutable" -ItemType directory
    }
    $currentDir = (Resolve-Path .\sonar-scanner-msbuild\dogfoodExecutable).Path

    $scannerMsbuild = Join-Path $currentDir "SonarScanner.MSBuild.exe"

    if (-Not (Test-Path $scannerMsbuild)) {
        Write-Host "Scanner for MSBuild not found, downloading it"

        if ($env:ARTIFACTORY_URL)
        {
            Write-Host "Environment variable ARTIFACTORY_URL = $env:ARTIFACTORY_URL"
        }
        else
        {
            # We want this to be a terminating error so we throw
            Throw "Environment variable ARTIFACTORY_URL is not set"
        }

        # This links always redirect to the latest released scanner
        $downloadLink = "$env:ARTIFACTORY_URL/sonarsource-public-releases/org/sonarsource/scanner/msbuild/" +
            "sonar-scanner-msbuild/%5BRELEASE%5D/sonar-scanner-msbuild-%5BRELEASE%5D-net46.zip"
        $scannerMsbuildZip = Join-Path $currentDir "\SonarScanner.MSBuild.zip"

        Write-Host "Downloading scanner from '${downloadLink}' at '${currentDir}'"

        # NB: the WebClient class defaults to TLS v1, which is no longer supported by some online providers
        [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
        (New-Object System.Net.WebClient).DownloadFile($downloadLink, $scannerMsbuildZip)

        # perhaps we could use other folder, not the repository root
        Expand-ZIPFile $scannerMsbuildZip $currentDir

        Write-Host "Deleting downloaded zip"
        Remove-Item $scannerMsbuildZip -Force
    }

    Write-Host "Scanner for MSBuild found at '$scannerMsbuild'"
    return $scannerMsbuild
}

function Invoke-SonarBeginAnalysis([array][parameter(ValueFromRemainingArguments = $true)]$remainingArgs) {
    Write-Header "Running SonarCloud Analysis begin step"

    if (Test-Debug) {
        $remainingArgs += "/d:sonar.verbose=true"
    }

    Exec { Get-ScannerMsBuildPath begin `
        /k:sonarscanner-msbuild `
        /n:"SonarScanner for MSBuild" `
        /d:sonar.host.url=$sonarCloudUrl `
        /d:sonar.login=$sonarCloudToken `
        /d:sonar.cs.vstest.reportsPaths="**\*.trx" `
        /d:sonar.cs.vscoveragexml.reportsPaths="**\*.coveragexml" `
        $remainingArgs `
    } -errorMessage "ERROR: SonarCloud Analysis begin step FAILED."
}

function Invoke-SonarEndAnalysis() {
    Write-Header "Running SonarCloud Analysis end step"

    Exec { Get-ScannerMsBuildPath end `
        /d:sonar.login=$sonarCloudToken `
    } -errorMessage "ERROR: SonarCloud Analysis end step FAILED."
}

function Publish-Artifacts() {
    $artifactsFolder = ".\sonar-scanner-msbuild\DeploymentArtifacts\BuildAgentPayload\Release"

    $classicScannerZipPath = Get-Item "$artifactsFolder\\sonarscanner-msbuild-net46.zip"
    $dotnetScannerZipPath = Get-Item "$artifactsFolder\\sonarscanner-msbuild-netcoreapp2.0.zip"
    $dotnetScannerZipPath3 = Get-Item "$artifactsFolder\\sonarscanner-msbuild-netcoreapp3.0.zip"
    $dotnetScannerGlobalToolPath = Get-Item "$artifactsFolder\\dotnet-sonarscanner.$leakPeriodVersion.nupkg"

    $version = Get-DotNetVersion

    Write-Host "Generating the chocolatey packages"
    $classicZipHash = (Get-FileHash $classicScannerZipPath -Algorithm SHA256).hash
    $net46ps1 = "nuspec\chocolatey\chocolateyInstall-net46.ps1"
    (Get-Content $net46ps1) `
            -Replace '-Checksum "not-set"', "-Checksum $classicZipHash" `
            -Replace "__PackageVersion__", "$version" `
        | Set-Content $net46ps1

    $dotnetZipHash = (Get-FileHash $dotnetScannerZipPath -Algorithm SHA256).hash
    $netcoreps1 = "nuspec\chocolatey\chocolateyInstall-netcoreapp2.0.ps1"
    (Get-Content $netcoreps1) `
            -Replace '-Checksum "not-set"', "-Checksum $dotnetZipHash" `
            -Replace "__PackageVersion__", "$version" `
        | Set-Content $netcoreps1

    $dotnetZipHash3 = (Get-FileHash $dotnetScannerZipPath3 -Algorithm SHA256).hash
    $netcoreps13 = "nuspec\chocolatey\chocolateyInstall-netcoreapp3.0.ps1"
    (Get-Content $netcoreps13) `
        -Replace '-Checksum "not-set"', "-Checksum $dotnetZipHash3" `
        -Replace "__PackageVersion__", "$version" `
    | Set-Content $netcoreps13

    Exec { & choco pack nuspec\chocolatey\sonarscanner-msbuild-net46.nuspec `
        --outputdirectory $artifactsFolder `
        --version $version `
    } -errorMessage "ERROR: Creation of the net46 chocolatey package FAILED."
    Exec { & choco pack nuspec\chocolatey\sonarscanner-msbuild-netcoreapp2.0.nuspec `
        --outputdirectory $artifactsFolder `
        --version $version `
    } -errorMessage "ERROR: Creation of the netcoreapp2.0 chocolatey package FAILED."

    Exec { & choco pack nuspec\chocolatey\sonarscanner-msbuild-netcoreapp3.0.nuspec `
        --outputdirectory $artifactsFolder `
        --version $version `
    } -errorMessage "ERROR: Creation of the netcoreapp3.0 chocolatey package FAILED."

    Exec { & nuget pack nuspec\netcoreglobaltool\dotnet-sonarscanner.nuspec `
        --outputdirectory $artifactsFolder `
        --version $version `
    } -errorMessage "ERROR: Creation of the dotnet global tool FAILED."


    Write-Host "Update artifacts locations in pom.xml"
    $pomFile = ".\pom.xml"
    $currentDir = (Get-Item -Path ".\").FullName
    (Get-Content $pomFile) `
            -Replace 'classicScannerZipPath', "$classicScannerZipPath" `
            -Replace 'dotnetScannerZipPath', "$dotnetScannerZipPath" `
            -Replace 'dotnetScannerGlobalToolPath', "$dotnetScannerGlobalToolPath" `
            -Replace 'classicScannerChocoPath', "$currentDir\\$artifactsFolder\\sonarscanner-msbuild-net46.$version.nupkg" `
            -Replace 'dotnetcore2ScannerChocoPath', "$artifactsFolder\\sonarscanner-msbuild-netcoreapp2.0.$version.nupkg" `
             -Replace 'dotnetcore3ScannerChocoPath', "$artifactsFolder\\sonarscanner-msbuild-netcoreapp30.$version.nupkg" `
        | Set-Content $pomFile

    Exec { & mvn org.codehaus.mojo:versions-maven-plugin:2.2:set "-DnewVersion=${version}" `
        -DgenerateBackupPoms=false -B -e `
    } -errorMessage "ERROR: Maven set version FAILED."
}

function Invoke-DotNetBuild() {
    Set-DotNetVersion

    $leakPeriodVersion = Get-LeakPeriodVersion

    Invoke-SonarBeginAnalysis `
           /v:$leakPeriodVersion `
            /d:sonar.analysis.buildNumber=$buildNumber `
            /d:sonar.analysis.pipeline=$buildNumber `
            /d:sonar.analysis.repository="local"

    Restore-Packages "Current" $solutionName
    Invoke-MSBuild "Current" $solutionName `
        /bl:"${binPath}\msbuild.binlog" `
        /consoleloggerparameters:Summary `
        /m `
        /p:configuration=$buildConfiguration `
        /p:platform="Any CPU" `
        /p:DeployExtension=false `
        /p:ZipPackageCompressionLevel=normal `

    Invoke-UnitTests $binPath $true
    Invoke-CodeCoverage

    Invoke-SonarEndAnalysis
    Publish-Artifacts $leakPeriodVersion
}

function Initialize-QaStep() {
    Write-Host "Triggering QA job"
    Invoke-InLocation "${PSScriptRoot}\..\its" { & mvn verify -B -e } -errorMessage "ERROR: Maven ITs FAILED."
}

try {
    . (Join-Path $PSScriptRoot "build-utils.ps1")

    $buildConfiguration = "Release"
    $binPath = "bin\${buildConfiguration}"
    $solutionName = Join-Path $PSScriptRoot "..\SonarScanner.MSBuild.sln"

    Write-Debug "Solution to build: ${solutionName}"
    Write-Debug "Build configuration: ${buildConfiguration}"
    Write-Debug "Bin folder to use: ${binPath}"

    Invoke-InLocation "${PSScriptRoot}\..\.." {
        Invoke-DotNetBuild
    }

    Invoke-InLocation "${PSScriptRoot}\..\.." { Initialize-QaStep }

    Write-Host -ForegroundColor Green "SUCCESS: BUILD job was successful!"
    exit 0
}
catch {
    Write-Host -ForegroundColor Red $_
    Write-Host $_.Exception
    Write-Host $_.ScriptStackTrace
    exit 1
}