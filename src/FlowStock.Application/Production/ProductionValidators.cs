using FluentValidation;

namespace FlowStock.Application.Production;

public class CreateBillOfMaterialItemRequestValidator : AbstractValidator<CreateBillOfMaterialItemRequest>
{
    public CreateBillOfMaterialItemRequestValidator()
    {
        RuleFor(x => x.ComponentProductId).NotEmpty();

        // The scale matches the numeric(18,4) column, so nothing is silently rounded on save.
        RuleFor(x => x.Quantity)
            .GreaterThan(0)
            .PrecisionScale(18, 4, ignoreTrailingZeros: true)
            .WithMessage("Quantity may have at most 4 decimal places.");
    }
}

public class CreateBillOfMaterialRequestValidator : AbstractValidator<CreateBillOfMaterialRequest>
{
    public CreateBillOfMaterialRequestValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();

        // The recipe's own scale: "Cookie / 100 pcs". Component quantities are read against it.
        RuleFor(x => x.OutputQuantity)
            .GreaterThan(0)
            .PrecisionScale(18, 4, ignoreTrailingZeros: true)
            .WithMessage("Output quantity may have at most 4 decimal places.");

        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);

        RuleFor(x => x.Items).NotEmpty().WithMessage("A bill of materials must have at least one component.");
        RuleForEach(x => x.Items).SetValidator(new CreateBillOfMaterialItemRequestValidator());
    }
}

public class UpdateBillOfMaterialRequestValidator : AbstractValidator<UpdateBillOfMaterialRequest>
{
    public UpdateBillOfMaterialRequestValidator()
    {
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

public class MaterialRequirementsQueryValidator : AbstractValidator<MaterialRequirementsQuery>
{
    public MaterialRequirementsQueryValidator()
    {
        RuleFor(x => x.Quantity)
            .GreaterThan(0)
            .PrecisionScale(18, 4, ignoreTrailingZeros: true)
            .WithMessage("Quantity may have at most 4 decimal places.");
    }
}

public class BillOfMaterialQueryValidator : AbstractValidator<BillOfMaterialQuery>
{
    private static readonly string[] SortFields = ["sku", "version", "createdAt"];

    public BillOfMaterialQueryValidator()
    {
        RuleFor(x => x.Sort)
            .Must(sort => sort is null || SortFields.Contains(sort.TrimStart('-'), StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Sort must be one of: {string.Join(", ", SortFields)}, optionally prefixed with '-'.");
    }
}

public class CreateProductionOrderRequestValidator : AbstractValidator<CreateProductionOrderRequest>
{
    public CreateProductionOrderRequestValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.ProductionLocationId).NotEmpty();
        RuleFor(x => x.OutputLocationId).NotEmpty();

        // The scale matches the numeric(18,4) column, so nothing is silently rounded on save.
        RuleFor(x => x.PlannedQuantity)
            .GreaterThan(0)
            .PrecisionScale(18, 4, ignoreTrailingZeros: true)
            .WithMessage("Planned quantity may have at most 4 decimal places.");

        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class CompleteProductionOrderRequestValidator : AbstractValidator<CompleteProductionOrderRequest>
{
    public CompleteProductionOrderRequestValidator()
    {
        // Omitted means "the planned quantity"; given, it is what the run actually yielded.
        RuleFor(x => x.ProducedQuantity)
            .GreaterThan(0)
            .PrecisionScale(18, 4, ignoreTrailingZeros: true)
            .WithMessage("Produced quantity may have at most 4 decimal places.")
            .When(x => x.ProducedQuantity is not null);

        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class CancelProductionOrderRequestValidator : AbstractValidator<CancelProductionOrderRequest>
{
    public CancelProductionOrderRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(1000);
    }
}

public class ProductionOrderQueryValidator : AbstractValidator<ProductionOrderQuery>
{
    private static readonly string[] SortFields = ["number", "createdAt", "plannedStartAt"];

    public ProductionOrderQueryValidator()
    {
        RuleFor(x => x.Sort)
            .Must(sort => sort is null || SortFields.Contains(sort.TrimStart('-'), StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Sort must be one of: {string.Join(", ", SortFields)}, optionally prefixed with '-'.");
    }
}
