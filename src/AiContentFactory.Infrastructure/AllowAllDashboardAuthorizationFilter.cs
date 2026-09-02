using Hangfire.Dashboard;

namespace AiContentFactory.Api.Infrastructure;

public class AllowAllDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}