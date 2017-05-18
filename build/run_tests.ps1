# Grab NuGet so we can download our tools
$sourceNugetExe = "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe"
$nugetExe = "./nuget.exe"
Invoke-WebRequest $sourceNugetExe -OutFile $nugetExe

# "Install" the tools to run/upload the coverage
& $nugetExe install OpenCover -OutputDirectory .\build\tools -version 4.6.519 -verbosity quiet
& $nugetExe install coveralls.net -OutputDirectory .\build\tools -version 0.7.0 -verbosity quiet

# dotnet-xunit is a tool package, so we can't install it :(
wget 'https://www.nuget.org/api/v2/package/dotnet-xunit/2.3.0-beta1-build3642' -OutFile .\build\tools\dotnet-xunit.zip
Expand-Archive .\build\tools\dotnet-xunit.zip .\build\tools\dotnet-xunit -Force

# Setup our variables relative to the current directory
$coveralls = Join-Path $pwd build\tools\coveralls.net.0.7.0\tools\csmacnz.Coveralls.exe
$coverResult = Join-Path $pwd CoverResult.xml
$dotnetexe = (Get-Command dotnet).Definition
$openCover = Join-Path $pwd build\tools\OpenCover.4.6.519\tools\OpenCover.Console.exe
$xunit = Join-Path $pwd build\tools\dotnet-xunit\lib\netcoreapp1.0\dotnet-xunit.dll

# Run all the projects in the test directory
foreach ($path in (dir .\test -Directory))
{
	# In order to invoke "dotnet xunit", we have to be in the test directory
	Write-Host ("Running unit tests for " + $path.Name)
	cd $path.FullName
	
	# This runs the unit tests and gets the coverage
	& $openCover `
	-output:"$coverResult" `
	-mergeoutput `
	-hideskipped `
	-oldstyle `
	-register:user `
	-filter:"+Crest.* -DryIoc.* -ImTools.*" `
	-target:"$dotnetexe" `
	-targetargs:"$xunit -nologo -appveyor" `
	-returntargetcode
	
	# Fail the build if a test failed
	$tests_exit_code = $?
	if (-not $tests_exit_code) { throw "Unit tests failed" }
}

# Upload the coverage in one go, as CoverResult.xml gets merged with all the results
& $coveralls `
	--opencover `
	-i "$coverResult" `
	--repoTokenVariable COVERALLS_REPO_TOKEN `
	--useRelativePaths `
	--serviceName appveyor `
	--commitId "$env:APPVEYOR_REPO_COMMIT" `
	--commitBranch "$env:APPVEYOR_REPO_BRANCH" `
	--commitAuthor "$env:APPVEYOR_REPO_COMMIT_AUTHOR" `
	--commitEmail "$env:APPVEYOR_REPO_COMMIT_AUTHOR_EMAIL" `
	--commitMessage "$env:APPVEYOR_REPO_COMMIT_MESSAGE" `
	--jobId "$env:APPVEYOR_BUILD_NUMBER"
