using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorkingTimeChecker.Core.Configuration;

namespace WorkingTimeChecker.Core.Application;

/// <summary>
/// GUIへ同梱されたRuntimeをLocalAppDataへ導入します。
/// </summary>
public sealed class RuntimeInstaller
{
    private const int SupportedSchemaVersion = 1;

    private const string InstallMutexName =
        @"Local\PERSOL_CROSS_TECHNOLOGY_" +
        "WorkingTimeChecker_RuntimeInstall";

    private const string InstalledManifestFileName =
        "installed-runtime.json";

    private static readonly JsonSerializerOptions
        ReadJsonOptions =
            new()
            {
                PropertyNameCaseInsensitive = true,
            };

    private static readonly JsonSerializerOptions
        WriteJsonOptions =
            new()
            {
                WriteIndented = true,
                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase,
            };

    /// <summary>
    /// Runtimeを導入または更新します。
    /// </summary>
    /// <param name="manifestFilePath">
    /// runtime-manifest.jsonのパスです。
    /// </param>
    /// <param name="archiveFilePath">
    /// runtime.zipのパスです。
    /// </param>
    /// <param name="writeLog">
    /// 状態を出力する処理です。
    /// </param>
    /// <returns>
    /// Runtimeの導入結果です。
    /// </returns>
    public RuntimeInstallResult InstallOrUpdate(
        string manifestFilePath,
        string archiveFilePath,
        Action<string>? writeLog = null)
    {
        writeLog ??=
            _ => { };

        ApplicationDataInitializer.CreateDirectories();

        ValidateInputFile(
            manifestFilePath,
            "Runtimeマニフェスト");

        ValidateInputFile(
            archiveFilePath,
            "Runtime ZIP");

        var manifest =
            ReadManifest(
                manifestFilePath);

        ValidateManifest(
            manifest,
            archiveFilePath);

        using var mutex =
            new Mutex(
                initiallyOwned: false,
                name: InstallMutexName);

        var lockAcquired =
            false;

        try
        {
            lockAcquired =
                mutex.WaitOne(
                    TimeSpan.FromSeconds(30));

            if (!lockAcquired)
            {
                throw new TimeoutException(
                    "Runtimeの更新処理が使用中です。");
            }

            return InstallOrUpdateCore(
                manifest,
                archiveFilePath,
                writeLog);
        }
        finally
        {
            if (lockAcquired)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private static RuntimeInstallResult InstallOrUpdateCore(
        RuntimeManifest manifest,
        string archiveFilePath,
        Action<string> writeLog)
    {
        var resolver =
            new RuntimeResolver();

        ActiveRuntimeState? currentState =
            null;

        if (File.Exists(
                ApplicationPaths.ActiveRuntimeFilePath))
        {
            currentState =
                resolver.GetActiveState();
        }

        var targetRuntimeDirectory =
            RuntimeResolver.GetRuntimeDirectory(
                manifest.RuntimeVersion);

        var targetCliPath =
            Path.Combine(
                targetRuntimeDirectory,
                manifest.EntryPoint);

        if (IsAlreadyInstalled(
                targetRuntimeDirectory,
                targetCliPath,
                manifest))
        {
            writeLog(
                "[OK] 導入済みRuntimeを使用します: " +
                manifest.RuntimeVersion);

            ActivateRuntime(
                manifest,
                currentState);

            return new RuntimeInstallResult(
                RuntimeVersion:
                    manifest.RuntimeVersion,
                RuntimeDirectory:
                    targetRuntimeDirectory,
                CliExecutablePath:
                    targetCliPath,
                Status:
                    RuntimeInstallStatus.AlreadyInstalled);
        }

        var temporaryDirectory =
            Path.Combine(
                ApplicationPaths.TemporaryDirectory,
                "runtime-install-" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(
            temporaryDirectory);

        try
        {
            writeLog(
                "[INFO] Runtimeを一時フォルダーへ展開します。");

            ExtractArchiveSafely(
                archiveFilePath,
                temporaryDirectory);

            var temporaryCliPath =
                Path.Combine(
                    temporaryDirectory,
                    manifest.EntryPoint);

            ValidateCliFile(
                temporaryCliPath);

            RunRuntimeCheck(
                temporaryCliPath,
                writeLog);

            WriteInstalledManifest(
                temporaryDirectory,
                manifest);

            if (Directory.Exists(
                    targetRuntimeDirectory))
            {
                Directory.Delete(
                    targetRuntimeDirectory,
                    recursive: true);
            }

            MoveDirectoryWithRetry(
                temporaryDirectory,
                targetRuntimeDirectory);

            ActivateRuntime(
                manifest,
                currentState);

            var status =
                currentState is null
                    ? RuntimeInstallStatus.Installed
                    : RuntimeInstallStatus.Updated;

            writeLog(
                "[OK] Runtimeを有効化しました: " +
                manifest.RuntimeVersion);

            return new RuntimeInstallResult(
                RuntimeVersion:
                    manifest.RuntimeVersion,
                RuntimeDirectory:
                    targetRuntimeDirectory,
                CliExecutablePath:
                    targetCliPath,
                Status:
                    status);
        }
        finally
        {
            if (Directory.Exists(
                    temporaryDirectory))
            {
                TryDeleteDirectory(
                    temporaryDirectory);
            }
        }
    }

    private static RuntimeManifest ReadManifest(
        string manifestFilePath)
    {
        var json =
            File.ReadAllText(
                manifestFilePath,
                Encoding.UTF8);

        var manifest =
            JsonSerializer.Deserialize<RuntimeManifest>(
                json,
                ReadJsonOptions);

        return manifest
            ?? throw new InvalidDataException(
                "Runtimeマニフェストを読み取れません。");
    }

    private static void ValidateManifest(
        RuntimeManifest manifest,
        string archiveFilePath)
    {
        if (manifest.SchemaVersion !=
            SupportedSchemaVersion)
        {
            throw new InvalidDataException(
                "未対応のRuntimeマニフェストです。" +
                $" SchemaVersion={manifest.SchemaVersion}");
        }

        RuntimeResolver.ValidateRuntimeVersion(
            manifest.RuntimeVersion);

        if (string.IsNullOrWhiteSpace(
                manifest.EntryPoint) ||
            !string.Equals(
                Path.GetFileName(
                    manifest.EntryPoint),
                manifest.EntryPoint,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "RuntimeのEntryPointが不正です。");
        }

        if (string.IsNullOrWhiteSpace(
                manifest.ArchiveFileName))
        {
            throw new InvalidDataException(
                "RuntimeのArchiveFileNameが空です。");
        }

        if (!string.Equals(
                Path.GetFileName(
                    archiveFilePath),
                manifest.ArchiveFileName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Runtime ZIPのファイル名が" +
                "マニフェストと一致しません。");
        }

        var actualHash =
            CalculateSha256(
                archiveFilePath);

        if (!string.Equals(
                actualHash,
                manifest.ArchiveSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Runtime ZIPのSHA-256が一致しません。" +
                Environment.NewLine +
                $"期待値: {manifest.ArchiveSha256}" +
                Environment.NewLine +
                $"実際値: {actualHash}");
        }
    }

    private static void ExtractArchiveSafely(
        string archiveFilePath,
        string destinationDirectory)
    {
        var destinationRoot =
            Path.GetFullPath(
                destinationDirectory)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        using var archive =
            ZipFile.OpenRead(
                archiveFilePath);

        foreach (var entry in archive.Entries)
        {
            var destinationPath =
                Path.GetFullPath(
                    Path.Combine(
                        destinationDirectory,
                        entry.FullName));

            if (!destinationPath.StartsWith(
                    destinationRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Runtime ZIPに不正なパスがあります: " +
                    entry.FullName);
            }

            if (string.IsNullOrEmpty(
                    entry.Name))
            {
                Directory.CreateDirectory(
                    destinationPath);

                continue;
            }

            var parentDirectory =
                Path.GetDirectoryName(
                    destinationPath);

            if (!string.IsNullOrWhiteSpace(
                    parentDirectory))
            {
                Directory.CreateDirectory(
                    parentDirectory);
            }

            entry.ExtractToFile(
                destinationPath,
                overwrite: true);
        }
    }

    private static void RunRuntimeCheck(
        string cliExecutablePath,
        Action<string> writeLog)
    {
        var startInfo =
            new ProcessStartInfo
            {
                FileName =
                    cliExecutablePath,

                UseShellExecute =
                    false,

                RedirectStandardOutput =
                    true,

                RedirectStandardError =
                    true,

                CreateNoWindow =
                    true,

                StandardOutputEncoding =
                    Encoding.UTF8,

                StandardErrorEncoding =
                    Encoding.UTF8,
            };

        startInfo.ArgumentList.Add(
            "--runtime-check");

        startInfo.ArgumentList.Add(
            "--log-directory");

        startInfo.ArgumentList.Add(
            ApplicationPaths.LogDirectory);

        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "RuntimeのCLIを起動できません。");

        var standardOutput =
            process.StandardOutput.ReadToEnd();

        var standardError =
            process.StandardError.ReadToEnd();

        process.WaitForExit();

        foreach (var line in
                 SplitOutputLines(
                     standardOutput))
        {
            writeLog(line);
        }

        foreach (var line in
                 SplitOutputLines(
                     standardError))
        {
            writeLog(line);
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "Runtime確認に失敗しました。" +
                $" 終了コード: {process.ExitCode}");
        }
    }

    private static bool IsAlreadyInstalled(
        string runtimeDirectory,
        string cliExecutablePath,
        RuntimeManifest manifest)
    {
        if (!Directory.Exists(
                runtimeDirectory) ||
            !File.Exists(
                cliExecutablePath))
        {
            return false;
        }

        var installedManifestPath =
            Path.Combine(
                runtimeDirectory,
                InstalledManifestFileName);

        if (!File.Exists(
                installedManifestPath))
        {
            return false;
        }

        try
        {
            var installedManifest =
                ReadManifest(
                    installedManifestPath);

            return
                string.Equals(
                    installedManifest.RuntimeVersion,
                    manifest.RuntimeVersion,
                    StringComparison.Ordinal) &&
                string.Equals(
                    installedManifest.ArchiveSha256,
                    manifest.ArchiveSha256,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    installedManifest.EntryPoint,
                    manifest.EntryPoint,
                    StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static void WriteInstalledManifest(
        string runtimeDirectory,
        RuntimeManifest manifest)
    {
        var filePath =
            Path.Combine(
                runtimeDirectory,
                InstalledManifestFileName);

        var json =
            JsonSerializer.Serialize(
                manifest,
                WriteJsonOptions);

        File.WriteAllText(
            filePath,
            json,
            new UTF8Encoding(false));
    }

    private static void ActivateRuntime(
        RuntimeManifest manifest,
        ActiveRuntimeState? currentState)
    {
        var previousVersion =
            currentState?.ActiveVersion;

        if (string.Equals(
                previousVersion,
                manifest.RuntimeVersion,
                StringComparison.Ordinal))
        {
            previousVersion =
                currentState?.PreviousVersion;
        }

        var newState =
            new ActiveRuntimeState(
                SchemaVersion:
                    SupportedSchemaVersion,
                ActiveVersion:
                    manifest.RuntimeVersion,
                PreviousVersion:
                    previousVersion,
                EntryPoint:
                    manifest.EntryPoint,
                ActivatedAtUtc:
                    DateTimeOffset.UtcNow);

        var json =
            JsonSerializer.Serialize(
                newState,
                WriteJsonOptions);

        Directory.CreateDirectory(
            ApplicationPaths.StateDirectory);

        var temporaryStatePath =
            ApplicationPaths.ActiveRuntimeFilePath +
            ".tmp";

        try
        {
            File.WriteAllText(
                temporaryStatePath,
                json,
                new UTF8Encoding(false));

            var reloadedState =
                JsonSerializer.Deserialize<ActiveRuntimeState>(
                    File.ReadAllText(
                        temporaryStatePath),
                    ReadJsonOptions);

            if (reloadedState is null ||
                !string.Equals(
                    reloadedState.ActiveVersion,
                    manifest.RuntimeVersion,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Runtime状態ファイルの検証に失敗しました。");
            }

            File.Move(
                temporaryStatePath,
                ApplicationPaths.ActiveRuntimeFilePath,
                overwrite: true);
        }
        finally
        {
            if (File.Exists(
                    temporaryStatePath))
            {
                File.Delete(
                    temporaryStatePath);
            }
        }
    }

    private static string CalculateSha256(
        string filePath)
    {
        using var stream =
            File.OpenRead(
                filePath);

        return Convert.ToHexString(
            SHA256.HashData(
                stream));
    }

    private static void ValidateCliFile(
        string cliFilePath)
    {
        if (!File.Exists(
                cliFilePath))
        {
            throw new FileNotFoundException(
                "Runtime内にCLIがありません。",
                cliFilePath);
        }

        var fileInfo =
            new FileInfo(
                cliFilePath);

        if (fileInfo.Length == 0)
        {
            throw new InvalidDataException(
                "Runtime内のCLIが空です。");
        }
    }

    private static void ValidateInputFile(
        string filePath,
        string displayName)
    {
        if (string.IsNullOrWhiteSpace(
                filePath))
        {
            throw new ArgumentException(
                $"{displayName}のパスが空です。",
                nameof(filePath));
        }

        if (!File.Exists(
                filePath))
        {
            throw new FileNotFoundException(
                $"{displayName}が見つかりません。",
                filePath);
        }
    }

    private static IReadOnlyList<string> SplitOutputLines(
        string output)
    {
        return output
            .Replace(
                "\r\n",
                "\n")
            .Replace(
                '\r',
                '\n')
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries);
    }

    private static void TryDeleteDirectory(
        string directory)
    {
        try
        {
            Directory.Delete(
                directory,
                recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Runtimeフォルダーの移動を再試行します。
    /// </summary>
    /// <param name="sourceDirectory">
    /// 移動元の一時フォルダーです。
    /// </param>
    /// <param name="destinationDirectory">
    /// 移動先のRuntimeフォルダーです。
    /// </param>
    private static void MoveDirectoryWithRetry(
        string sourceDirectory,
        string destinationDirectory)
    {
        const int maximumAttempts = 20;
        const int retryIntervalMilliseconds = 500;

        var destinationParentDirectory =
            Path.GetDirectoryName(
                destinationDirectory);

        if (string.IsNullOrWhiteSpace(
                destinationParentDirectory))
        {
            throw new InvalidOperationException(
                "Runtimeの移動先親フォルダーを取得できません。");
        }

        Directory.CreateDirectory(
            destinationParentDirectory);

        Exception? lastException = null;

        for (var attempt = 1;
             attempt <= maximumAttempts;
             attempt++)
        {
            try
            {
                Directory.Move(
                    sourceDirectory,
                    destinationDirectory);

                return;
            }
            catch (IOException exception)
                when (attempt < maximumAttempts)
            {
                lastException = exception;

                Thread.Sleep(
                    retryIntervalMilliseconds);
            }
            catch (UnauthorizedAccessException exception)
                when (attempt < maximumAttempts)
            {
                lastException = exception;

                Thread.Sleep(
                    retryIntervalMilliseconds);
            }
        }

        throw new IOException(
            "Runtimeフォルダーを移動できませんでした。" +
            Environment.NewLine +
            $"移動元: {sourceDirectory}" +
            Environment.NewLine +
            $"移動先: {destinationDirectory}",
            lastException);
    }
}
