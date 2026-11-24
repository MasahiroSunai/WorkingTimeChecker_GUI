using System.IO.Compression;
using System.Reflection;

namespace WorkingTimeChecker.Core.Runtime;

public sealed class RuntimeExtractor
{
    private const string PythonScriptsResourceName =
        "WorkingTimeChecker.Resources.python-scripts.zip";

    private const string RuntimeVersion =
        "1.0.0";

    public string EnsurePythonScriptsExtracted(
        Action<string> writeLog)
    {
        var localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData
            );

        var runtimeDirectory = Path.Combine(
            localAppData,
            "WorkingTimeChecker",
            "runtime"
        );

        var scriptsDirectory = Path.Combine(
            runtimeDirectory,
            "scripts"
        );

        var versionFilePath = Path.Combine(
            runtimeDirectory,
            ".scripts-version"
        );

        if (IsCurrentVersionInstalled(
                scriptsDirectory,
                versionFilePath))
        {
            writeLog(
                "[OK] 展開済みPythonスクリプトを使用します: " +
                scriptsDirectory
            );

            return scriptsDirectory;
        }

        writeLog(
            "組み込みPythonスクリプトを展開します。"
        );

        Directory.CreateDirectory(runtimeDirectory);

        var temporaryDirectory = Path.Combine(
            runtimeDirectory,
            "scripts.tmp"
        );

        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(
                temporaryDirectory,
                recursive: true
            );
        }

        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            ExtractResourceToDirectory(
                temporaryDirectory
            );

            ValidateExtractedFiles(
                temporaryDirectory
            );

            if (Directory.Exists(scriptsDirectory))
            {
                Directory.Delete(
                    scriptsDirectory,
                    recursive: true
                );
            }

            Directory.Move(
                temporaryDirectory,
                scriptsDirectory
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
            "[OK] Pythonスクリプトを展開しました: " +
            scriptsDirectory
        );

        return scriptsDirectory;
    }

    private static bool IsCurrentVersionInstalled(
        string scriptsDirectory,
        string versionFilePath)
    {
        if (!Directory.Exists(scriptsDirectory) ||
            !File.Exists(versionFilePath))
        {
            return false;
        }

        var installedVersion =
            File.ReadAllText(versionFilePath).Trim();

        if (!installedVersion.Equals(
                RuntimeVersion,
                StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            ValidateExtractedFiles(scriptsDirectory);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ExtractResourceToDirectory(
        string destinationDirectory)
    {
        var assembly =
            typeof(RuntimeExtractor).Assembly;

        using var resourceStream =
            assembly.GetManifestResourceStream(
                PythonScriptsResourceName
            );

        if (resourceStream is null)
        {
            throw new InvalidOperationException(
                "組み込みPythonスクリプトが" +
                "見つかりません。"
            );
        }

        using var zipArchive = new ZipArchive(
            resourceStream,
            ZipArchiveMode.Read
        );

        zipArchive.ExtractToDirectory(
            destinationDirectory,
            overwriteFiles: true
        );
    }

    private static void ValidateExtractedFiles(
        string scriptsDirectory)
    {
        var requiredFiles = new[]
        {
            "ConfluenceDownload.py",
            "WebAttendanceDownload.py",
            "WorkingTimeChecker.py",
            "CopyConfluence.py",
            "PostRocketChatMessage.py",
            Path.Combine(
                "utils",
                "__init__.py"
            ),
            Path.Combine(
                "utils",
                "config_loader.py"
            ),
            Path.Combine(
                "utils",
                "logger_utils.py"
            ),
        };

        foreach (var relativePath in requiredFiles)
        {
            var fullPath = Path.Combine(
                scriptsDirectory,
                relativePath
            );

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    "展開したPythonスクリプトに" +
                    "必要なファイルがありません。",
                    fullPath
                );
            }
        }
    }
}