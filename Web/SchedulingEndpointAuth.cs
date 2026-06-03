using Orders;

namespace Web;

public static class SchedulingEndpointAuth
{
    public const string AuthCookieName = "s3d_order_session";
    public const string ActorItemKey = "SchedulingActor";

    public static async Task<AuthenticatedActor?> AuthenticateAsync(HttpContext ctx, SchedulingAuthService auth)
    {
        ctx.Request.Cookies.TryGetValue(AuthCookieName, out var token);
        var actor = await auth.AuthenticateAsync(token, ctx.RequestAborted);
        if (actor != null)
            ctx.Items[ActorItemKey] = actor;
        return actor;
    }

    public static async ValueTask<object?> RequireTechnicianActorAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var auth = context.HttpContext.RequestServices.GetRequiredService<SchedulingAuthService>();
        var actor = await AuthenticateAsync(context.HttpContext, auth);
        if (actor == null)
            return Results.Json(new { error = "Not authenticated." }, statusCode: StatusCodes.Status401Unauthorized);
        if (!actor.IsTechnician)
            return Results.Json(new { error = "Technician access required." }, statusCode: StatusCodes.Status403Forbidden);
        return await next(context);
    }

    public static AuthenticatedActor? CurrentActor(HttpContext ctx) =>
        ctx.Items.TryGetValue(ActorItemKey, out var actor) ? actor as AuthenticatedActor : null;
}
