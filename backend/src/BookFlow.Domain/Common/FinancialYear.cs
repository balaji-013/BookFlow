namespace BookFlow.Domain.Common;

/// <summary>Indian financial year: 1 April – 31 March. Label format "2026-27".</summary>
public static class FinancialYear
{
    public static string Of(DateOnly date)
    {
        var startYear = date.Month >= 4 ? date.Year : date.Year - 1;
        return $"{startYear}-{(startYear + 1) % 100:00}";
    }
}
