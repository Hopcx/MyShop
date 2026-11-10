using System.Net.Mail;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MyShop.Api.Common;
using MyShop.Api.Domain;
using MyShop.Api.Infrastructure;
using MyShop.Api.Modules.Catalog;
using Prometheus;

namespace MyShop.Api.Modules.Notifications;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct);
}

public class LoggingEmailSender(ILogger<LoggingEmailSender> log) : IEmailSender
{
    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct)
    {
        log.LogInformation("[email] to={To} subject={Subject}", LogMasking.MaskEmail(to), subject);
        return Task.CompletedTask;
    }
}

public class SmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = "no-reply@myshop.local";
}

public class SmtpEmailSender(SmtpOptions opt) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct)
    {
        using var client = new SmtpClient(opt.Host, opt.Port) { EnableSsl = true };
        if (opt.User is not null) client.Credentials = new System.Net.NetworkCredential(opt.User, opt.Password);
        using var msg = new MailMessage(opt.From, to, subject, htmlBody) { IsBodyHtml = true };
        await client.SendMailAsync(msg, ct);
    }
}

/// <summary>
/// Outbox dispatcher: đọc sự kiện chưa xử lý, gọi handler (email, web notification, reindex search, đồng bộ kho),
/// retry với giới hạn số lần. Chạy được trên nhiều replica nhờ xử lý idempotent theo Id.
/// </summary>
public class OutboxDispatcher(IServiceScopeFactory scopes, ILogger<OutboxDispatcher> log) : BackgroundService
{
    private const int MaxAttempts = 5;
    private static readonly Counter Processed = Metrics.CreateCounter("myshop_outbox_processed_total", "Outbox messages processed", "type", "result");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var n = await ProcessBatchAsync(stoppingToken);
                if (n == 0) await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                log.LogError(ex, "Outbox loop failed");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        var batch = await db.Outbox.Where(m => m.ProcessedAt == null && m.Attempts < MaxAttempts)
            .OrderBy(m => m.OccurredAt).Take(50).ToListAsync(ct);
        foreach (var msg in batch)
        {
            try
            {
                await HandleAsync(scope.ServiceProvider, db, msg, ct);
                msg.ProcessedAt = DateTime.UtcNow;
                msg.Error = null;
                Processed.WithLabels(msg.Type, "ok").Inc();
            }
            catch (Exception ex)
            {
                msg.Attempts++;
                msg.Error = ex.Message[..Math.Min(ex.Message.Length, 1000)];
                Processed.WithLabels(msg.Type, "error").Inc();
                log.LogWarning(ex, "Outbox message {Id} ({Type}) failed, attempt {Attempt}", msg.Id, msg.Type, msg.Attempts);
            }
            await db.SaveChangesAsync(ct);
        }
        return batch.Count;
    }

    private static async Task HandleAsync(IServiceProvider sp, ShopDbContext db, OutboxMessage msg, CancellationToken ct)
    {
        var payload = JsonDocument.Parse(msg.Payload).RootElement;
        var email = sp.GetRequiredService<IEmailSender>();
        switch (msg.Type)
        {
            case "OrderCreated":
            {
                var number = payload.GetProperty("Number").GetString()!;
                await NotifyAsync(db, email, payload, $"Đã nhận đơn hàng {number}",
                    $"<p>Cảm ơn bạn! Đơn hàng <b>{number}</b> đã được tạo, tổng {payload.GetProperty("Total").GetDecimal():N0}đ.</p>", ct);
                break;
            }
            case "OrderStatusChanged":
            {
                var number = payload.GetProperty("Number").GetString()!;
                var status = payload.GetProperty("Status").GetString() switch
                {
                    "Confirmed" => "đã được xác nhận",
                    "Shipping" => "đang được giao",
                    "Completed" => "đã giao thành công",
                    "Cancelled" => "đã bị hủy",
                    var s => s,
                };
                await NotifyAsync(db, email, payload, $"Đơn hàng {number} {status}", $"<p>Đơn hàng <b>{number}</b> {status}.</p>", ct);
                break;
            }
            case "UserRegistered":
                await email.SendAsync(payload.GetProperty("Email").GetString()!, "Chào mừng đến với MyShop",
                    "<p>Tài khoản của bạn đã được tạo.</p>", ct);
                break;
            case "ProductChanged":
                await sp.GetRequiredService<IProductSearch>().IndexAsync(payload.GetProperty("ProductId").GetGuid(), ct);
                break;
            case "InventoryChanged":
                // Điểm tích hợp WMS/ERP: publish lên message broker (Kafka/RabbitMQ/Azure Service Bus) ở production.
                break;
        }
    }

    private static async Task NotifyAsync(ShopDbContext db, IEmailSender email, JsonElement payload, string subject, string body, CancellationToken ct)
    {
        var to = payload.GetProperty("Email").GetString()!;
        Guid? userId = payload.TryGetProperty("UserId", out var u) && u.ValueKind == JsonValueKind.String ? u.GetGuid() : null;
        db.Notifications.Add(new Notification { UserId = userId, Recipient = to, Subject = subject, Body = body, Channel = "web+email" });
        await email.SendAsync(to, subject, body, ct);
    }
}
