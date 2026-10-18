using System;
using System.Threading;
using System.Windows.Forms;

namespace DiskPulse
{
    internal static class Program
    {
        private const string MutexName = "Local\\DiskPulse.SingleInstance";
        private const string ShowEventName = "Local\\DiskPulse.ShowWindow";

        [STAThread]
        private static void Main(string[] args)
        {
            bool silent = HasArg(args, "--silent");
            bool recordOnly = HasArg(args, "--record");
            bool noAutostart = HasArg(args, "--no-autostart");
            string exportPath = GetArgValue(args, "--export");
            bool exportRequested = HasArg(args, "--export") || !string.IsNullOrEmpty(exportPath);
            string importPath = GetArgValue(args, "--import");
            bool importRequested = HasArg(args, "--import") || !string.IsNullOrEmpty(importPath);

            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    if (exportRequested && !string.IsNullOrEmpty(exportPath))
                    {
                        ExportOnly(exportPath);
                        return;
                    }
                    if (importRequested && !string.IsNullOrEmpty(importPath))
                    {
                        try
                        {
                            AppPaths.Ensure();
                            Logger.Initialize();
                            Logger.Error("已有 DiskPulse 实例正在运行，无法导入；请先退出 DiskPulse 再执行导入", null);
                        }
                        catch
                        {
                        }
                        Environment.ExitCode = 3;
                        return;
                    }
                    if (!silent && !recordOnly) SignalExistingInstance();
                    return;
                }

                EventWaitHandle showEvent = null;
                try
                {
                    AppPaths.Ensure();
                    Logger.Initialize();
                    Logger.Info("启动 DiskPulse v" + AppPaths.Version + "，参数: " + string.Join(" ", args));
                    Logger.Info("数据目录: " + AppPaths.Root);

                    if (exportRequested)
                    {
                        if (string.IsNullOrEmpty(exportPath))
                        {
                            Logger.Error("--export 需要指定输出文件路径", null);
                            Environment.ExitCode = 2;
                            return;
                        }
                        ExportOnly(exportPath);
                        return;
                    }

                    if (importRequested)
                    {
                        if (string.IsNullOrEmpty(importPath))
                        {
                            Logger.Error("--import 需要指定输入文件路径", null);
                            Environment.ExitCode = 2;
                            return;
                        }
                        ImportOnly(importPath);
                        return;
                    }

                    HistoryStore store = new HistoryStore(AppPaths.HistoryFile);
                    store.Load();

                    AppSettings settings = AppSettings.Load();
                    if (!noAutostart)
                    {
                        AutoStart.Ensure(settings.AutoStart);
                    }

                    DiskSampler sampler = new DiskSampler(store);
                    sampler.RecordNow("启动");

                    if (recordOnly) return;

                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                    Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
                    {
                        Logger.Error("界面线程发生未处理异常", e.Exception);
                    };
                    AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                    {
                        Logger.Error("后台线程发生未处理异常", e.ExceptionObject as Exception);
                    };

                    showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

                    using (MainForm form = new MainForm(store, sampler, settings, silent))
                    {
                        TrayApplicationContext context = new TrayApplicationContext(form);
                        form.AttachContext(context);
                        form.EnsureHandleCreated();
                        StartShowListener(form, showEvent);
                        if (!silent) form.ShowMainWindow(false);
                        Application.Run(context);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("程序运行失败", ex);
                    if (!silent && !recordOnly)
                    {
                        try
                        {
                            MessageBox.Show("DiskPulse 启动失败：\n" + ex.Message + "\n\n日志位置：" + AppPaths.LogDirectory,
                                "DiskPulse", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        catch
                        {
                        }
                    }
                }
                finally
                {
                    if (showEvent != null) showEvent.Dispose();
                    try { mutex.ReleaseMutex(); }
                    catch { }
                }
            }
        }

        private static void ExportOnly(string exportPath)
        {
            try
            {
                AppPaths.Ensure();
                Logger.Initialize();
                HistoryStore store = new HistoryStore(AppPaths.HistoryFile);
                store.Load();
                int rows = CsvExporter.Export(store, exportPath);
                Logger.Info("已导出 " + rows + " 条记录到 " + exportPath);
            }
            catch (Exception ex)
            {
                Logger.Error("导出 CSV 失败", ex);
                Environment.ExitCode = 1;
            }
        }

        private static void ImportOnly(string importPath)
        {
            try
            {
                AppPaths.Ensure();
                Logger.Initialize();
                HistoryStore store = new HistoryStore(AppPaths.HistoryFile);
                store.Load();
                CsvImportResult result = CsvImporter.Import(store, importPath);
                Logger.Info("导入 CSV: " + importPath + " 有效 " + result.ValidRows + " 行，新增 " +
                    result.ImportedDays + " 天，更新 " + result.UpdatedDays + " 天，跳过 " + result.Errors.Count + " 行");
                for (int i = 0; i < result.Errors.Count && i < 8; i++)
                {
                    Logger.Error("导入跳过: " + result.Errors[i], null);
                }
                if (result.ValidRows == 0) Environment.ExitCode = 1;
            }
            catch (Exception ex)
            {
                Logger.Error("导入 CSV 失败", ex);
                Environment.ExitCode = 1;
            }
        }

        private static bool HasArg(string[] args, string name)
        {
            if (args == null) return false;
            foreach (string arg in args)
            {
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)) return true;
                if (arg != null && arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string GetArgValue(string[] args, string name)
        {
            if (args == null) return null;
            string prefix = name + "=";
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == null) continue;
                if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return arg.Substring(prefix.Length).Trim('"');
                }
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    return args[i + 1].Trim('"');
                }
            }
            return null;
        }

        private static void SignalExistingInstance()
        {
            for (int attempt = 0; attempt < 25; attempt++)
            {
                try
                {
                    using (EventWaitHandle show = EventWaitHandle.OpenExisting(ShowEventName))
                    {
                        show.Set();
                        return;
                    }
                }
                catch
                {
                    Thread.Sleep(100);
                }
            }
        }

        private static void StartShowListener(MainForm form, EventWaitHandle showEvent)
        {
            Thread thread = new Thread(delegate()
            {
                while (true)
                {
                    try
                    {
                        if (!showEvent.WaitOne(1000))
                        {
                            if (form.IsDisposed) return;
                            continue;
                        }
                        if (form.IsDisposed) return;
                        form.BeginInvoke(new Action<bool>(form.ShowMainWindow), true);
                    }
                    catch
                    {
                        return;
                    }
                }
            });
            thread.IsBackground = true;
            thread.Name = "DiskPulse.ShowListener";
            thread.Start();
        }
    }
}
