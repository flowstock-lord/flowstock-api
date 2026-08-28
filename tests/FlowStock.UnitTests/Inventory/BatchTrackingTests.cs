using FlowStock.Application.Inventory;
using FlowStock.Domain.Catalog;
using FlowStock.Domain.Inventory;
using FlowStock.Domain.Warehouses;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowStock.UnitTests.Inventory;

/// <summary>
/// Batch tracking over the warehouse of <see cref="InventoryFixture"/>: flour is kept lot by lot,
/// sugar is not, and the two live side by side (docs/PLAN.md, section 20).
/// </summary>
public class BatchTrackingTests
{
    private readonly InventoryFixture _fixture = new();
    private readonly BatchService _batches;

    public BatchTrackingTests()
    {
        // Flour is bought in lots with expiry dates; sugar is not tracked at all.
        _fixture.Flour.IsBatchTracked = true;
        _fixture.Db.SaveChanges();

        _batches = new BatchService(_fixture.Db, TimeProvider.System, NullLogger<BatchService>.Instance);
    }

    private StockMovementService Movements => _fixture.Movements;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private Task<BatchResponse> RegisterAsync(string number, DateOnly? expiry = null, Product? product = null) =>
        _batches.CreateAsync(
            new CreateBatchRequest(
                (product ?? _fixture.Flour).Id, number, "Supplier A", Today.AddDays(-1), expiry, null),
            default);

    private async Task ReceiveAsync(StorageLocation destination, params (Product Product, decimal Quantity, Guid? BatchId)[] lines)
    {
        var receipt = await Movements.CreateAsync(
            new CreateStockMovementRequest(MovementType.Receipt, null, destination.Id, "Supplier delivery",
                lines.Select(l => new CreateStockMovementLineRequest(l.Product.Id, l.Quantity, l.BatchId)).ToList()),
            default);

        await Movements.ConfirmAsync(receipt.Id, default);
    }

    private decimal QuantityOf(Product product, StorageLocation location, Guid? batchId)
    {
        using var db = _fixture.NewContext();

        return db.Stocks
            .Where(s => s.ProductId == product.Id && s.LocationId == location.Id && s.BatchId == batchId)
            .Select(s => s.Quantity)
            .SingleOrDefault();
    }

    [Fact]
    public async Task A_batch_belongs_to_one_product_and_its_number_is_unique_there()
    {
        var batch = await RegisterAsync("fl-2026-0828", Today.AddDays(180));

        // Normalized upper-case, like every other business identifier in the system.
        Assert.Equal("FL-2026-0828", batch.Number);
        Assert.Equal("Supplier A", batch.Supplier);
        Assert.False(batch.IsExpired);

        var duplicate = await Assert.ThrowsAsync<BatchNumberAlreadyExistsException>(
            () => RegisterAsync("FL-2026-0828"));
        Assert.Equal("BATCH_NUMBER_EXISTS", duplicate.Code);

        // A product nobody tracks has no lots to register.
        var untracked = await Assert.ThrowsAsync<BatchNotAllowedException>(
            () => RegisterAsync("SU-2026-0001", product: _fixture.Sugar));
        Assert.Equal("BATCH_NOT_ALLOWED", untracked.Code);
    }

    [Fact]
    public async Task A_batch_cannot_expire_before_it_was_produced()
    {
        var exception = await Assert.ThrowsAsync<BatchInvalidException>(
            () => _batches.CreateAsync(
                new CreateBatchRequest(
                    _fixture.Flour.Id, "FL-BAD", null, Today, Today.AddDays(-10), null),
                default));

        Assert.Equal("BATCH_INVALID", exception.Code);
    }

