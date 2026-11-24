using System.IO.Compression;

namespace WorkingTimeChecker.Core.Runtime;

public sealed class PythonRuntimeExtractor
{
    private const string ResourceName =
        "WorkingTimeChecker.Resources.python-runtime.zip";

    private const string RuntimeVersion =
        "3.14.6-runtime1";

    public string EnsureExtracted(
        Action<string> writeLog)
    {
        var localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData
            );

        var runtimeBaseDirectory = Path.Combine(
            localAppData,
            "WorkingTimeChecker",
            "runtime"
        );

        var pythonDirectory = Path.Combine(
            runtimeBaseDirectory,
            "python"
        );

        var pythonPath = Path.Combine(
            pythonDirectory,
            "python.exe"
        );

        var versionFilePath = Path.Combine(
            runtimeBaseDirectory,
            ".python-version"
        );

        if (File.Exists(pythonPath) &&
            File.Exists(versionFilePath) &&
            File.ReadAllText(versionFilePath).Trim() ==
            RuntimeVersion)
        {
            writeLog(
                "[OK] 展開済み内蔵Pythonを使用します: " +
                pythonPath
            );

            ActivatePython(
                pythonDirectory,
                pythonPath
            );

            return pythonPath;
        }

        writeLog(
            "組み込みPython実行環境を展開します。"
        );

        Directory.CreateDirectory(
            runtimeBaseDirectory
        );

        var temporaryDirectory = Path.Combine(
            runtimeBaseDirectory,
            "python.tmp"
        );

        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(
                temporaryDirectory,
                recursive: true
            );
        }

        Directory.CreateDirectory(
            temporaryDirectory
        );

        try
        {
            ExtractResource(
                temporaryDirectory
            );

            var temporaryPythonPath = Path.Combine(
                temporaryDirectory,
                "python.exe"
            );

            ValidateRuntime(
                temporaryPythonPath
            );

            if (Directory.Exists(pythonDirectory))
            {
                Directory.Delete(
                    pythonDirectory,
                    recursive: true
                );
            }

            Directory.Move(
                temporaryDirectory,
                pythonDirectory
            );

            File.WriteAllText(
                versionFilePath,
                RuntimeVersion
            );
        }
        catch
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(
                    temporaryDirectory,
                    recursive: true
                );
            }

            throw;
        }

        writeLog(
            "[OK] 内蔵Pythonを展開しました: " +
            pythonPath
        );

        ActivatePython(
            pythonDirectory,
            pythonPath
        );

        return pythonPath;
    }

    private static void ExtractResource(
        string destinationDirectory)
    {
        var assembly =
            typeof(PythonRuntimeExtractor).Assembly;

        using var resourceStream =
            assembly.GetManifestResourceStream(
                ResourceName
            );

        if (resourceStream is null)
        {
            throw new InvalidOperationException(
                "組み込みPython実行環境が見つかりません。"
            );
        }

        using var archive = new ZipArchive(
            resourceStream,
            ZipArchiveMode.Read
        );

        archive.ExtractToDirectory(
            destinationDirectory,
            overwriteFiles: true
        );
    }

    private static void ValidateRuntime(
        string pythonPath)
    {
        if (!File.Exists(pythonPath))
        {
            throw new FileNotFoundException(
                "展開したPythonが見つかりません。",
                pythonPath
            );
        }

        var pythonDirectory =
            Path.GetDirectoryName(pythonPath)
            ?? throw new InvalidOperationException(
                "Pythonフォルダーを取得できません。"
            );

        var requiredDirectories = new[]
        {
            Path.Combine(
                pythonDirectory,
                "Lib"
            ),
            Path.Combine(
                pythonDirectory,
                "Lib",
                "site-packages"
            ),
            Path.Combine(
                pythonDirectory,
                "DLLs"
            ),
        };

        foreach (var requiredDirectory
                 in requiredDirectories)
        {
            if (!Directory.Exists(requiredDirectory))
            {
                throw new DirectoryNotFoundException(
                    "内蔵Pythonに必要なフォルダーがありません: " +
                    requiredDirectory
                );
            }
        }
    }

    private static void ActivatePython(
        string pythonDirectory,
        string pythonPath)
    {
        var currentPath =
            Environment.GetEnvironmentVariable(
                "PATH",
                EnvironmentVariableTarget.Process
            ) ?? string.Empty;

        Environment.SetEnvironmentVariable(
            "PATH",
            pythonDirectory +
            Path.PathSeparator +
            currentPath,
            EnvironmentVariableTarget.Process
        );

        Environment.SetEnvironmentVariable(
            "WORKING_TIME_CHECKER_PYTHON",
            pythonPath,
            EnvironmentVariableTarget.Process
        );

        Environment.SetEnvironmentVariable(
            "PYTHONUTF8",
            "1",
            EnvironmentVariableTarget.Process
        );

        Environment.SetEnvironmentVariable(
            "PYTHONIOENCODING",
            "utf-8",
            EnvironmentVariableTarget.Process
        );
    }
}