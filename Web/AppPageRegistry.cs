using Orders;

namespace Web;

public enum AppPageAccess
{
    AnyAuthenticated,
    LabOnly
}

public sealed record AppPageDefinition(
    string Path,
    string ResourceName,
    AppPageAccess Access,
    string FallbackPath);

public static class AppPageRegistry
{
    public const string LoginPath = "/login";
    public const string DefaultLabPath = "/";
    public const string DefaultClinicPath = "/orders";

    private static readonly AppPageDefinition[] RegisteredPages =
    [
        new(DefaultLabPath, "index.html", AppPageAccess.LabOnly, DefaultClinicPath),
        new(DefaultClinicPath, "orders.html", AppPageAccess.AnyAuthenticated, DefaultClinicPath),
        new("/iam", "iam.html", AppPageAccess.LabOnly, DefaultClinicPath),
        new("/scheduling-config", "scheduling-config.html", AppPageAccess.LabOnly, DefaultClinicPath),
    ];

    private static readonly Dictionary<string, AppPageDefinition> PagesByPath = RegisteredPages
        .ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);

    static AppPageRegistry()
    {
        foreach (var page in RegisteredPages)
        {
            if (!PagesByPath.ContainsKey(page.FallbackPath))
                throw new InvalidOperationException($"App page '{page.Path}' has unregistered fallback '{page.FallbackPath}'.");
        }
    }

    public static IReadOnlyCollection<AppPageDefinition> Pages => RegisteredPages;

    public static bool TryGetPage(string? path, out AppPageDefinition page) =>
        PagesByPath.TryGetValue(NormalizePath(path), out page!);

    public static bool CanAccess(AppPageDefinition page, AuthenticatedActor actor) =>
        page.Access switch
        {
            AppPageAccess.AnyAuthenticated => true,
            AppPageAccess.LabOnly => actor.IsLab,
            _ => false
        };

    public static string DefaultPathFor(AuthenticatedActor? actor) =>
        actor?.IsLab == true ? DefaultLabPath : DefaultClinicPath;

    public static string ResolveReturnPath(string? returnUrl, AuthenticatedActor? actor)
    {
        var target = NormalizeLocalPathAndQuery(returnUrl);
        if (target is null || !TryGetPage(target.Value.Path, out var page))
            return DefaultPathFor(actor);

        if (actor is not null && CanAccess(page, actor))
            return target.Value.PathAndQuery;

        if (actor is not null && TryGetPage(page.FallbackPath, out var fallback) && CanAccess(fallback, actor))
            return fallback.Path;

        return DefaultPathFor(actor);
    }

    public static async Task<IResult?> AuthorizeDocumentRequestAsync(HttpContext ctx, SchedulingAuthService auth, AppPageDefinition page)
    {
        var actor = await SchedulingEndpointAuth.AuthenticateAsync(ctx, auth);
        if (actor is null)
        {
            var returnUrl = BuildRequestPathAndQuery(ctx);
            return Results.Redirect($"{LoginPath}?returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        if (!CanAccess(page, actor))
            return Results.Redirect(page.FallbackPath);

        return null;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";
        if (!path.StartsWith('/'))
            path = "/" + path;
        return path.Length > 1 ? path.TrimEnd('/') : path;
    }

    private static string BuildRequestPathAndQuery(HttpContext ctx)
    {
        var path = NormalizePath(ctx.Request.Path.Value);
        return path + ctx.Request.QueryString.Value;
    }

    private static (string Path, string PathAndQuery)? NormalizeLocalPathAndQuery(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (!raw.StartsWith("/", StringComparison.Ordinal) || raw.StartsWith("//", StringComparison.Ordinal))
            return null;
        var withoutHash = raw.Split('#', 2)[0];
        var queryIndex = withoutHash.IndexOf('?', StringComparison.Ordinal);
        var rawPath = queryIndex >= 0 ? withoutHash[..queryIndex] : withoutHash;
        var query = queryIndex >= 0 ? withoutHash[(queryIndex + 1)..] : string.Empty;
        var path = NormalizePath(rawPath);
        return (path, string.IsNullOrEmpty(query) ? path : $"{path}?{query}");
    }
}
