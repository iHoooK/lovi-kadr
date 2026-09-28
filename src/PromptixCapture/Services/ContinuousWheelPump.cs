namespace PromptixCapture.Services;

internal static class ContinuousWheelPump
{
    internal static async Task RunAsync(Func<bool> running,Func<bool> canScroll,Func<int> pulse,
        Action<int> sendWheel,Func<int> intervalMs,CancellationToken cancellation)
    {
        while(running())
        {
            cancellation.ThrowIfCancellationRequested();
            if(canScroll())sendWheel(pulse());
            await Task.Delay(intervalMs(),cancellation);
        }
    }
}
