using System.Diagnostics;
using System.Text;

namespace WorkingTimeChecker.Core;

public sealed class PythonRunner
{
    public async Task<int> RunAsync(
        string pythonPath,
        string scriptPath,
        string configPath,
        Action<string> writeLog,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pythonPath))
        {
            throw new ArgumentException(
                "Python実行ファイルが未指定です。",
                nameof(pythonPath)
            );
        }

        if (!File.Exists(pythonPath))
        {
            throw new FileNotFoundException(
                "Python実行ファイルが見つかりません。",
                pythonPath
            );
        }

        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException(
                "Pythonスクリプトが見つかりません。",
                scriptPath
            );
        }

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException(
                "config.yamlが見つかりません。",
                configPath
            );
        }

        var workingDirectory =
            Path.GetDirectoryName(scriptPath)
            ?? AppContext.BaseDirectory;

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        startInfo.ArgumentList.Add("-u");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("--config");
        startInfo.ArgumentList.Add(configPath);

        // PythonへUTF-8出力を明示します。
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

        using var process = new Process
        {
            StartInfo = startInfo,
        };

        if (!process.Start())
        {
            throw new InvalidOperationException(
                $"Pythonを起動できませんでした: {pythonPath}"
            );
        }

        var standardOutputTask = ReadOutputAsync(
            process.StandardOutput,
            writeLog,
            cancellationToken
        );

        // Python loggingのINFOも標準エラーへ出るため、
        // エラー表示を付けず、そのまま出力します。
        var standardErrorTask = ReadOutputAsync(
            process.StandardError,
            writeLog,
            cancellationToken
        );

        await process.WaitForExitAsync(cancellationToken);

        await Task.WhenAll(
            standardOutputTask,
            standardErrorTask
        );

        return process.ExitCode;
    }

    private static async Task ReadOutputAsync(
        StreamReader reader,
        Action<string> writeLog,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(
                cancellationToken
            );

            if (line is null)
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                writeLog(line);
            }
        }
    }
}
