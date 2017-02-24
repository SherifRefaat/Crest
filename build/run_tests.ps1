# "Install" the tools to run/upload the coverage
dotnet restore .\build\project.json --packages .\build\tools

$dotnetexe = (Get-Command dotnet).Definition
$wc = New-Object 'System.Net.WebClient'

# Run all the projects in the test directory
foreach ($test in (dir .\test -Name))
{
	# This runs the unit tests and gets the coverage
	.\build\tools\OpenCover\4.6.519\tools\OpenCover.Console.exe `
	-output:.\CoverResult.xml `
	-mergeoutput `
	-hideskipped `
	-oldstyle `
	-register:user `
	-filter:"+[Crest.*]* -[*]DryIoc.*" `
	-target:"$dotnetexe" `
	-targetargs:"test test\$test --no-build  --labels=Off --noheader" `
	-returntargetcode
	
	# We still want to upload the failed tests so we know what went wrong
	$tests_exit_code = $?

	# Upload the results
	$wc.UploadFile("https://ci.appveyor.com/api/testresults/nunit3/$($env:APPVEYOR_JOB_ID)", (Resolve-Path .\TestResult.xml))
	
	# Fail the build if a test failed
	if (-not $tests_exit_code) { throw "Unit tests failed" }
}

# Upload the coverage in one go, as CoverResult.xml gets merged with all the results
.\build\tools\coveralls.net\0.7.0\tools\csmacnz.Coveralls.exe `
	--opencover `
	-i .\CoverResult.xml `
	--repoTokenVariable COVERALLS_REPO_TOKEN `
	--useRelativePaths `
	--serviceName appveyor `
	--commitId "$env:APPVEYOR_REPO_COMMIT" `
	--commitBranch "$env:APPVEYOR_REPO_BRANCH" `
	--commitAuthor "$env:APPVEYOR_REPO_COMMIT_AUTHOR" `
	--commitEmail "$env:APPVEYOR_REPO_COMMIT_AUTHOR_EMAIL" `
	--commitMessage "$env:APPVEYOR_REPO_COMMIT_MESSAGE" `
	--jobId "$env:APPVEYOR_BUILD_NUMBER"
