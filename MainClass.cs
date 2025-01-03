public class HighPrecisionTimer : IDisposable
{
    private delegate void TimerCallback(uint uTimerID, uint uMsg, IntPtr dwUser, uint dw1, uint dw2);
    private TimerCallback _callback;
    private uint _timerId;

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern uint timeSetEvent(uint msDelay, uint msResolution, TimerCallback callback, IntPtr userCtx, uint eventType);

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern uint timeKillEvent(uint uTimerID);

    public HighPrecisionTimer(Action action, uint interval)
    {
        _callback = (id, msg, user, dw1, dw2) => action();
        _timerId = timeSetEvent(interval, 1, _callback, IntPtr.Zero, 1);
        if (_timerId == 0)
        {
            DI.Resolve<ILog>().Error("timeSetEvent failed");
        }
    }

    public void Dispose()
    {
        timeKillEvent(_timerId);
    }
}

public class PeriodicTaskManager : IDisposable
{
    private readonly List<(Action Task, int Interval, CancellationTokenSource TokenSource)> _tasks = new List<(Action, int, CancellationTokenSource)>();

    // 添加任务
    public void AddTask(Action task, int interval)
    {
        var cts = new CancellationTokenSource();
        _tasks.Add((task, interval, cts));

        // 启动周期性任务
        Task.Run(() => ExecuteTaskPeriodically(task, interval, cts.Token));
    }

    // 执行周期性任务的内部方法
    private async Task ExecuteTaskPeriodically(Action task, int interval, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            task();
            await Task.Delay(interval, token); // 每 interval 毫秒执行一次任务
        }
    }

    // 停止所有任务
    public void StopAllTasks()
    {
        foreach (var (task, interval, cts) in _tasks)
        {
            cts.Cancel(); // 取消每个任务的 CancellationToken
        }
    }

    // 清理资源
    public void Dispose()
    {
        StopAllTasks();
    }
}

public class EventLoop
{
    // 任务列表中的任务既可以是同步的也可以是异步的
    private List<(Func<Task> Task, TimeSpan Interval, DateTime LastRun)> _tasks = new();
    ILog _log = DI.Resolve<ILog>();
    // 添加同步任务
    public void AddTask(Action task, TimeSpan interval)
    {
        // 将同步任务包装为异步任务
        _tasks.Add((() =>
        {
            task();
            return Task.CompletedTask;
        }, interval, DateTime.MinValue));
    }

    // 添加异步任务
    public void AddTask(Func<Task> task, TimeSpan interval)
    {
        _tasks.Add((task, interval, DateTime.MinValue));
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {

            foreach (var (task, interval, lastRun) in _tasks.ToList())
            {
                var time = DateTime.UtcNow;
                if (DateTime.UtcNow - lastRun >= interval)
                {
                    try
                    {
                        _log.Info($"Executing task{task.Method.Name}");
                        // 执行任务，无论同步或异步
                        task().Wait(1000, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        // 处理任务执行中的异常
                        _log.Error($"Error executing task: {ex.Message}");
                    }
                    // 更新任务的上次执行时间
                    var index = _tasks.IndexOf((task, interval, lastRun));
                    _tasks[index] = (task, interval, time);
                }
            }

            // 延迟，避免过度轮询
            await Task.Delay(20, cancellationToken);
        }
    }
}
