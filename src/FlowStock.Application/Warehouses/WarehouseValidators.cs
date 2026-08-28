using FluentValidation;

namespace FlowStock.Application.Warehouses;

public class CreateWarehouseRequestValidator : AbstractValidator<CreateWarehouseRequest>
{
    public CreateWarehouseRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32)
            .Matches("^[A-Za-z0-9._-]+$")
            .WithMessage("Code may contain only letters, digits, '.', '_' and '-'.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.WarehouseType).IsInEnum();
    }
}

public class UpdateWarehouseRequestValidator : AbstractValidator<UpdateWarehouseRequest>
{
    public UpdateWarehouseRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.WarehouseType).IsInEnum();
    }
}

public class CreateStorageLocationRequestValidator : AbstractValidator<CreateStorageLocationRequest>
{
    public CreateStorageLocationRequestValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32)
            .Matches("^[A-Za-z0-9._-]+$")
            .WithMessage("Code may contain only letters, digits, '.', '_' and '-'.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

public class UpdateStorageLocationRequestValidator : AbstractValidator<UpdateStorageLocationRequest>
{
    public UpdateStorageLocationRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

public class WarehouseQueryValidator : AbstractValidator<WarehouseQuery>
{
    private static readonly string[] SortFields = ["code", "name", "type", "createdAt"];

    public WarehouseQueryValidator()
    {
        RuleFor(x => x.Sort)
            .Must(sort => sort is null || SortFields.Contains(sort.TrimStart('-'), StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Sort must be one of: {string.Join(", ", SortFields)}, optionally prefixed with '-'.");
    }
}

public class StorageLocationQueryValidator : AbstractValidator<StorageLocationQuery>
{
    private static readonly string[] SortFields = ["code", "name", "createdAt"];

    public StorageLocationQueryValidator()
    {
        RuleFor(x => x.Sort)
            .Must(sort => sort is null || SortFields.Contains(sort.TrimStart('-'), StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Sort must be one of: {string.Join(", ", SortFields)}, optionally prefixed with '-'.");
    }
}
