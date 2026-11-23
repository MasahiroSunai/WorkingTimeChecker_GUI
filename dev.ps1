param(
    [ValidateSet(
        "PostRocketChat",
        "ExcelValidate",
        "Build"
    )]
    [string]$Mode = "PostRocketChat"
)

$ErrorActionPreference = "Stop"

$project = Join-Path `
    $PSScriptRoot `
    "src\WorkingTimeChecker.Cli\WorkingTimeChecker.Cli.csproj"

$solution = Join-Path `
    $PSScriptRoot `
    "WorkingTimeChecker.slnx"

switch ($Mode) {
    "Build" {
        dotnet build $solution

        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }

    "ExcelValidate" {
        dotnet run `
            --project $project `
            --no-restore `
            -- `
            --only ExcelMacro `
            --validate

        exit $LASTEXITCODE
    }

    "PostRocketChat" {
        dotnet run `
            --project $project `
            --no-restore `
            -- `
            --only PostRocketChat

        exit $LASTEXITCODE
    }
}