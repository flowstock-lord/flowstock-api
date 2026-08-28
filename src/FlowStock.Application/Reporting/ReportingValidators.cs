using FluentValidation;

namespace FlowStock.Application.Reporting;

/// <summary>
/// One rule for every report: the sort field must be one this report can actually order by, so a
/// typo is a 400 rather than a silently different report.
/// </summary>
public abstract class SortedReportQueryValidator<T> : AbstractValidator<T>
{
    protected SortedReportQueryValidator(string[] sortFields, Func<T, string?> sort)
    {
        RuleFor(query => sort(query))
            .Must(value => value is null
                           || sortFields.Contains(value.TrimStart('-'), StringComparer.OrdinalIgnoreCase))
            .WithName("sort")
            .WithMessage($"Sort must be one of: {string.Join(", ", sortFields)}, optionally prefixed with '-'.");
    }
}

public class CurrentStockQueryValidator : SortedReportQueryValidator<CurrentStockQuery>
{
    public CurrentStockQueryValidator() : base(["sku", "quantity"], query => query.Sort)
    {
    }
}

public class WarehouseStockQueryValidator : SortedReportQueryValidator<WarehouseStockQuery>
{
    public WarehouseStockQueryValidator() : base(["warehouse", "sku", "quantity"], query => query.Sort)
    {
    }
}

public class MovementHistoryQueryValidator : SortedReportQueryValidator<MovementHistoryQuery>
{
    public MovementHistoryQueryValidator() : base(["occurredAt"], query => query.Sort)
    {
    }
}

public class ProductionHistoryQueryValidator : SortedReportQueryValidator<ProductionHistoryQuery>
{
    public ProductionHistoryQueryValidator()
        : base(["number", "createdAt", "completedAt"], query => query.Sort)
    {
    }
}

public class ProductionTotalsQueryValidator : SortedReportQueryValidator<ProductionTotalsQuery>
{
    public ProductionTotalsQueryValidator() : base(["sku", "quantity"], query => query.Sort)
    {
    }
}

public class AdjustmentReportQueryValidator : SortedReportQueryValidator<AdjustmentReportQuery>
{
    public AdjustmentReportQueryValidator() : base(["occurredAt"], query => query.Sort)
    {
    }
}