    /// <summary>Every lot is its own balance, so "how much of FL-0828 is on the line" is a row.</summary>
    [Fact]
    public async Task Stock_of_a_tracked_product_is_kept_lot_by_lot()
    {
        var first = await RegisterAsync("FL-0828", Today.AddDays(180));
        var second = await RegisterAsync("FL-0901", Today.AddDays(200));

        await ReceiveAsync(_fixture.MainLocation,
            (_fixture.Flour, 500m, first.Id),
            (_fixture.Flour, 300m, second.Id),
            (_fixture.Sugar, 200m, null));

        Assert.Equal(500m, QuantityOf(_fixture.Flour, _fixture.MainLocation, first.Id));
        Assert.Equal(300m, QuantityOf(_fixture.Flour, _fixture.MainLocation, second.Id));

        // The untracked product keeps its single anonymous balance.
        Assert.Equal(200m, QuantityOf(_fixture.Sugar, _fixture.MainLocation, null));

        var stock = await _fixture.StockReader.ListAsync(
            new StockQuery { ProductId = _fixture.Flour.Id }, default);

        Assert.Equal(["FL-0828", "FL-0901"], stock.Items.Select(s => s.BatchNumber).Order());
        Assert.All(stock.Items, s => Assert.NotNull(s.BatchExpiryDate));
    }

    [Fact]
    public async Task A_transfer_moves_the_lot_it_names_and_leaves_the_others_alone()
    {
        var first = await RegisterAsync("FL-0828");
        var second = await RegisterAsync("FL-0901");

        await ReceiveAsync(_fixture.MainLocation,
            (_fixture.Flour, 500m, first.Id),
            (_fixture.Flour, 300m, second.Id));

        var transfer = await Movements.CreateAsync(
            new CreateStockMovementRequest(
                MovementType.Transfer, _fixture.MainLocation.Id, _fixture.ProductionLocation.Id, null,
                [new CreateStockMovementLineRequest(_fixture.Flour.Id, 100m, second.Id)]),
            default);

        await Movements.ConfirmAsync(transfer.Id, default);

        Assert.Equal(500m, QuantityOf(_fixture.Flour, _fixture.MainLocation, first.Id));
        Assert.Equal(200m, QuantityOf(_fixture.Flour, _fixture.MainLocation, second.Id));
        Assert.Equal(100m, QuantityOf(_fixture.Flour, _fixture.ProductionLocation, second.Id));
        Assert.Equal(0m, QuantityOf(_fixture.Flour, _fixture.ProductionLocation, first.Id));
    }

    /// <summary>Two lots of the same product in one document are two lines, not a duplicate.</summary>
    [Fact]
    public async Task One_document_may_move_two_lots_of_the_same_product()
    {
        var first = await RegisterAsync("FL-0828");
        var second = await RegisterAsync("FL-0901");

        await ReceiveAsync(_fixture.MainLocation,
            (_fixture.Flour, 500m, first.Id),
            (_fixture.Flour, 300m, second.Id));

        var movement = await Movements.CreateAsync(
            new CreateStockMovementRequest(
                MovementType.Transfer, _fixture.MainLocation.Id, _fixture.ProductionLocation.Id, null,
                [
                    new CreateStockMovementLineRequest(_fixture.Flour.Id, 200m, first.Id),
                    new CreateStockMovementLineRequest(_fixture.Flour.Id, 100m, second.Id)
                ]),
            default);

        await Movements.ConfirmAsync(movement.Id, default);

        Assert.Equal(200m, QuantityOf(_fixture.Flour, _fixture.ProductionLocation, first.Id));
        Assert.Equal(100m, QuantityOf(_fixture.Flour, _fixture.ProductionLocation, second.Id));

        // The same lot twice is still ambiguous, and still rejected.
        var ambiguous = await Assert.ThrowsAsync<InvalidMovementException>(
            () => Movements.CreateAsync(
                new CreateStockMovementRequest(
                    MovementType.Transfer, _fixture.MainLocation.Id, _fixture.ProductionLocation.Id, null,
                    [
                        new CreateStockMovementLineRequest(_fixture.Flour.Id, 10m, first.Id),
                        new CreateStockMovementLineRequest(_fixture.Flour.Id, 20m, first.Id)
                    ]),
                default));

        Assert.Equal("INVALID_MOVEMENT", ambiguous.Code);
    }

