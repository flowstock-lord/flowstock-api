using FlowStock.Domain.Inventory;
using FluentValidation;

namespace FlowStock.Application.Inventory;

public class CreateStockMovementLineRequestValidator : AbstractValidator<CreateStockMovementLineRequest>
{
    public CreateStockMovementLineRequestValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();

        // Positive quantities only — the direction of a movement comes from its endpoints.
        // The scale matches the numeric(18,4) column, so nothing is silently rounded on save.
        RuleFor(x => x.Quantity)
            .GreaterThan(0)
            .PrecisionScale(18, 4, ignoreTrailingZeros: true)
            .WithMessage("Quantity may have at most 4 decimal places.");
    }
}

public class CreateStockMovementRequestValidator : AbstractValidator<CreateStockMovementRequest>
{
    /// <summary>
    /// Consumption and production output belong to a production order and are created by it in
    /// Phase 6; a write-off needs its own approval rules. None of them may be posted by hand here.
    /// </summary>
    private static readonly MovementType[] CreatableTypes =
        [MovementType.Receipt, MovementType.Transfer, MovementType.Adjustment];

    public CreateStockMovementRequestValidator()
    {
        RuleFor(x => x.MovementType)
            .IsInEnum()
            .Must(CreatableTypes.Contains)
            .WithMessage(
                $"Only {string.Join(", ", CreatableTypes)} movements can be created directly.");

        RuleFor(x => x.Reason).MaximumLength(1000);

        // An adjustment rewrites a counted quantity, so it must say why.
        RuleFor(x => x.Reason)
            .NotEmpty()
            .When(x => x.MovementType == MovementType.Adjustment)
            .WithMessage("An adjustment must state a reason.");

        RuleFor(x => x.Lines).NotEmpty().WithMessage("A movement must have at least one line.");
        RuleForEach(x => x.Lines).SetValidator(new CreateStockMovementLineRequestValidator());
    }
}

public class CancelStockMovementRequestValidator : AbstractValidator<CancelStockMovementRequest>
{
    public CancelStockMovementRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(1000);
    }
}

public class StockQueryValidator : AbstractValidator<StockQuery>
{
    private static readonly string[] SortFields = ["sku", "location", "quantity", "expiry"];

    public StockQueryValidator()
    {
        RuleFor(x => x.Sort)
            .Must(sort => sort is null || SortFields.Contains(sort.TrimStart('-'), StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Sort must be one of: {string.Join(", ", SortFields)}, optionally prefixed with '-'.");
    }
}

public class StockMovementQueryValidator : AbstractValidator<StockMovementQuery>
{
    private static readonly string[] SortFields = ["number", "createdAt"];

    public StockMovementQueryValidator()
    {
        RuleFor(x => x.Sort)
            .Must(sort => sort is null || SortFields.Contains(sort.TrimStart('-'), StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Sort must be one of: {string.Join(", ", SortFields)}, optionally prefixed with '-'.");

        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From!.Value)
            .When(x => x.From is not null && x.To is not null)
            .WithMessage("'to' must not be earlier than 'from'.");
    }
}

public class CreateBatchRequestValidator : AbstractValidator<CreateBatchRequest>
{
    public CreateBatchRequestValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();

        // The lot number as it is written on the goods; normalized upper-case on save.
        RuleFor(x => x.Number).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Supplier).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class UpdateBatchRequestValidator : AbstractValidator<UpdateBatchRequest>
{
    public UpdateBatchRequestValidator()
    {
        RuleFor(x => x.Supplier).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class BatchQueryValidator : AbstractValidator<BatchQuery>
{
    private static readonly string[] SortFields = ["number", "expiryDate", "createdAt"];

    public BatchQueryValidator()
    {
        RuleFor(x => x.Sort)
            .Must(sort => sort is null || SortFields.Contains(sort.TrimStart('-'), StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Sort must be one of: {string.Join(", ", SortFields)}, optionally prefixed with '-'.");
    }
}
