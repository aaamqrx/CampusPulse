using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using CampusPulse.Core;
using Microsoft.Extensions.Hosting;

namespace CampusPulse.Service;

internal sealed class ControlPipeServer(ConnectionWorker worker) : BackgroundService
{
    private const int MaximumMessageBytes = 16 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(stoppingToken);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(15));
                await ServeOneAsync(pipe, deadline.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) when (error is IOException or JsonException or DecoderFallbackException or
                   InvalidDataException or OperationCanceledException or UnauthorizedAccessException)
            {
                // A malformed, disconnected, or slow client cannot stop the service. No request data is logged.
            }
        }
    }

    private async Task ServeOneAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        while (true)
        {
            int count = await pipe.ReadAsync(chunk, token);
            if (count == 0) return;
            int end = Array.IndexOf(chunk, (byte)'\n', 0, count);
            int length = end < 0 ? count : end;
            if (buffer.Length + length > MaximumMessageBytes)
            {
                await ReplyAsync(pipe, new(false, "请求过长"), token);
                return;
            }
            buffer.Write(chunk, 0, length);
            if (end < 0) continue;
            ServiceReply reply;
            try
            {
                string json = StrictUtf8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
                var request = JsonSerializer.Deserialize<ServiceRequest>(json);
                reply = request is null ? new(false, "请求格式不正确") : await worker.HandleAsync(request, token);
            }
            catch (Exception error) when (error is JsonException or DecoderFallbackException)
            {
                reply = new(false, "请求格式不正确");
            }
            await ReplyAsync(pipe, reply, token);
            return;
        }
    }

    private static async Task ReplyAsync(NamedPipeServerStream pipe, ServiceReply reply, CancellationToken token)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(reply);
        if (bytes.Length > MaximumMessageBytes)
            bytes = JsonSerializer.SerializeToUtf8Bytes(new ServiceReply(false, "状态过长，请稍后刷新"));
        await pipe.WriteAsync(bytes, token);
        await pipe.WriteAsync(new byte[] { (byte)'\n' }, token);
        await pipe.FlushAsync(token);
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(sid, null),
                PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(ProductInfo.PipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security,
            HandleInheritability.None, (PipeAccessRights)0);
    }
}
