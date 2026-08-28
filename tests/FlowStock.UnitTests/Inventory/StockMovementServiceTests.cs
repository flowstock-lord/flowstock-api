using FlowStock.Application.Inventory;
using FlowStock.Domain.Catalog;
using FlowStock.Domain.Inventory;
using FlowStock.Domain.Warehouses;

namespace FlowStock.UnitTests.Inventory;

public class StockMovementServiceTests
{
    private readonly InventoryFixture _fixture = new();

    private StockMovementService Movements => _fixture.Movements;

    /// <summary>Receives stock so a test has something to move.</summary>
    private async Task<StockMovementResponse> ReceiveAsync(StorageLocation destination, Product product, decimal quantity)
    {
        var receipt = await Movements.CreateAsync(_fixture.Receipt(destination, (product, quantity)), default);

        return await Movements.ConfirmAsync(receipt.Id, default);
    }

    [Fact]
    public async Task A_draft_movement_leaves_stock_untouched()
    {
        var draft = await Movements.CreateAsync(
            _fixture.Receipt(_fixture.MainLocation, (_fixture.Flour, 500m)), default);

        Assert.Equal(MovementStatus.Draft, draft.Status);
        Assert.Null(draft.ConfirmedAt);
        Assert.Equal(0m, _fixture.QuantityOf(_fixture.Flour, _fixture.MainLocation));
    }

    [Fact]
    public async Task Confirming_a_receipt_creates_the_balance_and_records_who_confirmed_it()
    {
        var confirmed = await ReceiveAsync(_fixture.MainLocation, _fixture.Flour, 500m);

        Assert.Equal(MovementStatus.Confirmed, confirmed.Status);
        Assert.Equal(_fixture.UserId, confirmed.ConfirmedBy);
        Assert.NotNull(confirmed.ConfirmedAt);
        Assert.Equal(500m, _fixture.QuantityOf(_fixture.Flour, _fixture.MainLocation));
    }

    /// <summary>
    /// The Phase 4 Definition of Done: 500 kg of flour in the main warehouse, transfer 100 kg,
    /// and the main warehouse holds 400 while production holds 100.
    /// </summary>
    [Fact]
    public async Task Confirming_a_transfer_moves_the_quantity_between_locations()
    {
        await ReceiveAsync(_fixture.MainLocation, _fixture.Flour, 500m);

        var transfer = await Movements.CreateAsync(
            _fixture.Transfer(_fixture.MainLocation, _fixture.ProductionLocation, (_fixture.Flour, 100m)), default);

        await Movements.ConfirmAsync(transfer.Id, default);

        Assert.Equal(400m, _fixture.QuantityOf(_fixture.Flour, _fixture.MainLocation));
        Assert.Equal(100m, _fixture.QuantityOf(_fixture.Flour, _fixture.ProductionLocation));
    }

    [Fact]
    public async Task Fractional_quantities_survive_a_round_trip()
    {
        await ReceiveAsync(_fixture.MainLocation, _fixture.Flour, 12.5m);

        var transfer = await Movements.CreateAsync(
            _fixture.Transfer(_fixture.MainLocation, _fixture.ProductionLocation, (_fixture.Flour, 0.75m)), default);

        await Movements.ConfirmAsync(transfer.Id, default);

        Assert.Equal(11.75m, _fixture.QuantityOf(_fixture.Flour, _fixture.MainLocation));
        Assert.Equal(0.75m, _fixture.QuantityOf(_fixture.Flour, _fixture.ProductionLocation));
    }

    [Fact]
    public async Task A_transfer_of_more_than_is_available_is_rejected_and_changes_nothing()
    {
        await ReceiveAsync(_fixture.MainLocation, _fixture.Flour, 100m);

        var transfer = await Movements.CreateAsync(
            _fixture.Transfer(_fixture.MainLocation, _fixture.ProductionLocation, (_fixture.Flour, 150m)), default);

        var exception = await Assert.ThrowsAsync<InsufficientStockException>(
            () => Movements.ConfirmAsync(transfer.Id, default));

        Assert.Equal("INSUFFICIENT_STOCK", exception.Code);
        Assert.Equal(150m, exception.Details!["requested"]);
        Assert.Equal(100m, exception.Details["available"]);
        Assert.Equal("FLOUR-001", exception.Details["sku"]);

        Assert.Equal(100m, _fixture.QuantityOf(_fixture.Flour, _fixture.MainLocation));
        Assert.Equal(0m, _fixture.QuantityOf(_fixture.Flour, _fixture.ProductionLocation));
    }

    [Fact]
    public async Task One_failing_line_rolls_the_whole_document_back()
    {
        await ReceiveAsync(_fixture.MainLocation, _fixture.Flour, 100m);
        await ReceiveAsync(_fixture.MainLocation, _fixture.Sugar, 10m);

        var transfer = await Movements.CreateAsync(
            _fixture.Transfer(
                _fixture.MainLocation,
                _fixture.ProductionLocation,
                (_fixture.Flour, 50m),
                (_fixture.Sugar, 40m)),
            default);

        await Assert.ThrowsAsync<InsufficientStockException>(() => Movements.ConfirmAsync(transfer.Id, default));

        // The flour line would have succeeded on its own; it must not have been applied.
        Assert.Equal(100m, _fixture.QuantityOf(_fixture.Flour, _fixture.MainLocation));
        Assert.Equal(0m, _fixture.QuantityOf(_fixture.Flour, _fixture.ProductionLocation));
        Assert.Equal(MovementStatus.Draft, (await Movements.GetAsync(transfer.Id, default)).Status);
    }

