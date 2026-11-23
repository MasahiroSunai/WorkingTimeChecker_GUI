using System.IO;
using System.Windows;
using WorkingTimeChecker.Core.Application;

namespace WorkingTimeChecker.Gui;

/// <summary>
/// GUIアプリケーションの起動処理を管理します。
/// </summary>
public partial class App : System.Windows.Application
{
    /// <summary>
    /// LocalAppDataとRuntimeを初期化してから
    /// メイン画面を表示します。
    /// </summary>
    /// <param name="e">
    /// 起動イベントの情報です。
    /// </param>
    protected override void OnStartup(
        StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var startupMessages =
                new List<string>();

            var defaultConfigFilePath =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Defaults",
                    "config.example.yaml");

            var configCopied =
                ApplicationDataInitializer.Initialize(
                    defaultConfigFilePath);

            var payloadDirectory =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Payload");

            var runtimeManifestPath =
                Path.Combine(
                    payloadDirectory,
                    "runtime-manifest.json");

            var runtimeArchivePath =
                Path.Combine(
                    payloadDirectory,
                    "runtime.zip");

            startupMessages.Add(
                "[INFO] Runtimeの確認を開始します。");

            var runtimeInstaller =
                new RuntimeInstaller();

            var installResult =
                runtimeInstaller.InstallOrUpdate(
                    runtimeManifestPath,
                    runtimeArchivePath,
                    message =>
                        startupMessages.Add(
                            message));

            var runtimeResolver =
                new RuntimeResolver();

            var activeCliPath =
                runtimeResolver.GetActiveCliPath();

            if (!string.Equals(
                    activeCliPath,
                    installResult.CliExecutablePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "導入したRuntimeと有効なRuntimeが" +
                    "一致しません。");
            }

            var mainWindow =
                new MainWindow(
                    configCopied,
                    installResult,
                    startupMessages);

            MainWindow =
                mainWindow;

            mainWindow.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                "アプリケーションの初期化に失敗しました。" +
                Environment.NewLine +
                Environment.NewLine +
                exception.Message,
                "起動エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(
                exitCode: 1);
        }
    }
}