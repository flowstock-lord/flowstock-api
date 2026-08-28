using FluentValidation;

namespace FlowStock.Application.Traceability;

public class ProductHistoryQueryValidator : AbstractValidator<ProductHistoryQuery>
{
    private static readonly string[] SortFields = ["occurredAt"];

    public ProductHistoryQueryValidator()
    {
        RuleFor(x => x.Sort)
            .Must(sort => sort is null || SortFields.Contains(sort.TrimStart('-'), StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Sort must be one of: {string.Join(", ", SortFields)}, optionally prefixed with '-'.");
    }
}

public class MaterialUsageQueryValidator : AbstractValidator<MaterialUsageQuery>
{
    private static readonly string[] SortFields = ["startedAt"];

    public MaterialUsageQueryValidator()
    {
        RuleFor(x => x.Sort)
            .Must(sort => sort is null || SortFields.Contains(sort.TrimStart('-'), StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Sort must be one of: {string.Join(", ", SortFields)}, optionally prefixed with '-'.");
    }
}
