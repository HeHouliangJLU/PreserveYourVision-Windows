using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace BreaksForEyes.Services;

/// <summary>
/// 开机自启（FR-8）：HKCU\Software\Microsoft\Windows\CurrentVersion\Run。
/// 不需要管理员权限，关闭时删除对应键值。
/// </summary>
public static class StartupRegistrar
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BreaksForEyes";

    public static bool TrySetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return false;
            }

            if (enabled)
            {
                var command = BuildLaunchCommand();
                if (string.IsNullOrWhiteSpace(command))
                {
                    return false;
                }

                key.SetValue(ValueName, command, RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 生成自启命令行。常规运行使用进程可执行文件；若通过 `dotnet xxx.dll` 启动，
    /// 则记录 dotnet + 程序集路径，保证登录后仍能启动。
    /// </summary>
    private static string BuildLaunchCommand()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return "";
        }

        var processName = Path.GetFileNameWithoutExtension(processPath);
        if (string.Equals(processName, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            // 避免使用 Assembly.Location：在单文件发布中它始终为空（IL3000）。
            // AppContext.BaseDirectory 指向入口程序集所在目录。
            var entryName = Assembly.GetEntryAssembly()?.GetName().Name;
            if (string.IsNullOrWhiteSpace(entryName))
            {
                return "";
            }

            var entryAssemblyPath = Path.Combine(AppContext.BaseDirectory, entryName + ".dll");
            return $"\"{processPath}\" \"{entryAssemblyPath}\"";
        }

        return $"\"{processPath}\"";
    }
}
