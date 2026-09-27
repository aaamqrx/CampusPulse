using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using CampusPulse.Core;

namespace CampusPulse.App;

/// <summary>Local, bounded request/reply transport. Never logs requests or their contents.</summary>
internal sealed class ControlClient
{
    internal const int MaximumMessageBytes = 16 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<ServiceReply> SendAsync(ServiceRequest request, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request);
        if (bytes.Length > MaximumMessageBytes)
            throw new InvalidDataException("设置内容过长，请缩短输入。");

        using var pipe = new NamedPipeClientStream(".", ProductInfo.PipeName,
            PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        using var connectionDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectionDeadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await pipe.ConnectAsync(connectionDeadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("连接后台超时，请检查服务是否正在运行。");
        }

        using var messageDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        messageDeadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await pipe.WriteAsync(bytes, messageDeadline.Token);
            await pipe.WriteAsync(new byte[] { (byte)'\n' }, messageDeadline.Token);
            await pipe.FlushAsync(messageDeadline.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[1024];
            while (true)
            {
                var count = await pipe.ReadAsync(chunk, messageDeadline.Token);
                if (count == 0)
                    throw new InvalidDataException("后台连接中断，请重试。");
                var newline = Array.IndexOf(chunk, (byte)'\n', 0, count);
                var payloadCount = newline < 0 ? count : newline;
                if (buffer.Length + payloadCount > MaximumMessageBytes)
                    throw new InvalidDataException("后台响应超过允许长度。");
                buffer.Write(chunk, 0, payloadCount);
                if (newline >= 0)
                {
                    var json = StrictUtf8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
                    return JsonSerializer.Deserialize<ServiceReply>(json)
                        ?? throw new InvalidDataException("后台未返回有效状态。");
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("后台响应超时。操作可能已生效，请刷新状态后确认。");
        }
        finally
        {
            // Reduce the lifetime of the request byte buffer (which may contain a password).
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
