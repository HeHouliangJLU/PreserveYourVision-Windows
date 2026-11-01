using System.Threading;
using System.Windows.Threading;

namespace PreserveYourVision.Services;

/// <summary>
/// 命名 Mutex 单实例（DESIGN.md §8.2.5）。第二个实例通过命名事件通知已运行实例
/// 打开设置窗口，然后自行退出。
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private static readonly string ObjectName =
        "PreserveYourVision_" + SanitizeName(Environment.UserName);

    private Mutex? _mutex;
    private EventWaitHandle? _wakeEvent;
    private Thread? _listenerThread;
    private volatile bool _disposed;

    public bool IsFirstInstance { get; private set; }

    public bool TryAcquire()
    {
        try
        {
            // 只需要"命名对象是否存在"来判定单实例，不实际持有 Mutex 所有权，
            // 避免跨线程 ReleaseMutex 的约束。
            _mutex = new Mutex(initiallyOwned: false, ObjectName, out bool createdNew);
            IsFirstInstance = createdNew;
            if (!createdNew)
            {
                _mutex.Dispose();
                _mutex = null;
            }

            return createdNew;
        }
        catch
        {
            // 极端情况下（命名对象异常）以单实例优先：视为首个实例继续运行。
            IsFirstInstance = true;
            return true;
        }
    }

    /// <summary>第一个实例在 UI 线程上监听第二个实例的唤醒信号。</summary>
    public void StartSignalListener(Dispatcher dispatcher, Action onSecondInstance)
    {
        if (!IsFirstInstance || _disposed)
        {
            return;
        }

        try
        {
            _wakeEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                ObjectName + "_ShowSettings");

            _listenerThread = new Thread(() =>
            {
                while (!_disposed)
                {
                    try
                    {
                        if (!_wakeEvent.WaitOne(500))
                        {
                            continue;
                        }
                    }
                    catch
                    {
                        return;
                    }

                    if (_disposed)
                    {
                        return;
                    }

                    try
                    {
                        dispatcher.BeginInvoke(onSecondInstance);
                    }
                    catch
                    {
                        // 应用退出过程中 Dispatcher 可能已关闭。
                        return;
                    }
                }
            })
            {
                IsBackground = true,
                Name = "PreserveYourVision.SingleInstanceListener"
            };

            _listenerThread.Start();
        }
        catch
        {
            // 监听失败不影响主功能。
        }
    }

    /// <summary>第二个实例调用：唤醒已运行实例并请求其打开设置。</summary>
    public void SignalExistingInstance()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var handle = EventWaitHandle.OpenExisting(ObjectName + "_ShowSettings");
                handle.Set();
                return;
            }
            catch
            {
                Thread.Sleep(120);
            }
        }
    }

    private static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "User";
        }

        var chars = name.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray();
        return new string(chars);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _wakeEvent?.Set();
        }
        catch
        {
            // 忽略。
        }

        try
        {
            _wakeEvent?.Dispose();
        }
        catch
        {
            // 忽略。
        }

        try
        {
            _mutex?.Dispose();
        }
        catch
        {
            // 忽略。
        }
    }
}