    [Fact]
    public async Task An_adjustment_corrects_a_count_in_either_direction()
    {
        await ReceiveAsync(_fixture.MainLocation, _fixture.Flour, 100m);

        var surplus = await Movements.CreateAsync(
            new CreateStockMovementRequest(
                MovementType.Adjustment, null, _fixture.MainLocation.Id, "Counted 3 kg more",
                [new CreateStockMovementLineRequest(_fixture.Flour.Id, 3m)]),
            default);
        await Movements.ConfirmAsync(surplus.Id, default);

        Assert.Equal(103m, _fixture.QuantityOf(_fixture.Flour, _fixture.MainLocation));

        var shortage = await Movements.CreateAsync(
            new CreateStockMovementRequest(
                MovementType.Adjustment, _fixture.MainLocation.Id, null, "Counted 8 kg less",
                [new CreateStockMovementLineRequest(_fixture.Flour.Id, 8m)]),
            default);
        await Movements.ConfirmAsync(shortage.Id, default);

        Assert.Equal(95m, _fixture.QuantityOf(_fixture.Flour, _fixture.MainLocation));
    }

    [Theory]
    [InlineData(MovementType.Receipt, true, true)]
    [InlineData(MovementType.Receipt, false, false)]
    [InlineData(MovementType.Transfer, true, false)]
    [InlineData(MovementType.Transfer, false, true)]
    [InlineData(MovementType.Adjustment, true, true)]
    [InlineData(MovementType.Adjustment, false, false)]
    public async Task A_movement_with_the_wrong_endpoints_is_rejected(
        MovementType type,
        bool hasSource,
        bool hasDestination)
    {
        var request = new CreateStockMovementRequest(
            type,
            hasSource ? _fixture.MainLocation.Id : null,
            hasDestination ? _fixture.ProductionLocation.Id : null,
            "Reason",
            [new CreateStockMovementLineRequest(_fixture.Flour.Id, 1m)]);

        var exception = await Assert.ThrowsAsync<InvalidMovementException>(
            () => Movements.CreateAsync(request, default));

        Assert.Equal("INVALID_MOVEMENT", exception.Code);
    }

    [Fact]
    public async Task A_transfer_to_the_same_location_is_rejected()
    {
        var request = _fixture.Transfer(_fixture.MainLocation, _fixture.MainLocation, (_fixture.Flour, 1m));

        await Assert.ThrowsAsync<InvalidMovementException>(() => Movements.CreateAsync(request, default));
    }

    [Fact]
    public async Task The_same_product_cannot_appear_on_two_lines()
    {
        var request = _fixture.Receipt(_fixture.MainLocation, (_fixture.Flour, 10m), (_fixture.Flour, 5m));

        var exception = await Assert.ThrowsAsync<InvalidMovementException>(
            () => Movements.CreateAsync(request, default));

        Assert.Equal(_fixture.Flour.Id, exception.Details!["productId"]);
    }

    [Fact]
    public async Task An_unknown_product_is_rejected()
    {
        var request = new CreateStockMovementRequest(
            MovementType.Receipt, null, _fixture.MainLocation.Id, null,
            [new CreateStockMovementLineRequest(Guid.NewGuid(), 1m)]);

        var exception = await Assert.ThrowsAsync<ProductNotFoundException>(
            () => Movements.CreateAsync(request, default));

        Assert.Equal("PRODUCT_NOT_FOUND", exception.Code);
    }

    [Fact]
    public async Task An_unknown_location_is_rejected()
    {
        var request = new CreateStockMovementRequest(
            MovementType.Receipt, null, Guid.NewGuid(), null,
            [new CreateStockMovementLineRequest(_fixture.Flour.Id, 1m)]);

        var exception = await Assert.ThrowsAsync<LocationNotFoundException>(
            () => Movements.CreateAsync(request, default));

        Assert.Equal("LOCATION_NOT_FOUND", exception.Code);
    }

    [Fact]
    public async Task A_deactivated_location_takes_no_stock()
    {
        var request = _fixture.Receipt(_fixture.ClosedLocation, (_fixture.Flour, 1m));

        var exception = await Assert.ThrowsAsync<LocationInactiveException>(
            () => Movements.CreateAsync(request, default));

        Assert.Equal("LOCATION_INACTIVE", exception.Code);
    }

    [Fact]
    public async Task A_location_deactivated_after_the_draft_blocks_the_confirmation()
    {
        var draft = await Movements.CreateAsync(
            _fixture.Receipt(_fixture.MainLocation, (_fixture.Flour, 10m)), default);

        _fixture.MainLocation.IsActive = false;
        await _fixture.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<LocationInactiveException>(() => Movements.ConfirmAsync(draft.Id, default));

        Assert.Equal(0m, _fixture.QuantityOf(_fixture.Flour, _fixture.MainLocation));
    }

