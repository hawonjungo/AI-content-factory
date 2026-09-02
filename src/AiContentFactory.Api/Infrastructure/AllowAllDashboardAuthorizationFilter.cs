using Hangfire.Dashboard;

namespace AiContentFactory.Api.Infrastructure;

/// <summary>
/// Hangfire's default dashboard authorization only allows requests it
/// detects as "local" - a check that frequently fails inside Docker (the
/// browser's connection looks like it's coming through the container's
/// network bridge, not truly local), giving a confusing 401 even from
/// localhost. This opens the dashboard to anyone who can reach the API.
/// Fine for local dev; for a real deployment, replace with a filter that
/// checks authentication/role instead of allowing everyone.
/// </summary>
public class AllowAllDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}