    [Fact]
    public async Task A_tracked_product_never_moves_anonymously_and_an_untracked_one_never_names_a_lot()
    {
        var batch = await RegisterAsync("FL-0828");

        var missing = await Assert.ThrowsAsync<BatchRequiredException>(
            () => Movements.CreateAsync(
                _fixture.Receipt(_fixture.MainLocation, (_fixture.Flour, 100m)), default));
        Assert.Equal("BATCH_REQUIRED", missing.Code);

        var unwanted = await Assert.ThrowsAsync<BatchNotAllowedException>(
            () => Movements.CreateAsync(
                new CreateStockMovementRequest(MovementType.Receipt, null, _fixture.MainLocation.Id, null,
                    [new CreateStockMovementLineRequest(_fixture.Sugar.Id, 100m, batch.Id)]),
                default));
        Assert.Equal("BATCH_NOT_ALLOWED", unwanted.Code);
    }

    [Fact]
    public async Task A_lot_of_another_product_cannot_be_used()
    {
        _fixture.Cookies.IsBatchTracked = true;
        _fixture.Db.SaveChanges();

        var cookieBatch = await RegisterAsync("CK-0001", product: _fixture.Cookies);

        var exception = await Assert.ThrowsAsync<BatchInvalidException>(
            () => Movements.CreateAsync(
                new CreateStockMovementRequest(MovementType.Receipt, null, _fixture.MainLocation.Id, null,
                    [new CreateStockMovementLineRequest(_fixture.Flour.Id, 100m, cookieBatch.Id)]),
                default));

        Assert.Equal("BATCH_INVALID", exception.Code);
    }

    /// <summary>A shortage is of one lot, and the error says which one.</summary>
    [Fact]
    public async Task Taking_more_than_one_lot_holds_is_rejected_even_when_another_lot_has_plenty()
    {
        var small = await RegisterAsync("FL-0828");
        var large = await RegisterAsync("FL-0901");

        await ReceiveAsync(_fixture.MainLocation,
            (_fixture.Flour, 50m, small.Id),
            (_fixture.Flour, 900m, large.Id));

        var transfer = await Movements.CreateAsync(
            new CreateStockMovementRequest(
                MovementType.Transfer, _fixture.MainLocation.Id, _fixture.ProductionLocation.Id, null,
                [new CreateStockMovementLineRequest(_fixture.Flour.Id, 100m, small.Id)]),
            default);

        var exception = await Assert.ThrowsAsync<InsufficientStockException>(
            () => Movements.ConfirmAsync(transfer.Id, default));

        Assert.Equal("INSUFFICIENT_STOCK", exception.Code);
        Assert.Equal(50m, exception.Details!["available"]);
        Assert.Equal("FL-0828", exception.Details["batchNumber"]);
        Assert.Contains("FL-0828", exception.Message);
    }

    [Fact]
    public async Task An_expired_lot_is_reported_as_expired_and_can_be_filtered_out()
    {
        var expired = await RegisterAsync("FL-OLD", Today.AddDays(-1));
        var fresh = await RegisterAsync("FL-NEW", Today.AddDays(30));

        Assert.True((await _batches.GetAsync(expired.Id, default)).IsExpired);
        Assert.False((await _batches.GetAsync(fresh.Id, default)).IsExpired);

        var expiredOnly = await _batches.ListAsync(new BatchQuery { IsExpired = true }, default);
        Assert.Equal("FL-OLD", Assert.Single(expiredOnly.Items).Number);

        var soon = await _batches.ListAsync(new BatchQuery { ExpiringBefore = Today.AddDays(7) }, default);
        Assert.Equal("FL-OLD", Assert.Single(soon.Items).Number);

        // Soonest expiry first: the order the shelf should be emptied in.
        var all = await _batches.ListAsync(new BatchQuery(), default);
        Assert.Equal(["FL-OLD", "FL-NEW"], all.Items.Select(b => b.Number));
    }
}