    [Fact]
    public async Task A_confirmed_movement_can_be_neither_confirmed_again_nor_cancelled()
    {
        var confirmed = await ReceiveAsync(_fixture.MainLocation, _fixture.Flour, 500m);

        Assert.Equal("MOVEMENT_ALREADY_CONFIRMED",
            (await Assert.ThrowsAsync<MovementAlreadyConfirmedException>(
                () => Movements.ConfirmAsync(confirmed.Id, default))).Code);

        Assert.Equal("MOVEMENT_ALREADY_CONFIRMED",
            (await Assert.ThrowsAsync<MovementAlreadyConfirmedException>(
                () => Movements.CancelAsync(confirmed.Id, new CancelStockMovementRequest(null), default))).Code);

        // The one confirmation that did happen stands.
        Assert.Equal(500m, _fixture.QuantityOf(_fixture.Flour, _fixture.MainLocation));
    }

    [Fact]
    public async Task A_cancelled_movement_never_reaches_stock()
    {
        var draft = await Movements.CreateAsync(
            _fixture.Receipt(_fixture.MainLocation, (_fixture.Flour, 500m)), default);

        var cancelled = await Movements.CancelAsync(
            draft.Id, new CancelStockMovementRequest("Delivery never arrived"), default);

        Assert.Equal(MovementStatus.Cancelled, cancelled.Status);
        Assert.Equal(_fixture.UserId, cancelled.CancelledBy);
        Assert.Equal("Delivery never arrived", cancelled.Reason);
        Assert.Equal(0m, _fixture.QuantityOf(_fixture.Flour, _fixture.MainLocation));

        Assert.Equal("MOVEMENT_ALREADY_CANCELLED",
            (await Assert.ThrowsAsync<MovementAlreadyCancelledException>(
                () => Movements.ConfirmAsync(draft.Id, default))).Code);
    }

    [Fact]
    public async Task A_line_carries_the_unit_of_its_product()
    {
        var movement = await Movements.CreateAsync(
            new CreateStockMovementRequest(
                MovementType.Receipt, null, _fixture.MainLocation.Id, null,
                [
                    new CreateStockMovementLineRequest(_fixture.Flour.Id, 1m),
                    new CreateStockMovementLineRequest(_fixture.Cookies.Id, 2m)
                ]),
            default);

        Assert.Equal("pcs", movement.Lines.Single(l => l.Sku == "COOKIE-001").UnitOfMeasureCode);
        Assert.Equal("kg", movement.Lines.Single(l => l.Sku == "FLOUR-001").UnitOfMeasureCode);
    }

    [Fact]
    public async Task Movements_get_distinct_ascending_numbers()
    {
        var first = await Movements.CreateAsync(_fixture.Receipt(_fixture.MainLocation, (_fixture.Flour, 1m)), default);
        var second = await Movements.CreateAsync(_fixture.Receipt(_fixture.MainLocation, (_fixture.Sugar, 1m)), default);

        Assert.StartsWith("MOV-", first.Number);
        Assert.NotEqual(first.Number, second.Number);
        Assert.True(string.CompareOrdinal(first.Number, second.Number) < 0);
    }

    [Fact]
    public async Task Get_reports_an_unknown_movement()
    {
        var exception = await Assert.ThrowsAsync<MovementNotFoundException>(
            () => Movements.GetAsync(Guid.NewGuid(), default));

        Assert.Equal("MOVEMENT_NOT_FOUND", exception.Code);
    }

    [Fact]
    public async Task List_filters_by_status_type_product_and_location()
    {
        var receipt = await Movements.CreateAsync(
            _fixture.Receipt(_fixture.MainLocation, (_fixture.Flour, 100m)), default);
        await Movements.ConfirmAsync(receipt.Id, default);

        await Movements.CreateAsync(
            _fixture.Transfer(_fixture.MainLocation, _fixture.ProductionLocation, (_fixture.Sugar, 1m)), default);

        var confirmed = await Movements.ListAsync(new StockMovementQuery { Status = MovementStatus.Confirmed }, default);
        Assert.Equal(receipt.Id, Assert.Single(confirmed.Items).Id);

        var transfers = await Movements.ListAsync(
            new StockMovementQuery { MovementType = MovementType.Transfer }, default);
        Assert.Equal(MovementType.Transfer, Assert.Single(transfers.Items).MovementType);

        var byProduct = await Movements.ListAsync(new StockMovementQuery { ProductId = _fixture.Sugar.Id }, default);
        Assert.Single(byProduct.Items);

        var byLocation = await Movements.ListAsync(
            new StockMovementQuery { LocationId = _fixture.ProductionLocation.Id }, default);
        Assert.Single(byLocation.Items);

        var all = await Movements.ListAsync(new StockMovementQuery(), default);
        Assert.Equal(2, all.TotalCount);
    }
}
