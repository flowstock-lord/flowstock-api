using FlowStock.Domain.Common;

namespace FlowStock.Domain.Notifications;

public class NotificationNotFoundException(Guid notificationId)
    : DomainException("NOTIFICATION_NOT_FOUND", $"Notification '{notificationId}' was not found.",
        new Dictionary<string, object?> { ["notificationId"] = notificationId });
