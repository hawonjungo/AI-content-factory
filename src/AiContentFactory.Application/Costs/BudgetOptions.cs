namespace AiContentFactory.Application.Costs;

public class BudgetOptions
{
    public const string SectionName = "Budget";

    /// <summary>Hard ceiling on AI generation spend per calendar month, across all content projects.</summary>
    public decimal MonthlyLimitUsd { get; set; } = 50m;
}
