using FlowStock.Application.Inventory;
using FlowStock.Application.Notifications;
using FlowStock.Application.Production;
using FlowStock.Domain.Inventory;
using FlowStock.Domain.Notifications;
using FlowStock.UnitTests.Inventory;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowStock.UnitTests.Notifications;

/// <summary>
/// The notifications of docs/PLAN.md, section 31: what an operation reports as it happens, and
/// what the periodic scan notices about time and stock.
/// </summary>
public class NotificationServiceTests
{
    private readonly InventoryFixture _fixture = new();
    private readonly NotificationService _notifications;
    private readonly BatchService _batches;
    private readonly BillOfMaterialService _boms;
    private readonly ProductionOrderService _orders;

    public NotificationServiceTests()
    {
        _notifications = _fixture.Notifications;
        _batches = new BatchService(_fixture.Db, TimeProvider.System, NullLogger<BatchService>.Instance);
        _boms = new BillOfMaterialService(_fixture.Db, NullLogger<BillOfMaterialService>.Instance);
        _orders = new ProductionOrderService(
            _fixture.Db,
            _fixture.Movements,
            _fixture.Notifications,
            _fixture.CurrentUser,
            TimeProvider.System,
            NullLogger<ProductionOrderService>.Instance);
    }

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task ConfirmAsync(CreateStockMovementRequest request)
    {
        var draft = await _fixture.Movements.CreateAsync(request, default);

        await _fixture.Movements.ConfirmAsync(draft.Id, default);
    }

    private Task<IReadOnlyList<NotificationResponse>> NotificationsAsync(NotificationType? type = null) =>
        _notifications.ListAsync(new NotificationQuery { Type = type }, default)
            .ContinueWith(t => t.Result.Items);

    [Fact]
    public async Task A_confirmed_transfer_reports_that_its_destination_now_holds_the_goods()
    {
        await ConfirmAsync(_fixture.Receipt(_fixture.MainLocation, (_fixture.Flour, 500m)));

        // A receipt is not a transfer: nothing arrived from anywhere inside the warehouse.
        Assert.Empty(await NotificationsAsync());

        await ConfirmAsync(_fixture.Transfer(
            _fixture.MainLocation, _fixture.ProductionLocation, (_fixture.Flour, 100m)));

        var notification = Assert.Single(await NotificationsAsync());

        Assert.Equal(NotificationType.TransferReceived, notification.Type);
        Assert.Equal(_fixture.ProductionLocation.Id, notification.LocationId);
        Assert.Equal("LINE-01", notification.LocationCode);
        Assert.NotNull(notification.StockMovementNumber);
        Assert.Contains(notification.StockMovementNumber!, notification.Message);
        Assert.False(notification.IsRead);
    }

    /// <summary>A draft says nothing: only a confirmed movement has happened at all.</summary>
    [Fact]
    public async Task A_draft_transfer_reports_nothing()
    {
        await ConfirmAsync(_fixture.Receipt(_fixture.MainLocation, (_fixture.Flour, 500m)));

        await _fixture.Movements.CreateAsync(
            _fixture.Transfer(_fixture.MainLocation, _fixture.ProductionLocation, (_fixture.Flour, 100m)),
            default);

        Assert.Empty(await NotificationsAsync());
    }

    [Fact]
    public async Task A_completed_run_reports_what_it_made_and_where_it_put_it()
    {
        await ConfirmAsync(_fixture.Receipt(_fixture.ProductionLocation,
            (_fixture.Flour, 500m), (_fixture.Sugar, 200m)));

        await _boms.CreateAsync(
            new CreateBillOfMaterialRequest(_fixture.Cookies.Id, 100m, "Cookie", null,
                [new CreateBillOfMaterialItemRequest(_fixture.Flour.Id, 10m)]),
            default);

        var order = await _orders.CreateAsync(
            new CreateProductionOrderRequest(
                _fixture.Cookies.Id, 1000m, _fixture.ProductionLocation.Id, _fixture.MainLocation.Id,
                null, null, null),
            default);

        await _orders.PlanAsync(order.Id, default);
        await _orders.StartAsync(order.Id, default);

        Assert.Empty(await NotificationsAsync(NotificationType.ProductionCompleted));

        await _orders.CompleteAsync(order.Id, new CompleteProductionOrderRequest(940m, null), default);

        var notification = Assert.Single(await NotificationsAsync(NotificationType.ProductionCompleted));

        Assert.Equal(order.Id, notification.ProductionOrderId);
        Assert.Equal(order.Number, notification.ProductionOrderNumber);
        Assert.Equal(_fixture.Cookies.Id, notification.ProductId);
        Assert.Equal(_fixture.MainLocation.Id, notification.LocationId);
        Assert.Contains("940", notification.Message);
    }

