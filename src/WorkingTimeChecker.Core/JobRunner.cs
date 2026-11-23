using WorkingTimeChecker.Core.Workload;

namespace WorkingTimeChecker.Core;

public sealed class JobRunner
{
    private readonly PythonRunner _pythonRunner;
    private readonly string _pythonScriptsDirectory;
    private readonly string _pythonExecutablePath;
    private static readonly IReadOnlyList<JobStep> Steps =
        new List<JobStep>
        {
            new(
                "ConfluenceDownload",
                JobStepType.Python,
                "ConfluenceDownload.py"
            ),
            new(
                "ExcelMacro",
                JobStepType.PowerShell,
                "CSharpWorkloadAggregation"
            ),
            new(
                "WebAttendanceDownload",
                JobStepType.Python,
                "WebAttendanceDownload.py"
            ),
            new(
                "WorkingTimeChecker",
                JobStepType.Python,
                "WorkingTimeChecker.py"
            ),
            new(
                "CopyConfluence",
                JobStepType.Python,
                "CopyConfluence.py"
            ),
            new(
                "PostRocketChat",
                JobStepType.Python,
                "PostRocketChatMessage.py"
            ),
        };

    public JobRunner(
        PythonRunner pythonRunner,
        string pythonScriptsDirectory,
        string pythonExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(
                pythonScriptsDirectory))
        {
            throw new ArgumentException(
                "Pythonスクリプトフォルダーが未指定です。",
                nameof(pythonScriptsDirectory)
            );
        }
_pythonRunner = pythonRunner;
        _pythonScriptsDirectory =
            pythonScriptsDirectory;

        if (string.IsNullOrWhiteSpace(
                pythonExecutablePath))
        {
            throw new ArgumentException(
                "Python実行ファイルが未指定です。",
                nameof(pythonExecutablePath)
            );
        }

        if (!File.Exists(pythonExecutablePath))
        {
            throw new FileNotFoundException(
                "Python実行ファイルが見つかりません。",
                pythonExecutablePath
            );
        }

        _pythonExecutablePath =
            pythonExecutablePath;
}

    public async Task<int> RunOnlyAsync(
        string stepName,
        Action<string> writeLog,
        CancellationToken cancellationToken = default,
        bool validateOnly = false)
    {
        var step = FindStep(stepName);

        if (validateOnly &&
            !step.Name.Equals(
                "ExcelMacro",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "--validateはExcelMacroでのみ使用できます。"
            );
        }

        return await RunStepAsync(
            step,
            writeLog,
            cancellationToken,
            validateOnly
        );
    }

    public async Task<int> RunRangeAsync(
        string? fromStepName,
        string? toStepName,
        Action<string> writeLog,
        CancellationToken cancellationToken = default)
    {
        var startIndex = 0;
        var endIndex = Steps.Count - 1;

        if (!string.IsNullOrWhiteSpace(fromStepName))
        {
            startIndex = FindStepIndex(fromStepName);
        }

        if (!string.IsNullOrWhiteSpace(toStepName))
        {
            endIndex = FindStepIndex(toStepName);
        }

        if (startIndex > endIndex)
        {
            throw new ArgumentException(
                "開始ステップが終了ステップより後です: " +
                $"{fromStepName} → {toStepName}"
            );
        }

        for (var index = startIndex;
             index <= endIndex;
             index++)
        {
            var exitCode = await RunStepAsync(
                Steps[index],
                writeLog,
                cancellationToken
            );

            if (exitCode != 0)
            {
                writeLog(
                    "後続ステップは実行しません。"
                );

                return exitCode;
            }
        }

        return 0;
    }

    public static IReadOnlyList<string> GetStepNames()
    {
        return Steps
            .Select(step => step.Name)
            .ToList();
    }

    private static JobStep FindStep(
        string stepName)
    {
        var index = FindStepIndex(stepName);

        return Steps[index];
    }

    private static int FindStepIndex(
        string stepName)
    {
        for (var index = 0;
             index < Steps.Count;
             index++)
        {
            if (Steps[index].Name.Equals(
                    stepName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        throw new ArgumentException(
            $"存在しないステップです: {stepName}"
        );
    }

    private async Task<int> RunStepAsync(
        JobStep step,
        Action<string> writeLog,
        CancellationToken cancellationToken,
        bool validateOnly = false)
    {
        cancellationToken.ThrowIfCancellationRequested();

        writeLog(
            $"===== ステップ開始: {step.Name} ====="
        );

        int exitCode;

        switch (step.Type)
        {
            case JobStepType.Python:
                exitCode = await RunPythonStepAsync(
                    step,
                    writeLog,
                    cancellationToken
                );
                break;

            case JobStepType.PowerShell:
                exitCode = await RunCSharpExcelStepAsync(
                    writeLog,
                    cancellationToken,
                    validateOnly
                );
                break;

            default:
                throw new InvalidOperationException(
                    $"未対応のステップ種類です: {step.Type}"
                );
        }

        if (exitCode != 0)
        {
            writeLog(
                $"[NG] ステップ失敗: {step.Name} " +
                $"終了コード={exitCode}"
            );

            return exitCode;
        }

        writeLog(
            $"[OK] ステップ完了: {step.Name}"
        );

        return 0;
    }

    private Task<int> RunPythonStepAsync(
        JobStep step,
        Action<string> writeLog,
        CancellationToken cancellationToken)
    {
        var scriptPath = Path.Combine(
            _pythonScriptsDirectory,
            step.FileName
        );

        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException(
                "Pythonスクリプトが見つかりません: " +
                step.Name,
                scriptPath
            );
        }

        return _pythonRunner.RunAsync(
            _pythonExecutablePath,
            scriptPath,
            ApplicationPaths.ConfigPath,
            writeLog,
            cancellationToken
        );
    }

    private Task<int> RunCSharpExcelStepAsync(
        Action<string> writeLog,
        CancellationToken cancellationToken,
        bool validateOnly)
    {
        var aggregationStep =
            new CSharpExcelAggregationStep();

        return aggregationStep.RunAsync(
            ApplicationPaths.ConfigPath,
            writeLog,
            cancellationToken,
            validateOnly
        );
    }
}