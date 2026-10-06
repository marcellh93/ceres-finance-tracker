using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ProjectCeres.Controllers.Api;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.Services.Reports;
using ProjectCeres.Tests.Common;

namespace ProjectCeres.Tests.Unit;

public class ReportsApiControllerExpenseBreakdownTests
{
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To   = new(2026, 9, 30);

    private static (ReportsApiController controller, Mock<IReportService> reports) Build()
    {
        var reports = new Mock<IReportService>();
        reports
            .Setup(r => r.GetExpenseBreakdownAsync(It.IsAny<int>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new ExpenseBreakdown("EUR", "€", []));

        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetAsync()).ReturnsAsync(new Settings { DefaultCurrencyId = 1 });

        var user = new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001"));
        var factory = new ReportGeneratorFactory(
            new NetWorthGenerator(null!, user),
            new IncomeExpenseGenerator(null!, user),
            new ExpenseBreakdownGenerator(null!, user),
            new TransactionHistoryGenerator(null!, user),
            new BudgetVsActualReportGenerator(null!, new SettingsService(null!, user), user),
            new LargestExpensesReportGenerator(null!, user),
            new MonthlyCashFlowReportGenerator(null!, user),
            new NetWorthOverTimeReportGenerator(null!, user));

        return (new ReportsApiController(reports.Object, settings.Object, factory), reports);
    }

    [Fact]
    public async Task ExpenseBreakdown_forwards_categoryId_to_the_service()
    {
        var (controller, reports) = Build();
        var categoryId = Guid.NewGuid();

        var result = await controller.ExpenseBreakdown(currencyId: 1, From, To, categoryId);

        result.Should().BeOfType<OkObjectResult>();
        reports.Verify(r => r.GetExpenseBreakdownAsync(1, From, To, categoryId), Times.Once);
    }

    [Fact]
    public async Task ExpenseBreakdown_without_categoryId_asks_the_service_for_every_category()
    {
        var (controller, reports) = Build();

        await controller.ExpenseBreakdown(currencyId: 1, From, To);

        reports.Verify(r => r.GetExpenseBreakdownAsync(1, From, To, null), Times.Once);
    }
}