    /// <summary>A lot that has gone out of date with stock still on it is nobody's operation.</summary>
    [Fact]
    public async Task The_scan_finds_an_expired_lot_that_still_holds_stock()
    {
        _fixture.Flour.IsBatchTracked = true;
        _fixture.Db.SaveChanges();

        var expired = await _batches.CreateAsync(
            new CreateBatchRequest(_fixture.Flour.Id, "FL-OLD", "Supplier A", null, Today.AddDays(-1), null),
            default);
        var fresh = await _batches.CreateAsync(
            new CreateBatchRequest(_fixture.Flour.Id, "FL-NEW", "Supplier A", null, Today.AddDays(30), null),
            default);

        await ConfirmAsync(new CreateStockMovementRequest(
            MovementType.Receipt, null, _fixture.MainLocation.Id, null,
            [
                new CreateStockMovementLineRequest(_fixture.Flour.Id, 40m, expired.Id),
                new CreateStockMovementLineRequest(_fixture.Flour.Id, 60m, fresh.Id)
            ]));

        var scan = await _notifications.ScanAsync(default);

        Assert.Equal(1, scan.ExpiredBatches);

        var notification = Assert.Single(await NotificationsAsync(NotificationType.ExpiredBatch));
        Assert.Equal(expired.Id, notification.BatchId);
        Assert.Equal("FL-OLD", notification.BatchNumber);
        Assert.Contains("40", notification.Message);

        // Running the scan again reports the same lot again to nobody: one event, one notification.
        var again = await _notifications.ScanAsync(default);

        Assert.Equal(0, again.Raised);
        Assert.Single(await NotificationsAsync(NotificationType.ExpiredBatch));
    }

    [Fact]
    public async Task An_expired_lot_nobody_holds_any_more_is_not_worth_reporting()
    {
        _fixture.Flour.IsBatchTracked = true;
        _fixture.Db.SaveChanges();

        await _batches.CreateAsync(
            new CreateBatchRequest(_fixture.Flour.Id, "FL-GONE", null, null, Today.AddDays(-5), null),
            default);

        var scan = await _notifications.ScanAsync(default);

        Assert.Equal(0, scan.ExpiredBatches);
        Assert.Empty(await NotificationsAsync(NotificationType.ExpiredBatch));
    }

    /// <summary>
    /// A draft run the shop floor cannot feed. A planned one is not short: planning reserved
    /// exactly what it needs, which is what its availability now reflects.
    /// </summary>
    [Fact]
    public async Task The_scan_finds_a_draft_run_the_line_cannot_feed()
    {
        await ConfirmAsync(_fixture.Receipt(_fixture.ProductionLocation, (_fixture.Flour, 50m)));

        await _boms.CreateAsync(
            new CreateBillOfMaterialRequest(_fixture.Cookies.Id, 100m, "Cookie", null,
                [new CreateBillOfMaterialItemRequest(_fixture.Flour.Id, 10m)]),
            default);

        var order = await _orders.CreateAsync(
            new CreateProductionOrderRequest(
                _fixture.Cookies.Id, 1000m, _fixture.ProductionLocation.Id, _fixture.MainLocation.Id,
                null, null, null),
            default);

        var scan = await _notifications.ScanAsync(default);

        Assert.Equal(1, scan.ProductionShortages);

        var notification = Assert.Single(await NotificationsAsync(NotificationType.ProductionShortage));
        Assert.Equal(order.Id, notification.ProductionOrderId);
        Assert.Equal(_fixture.Flour.Id, notification.ProductId);
        Assert.Equal(_fixture.ProductionLocation.Id, notification.LocationId);
        Assert.Contains("100", notification.Message);
        Assert.Contains("50", notification.Message);
    }

    [Fact]
    public async Task A_run_the_line_can_feed_is_not_reported_and_neither_is_a_planned_one()
    {
        await ConfirmAsync(_fixture.Receipt(_fixture.ProductionLocation, (_fixture.Flour, 500m)));

        await _boms.CreateAsync(
            new CreateBillOfMaterialRequest(_fixture.Cookies.Id, 100m, "Cookie", null,
                [new CreateBillOfMaterialItemRequest(_fixture.Flour.Id, 10m)]),
            default);

        var order = await _orders.CreateAsync(
            new CreateProductionOrderRequest(
                _fixture.Cookies.Id, 1000m, _fixture.ProductionLocation.Id, _fixture.MainLocation.Id,
                null, null, null),
            default);

        Assert.Equal(0, (await _notifications.ScanAsync(default)).ProductionShortages);

        // Once planned, the material is reserved for this very run: that is not a shortage either.
        await _orders.PlanAsync(order.Id, default);

        Assert.Equal(0, (await _notifications.ScanAsync(default)).ProductionShortages);
        Assert.Empty(await NotificationsAsync(NotificationType.ProductionShortage));
    }

    [Fact]
    public async Task A_notification_can_be_marked_read_and_unread_and_says_who_did_it()
    {
        await ConfirmAsync(_fixture.Receipt(_fixture.MainLocation, (_fixture.Flour, 500m)));
        await ConfirmAsync(_fixture.Transfer(
            _fixture.MainLocation, _fixture.ProductionLocation, (_fixture.Flour, 100m)));

        var notification = Assert.Single(await NotificationsAsync());

        var read = await _notifications.MarkReadAsync(notification.Id, isRead: true, default);
        Assert.True(read.IsRead);
        Assert.Equal(_fixture.UserId, read.ReadBy);
        Assert.NotNull(read.ReadAt);

        var unread = await _notifications.MarkReadAsync(notification.Id, isRead: false, default);
        Assert.False(unread.IsRead);
        Assert.Null(unread.ReadBy);
        Assert.Null(unread.ReadAt);

        var pending = await _notifications.ListAsync(new NotificationQuery { IsRead = false }, default);
        Assert.Single(pending.Items);

        var missing = await Assert.ThrowsAsync<NotificationNotFoundException>(
            () => _notifications.MarkReadAsync(Guid.NewGuid(), isRead: true, default));
        Assert.Equal("NOTIFICATION_NOT_FOUND", missing.Code);
    }
}
