using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace ZapretGui.Services;

public static class SingleInstanceService
{
    private const string MutexName = "ZapretControl_SingleInstance_Mutex";
    private const string PipeName  = "ZapretControl_SingleInstance_Pipe";

    private static Mutex? _mutex;
    private static CancellationTokenSource? _cts;

    public static bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        return createdNew;
    }

    public static void SendShowSignal()
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".", PipeName, PipeDirection.Out, PipeOptions.None);
            client.Connect(1500);

            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.Write("show");
        }
        catch {}
    }

    public static void StartListening(Action onShow)
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(server);
                    var msg = await reader.ReadToEndAsync();

                    if (msg.Trim().Equals("show", StringComparison.OrdinalIgnoreCase))
                        onShow();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(200, token);
                }
            }
        }, token);
    }

    public static void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _mutex?.ReleaseMutex(); } catch { }
        try { _mutex?.Dispose(); } catch { }
    }
}
