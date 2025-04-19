namespace MyNet8App.Core;

public static class FinancialYearHelper
{
    public static (DateOnly Start, DateOnly End) GetDateRange(
        DateOnly fyStart,
        DateOnly todayUtc)
    {
        var fyEnd = fyStart.AddYears(1).AddDays(-1);

        if (todayUtc <= fyEnd)
            return (fyStart, todayUtc.AddDays(-1));   // still inside FY

        return (fyStart, fyEnd);                      // after year‑end – lock range
    }
}
