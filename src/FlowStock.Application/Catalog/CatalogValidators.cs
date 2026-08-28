using FluentValidation;

namespace FlowStock.Application.Catalog;

public class CreateUnitOfMeasureRequestValidator : AbstractValidator<CreateUnitOfMeasureRequest>
{
    public CreateUnitOfMeasureRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(16);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(64);
    }
}

public class UpdateUnitOfMeasureRequestValidator : AbstractValidator<UpdateUnitOfMeasureRequest>
{
    public UpdateUnitOfMeasureRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(64);
    }
}

public class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(64)
            .Matches("^[A-Za-z0-9._-]+$")
            .WithMessage("SKU may contain only letters, digits, '.', '_' and '-'.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.ProductType).IsInEnum();
        RuleFor(x => x.UnitOfMeasureId).NotEmpty();
    }
}

public class UpdateProductRequestValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.ProductType).IsInEnum();
        RuleFor(x => x.UnitOfMeasureId).NotEmpty();
    }
}

public class ProductQueryValidator : AbstractValidator<ProductQuery>
{
    private static readonly string[] SortFields = ["sku", "name", "type", "createdAt"];

    public ProductQueryValidator()
    {
        RuleFor(x => x.Sort)
            .Must(sort => sort is null || SortFields.Contains(sort.TrimStart('-'), StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Sort must be one of: {string.Join(", ", SortFields)}, optionally prefixed with '-'.");
    }
}
