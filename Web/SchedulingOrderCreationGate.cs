namespace Web;

/// <summary>
/// Temporary Deployment 1 switch.
/// Deployment 2 cleanup:
/// - set <see cref="IsTemporarilyDisabled"/> to false (or remove this file)
/// - set ORDER_CREATION_TEMPORARILY_DISABLED to false in Web/wwwroot/js/orders-root-view.js
/// - remove temporary test ignores: rg "Deployment 1: order creation temporarily disabled"
/// </summary>
internal static class SchedulingOrderCreationGate
{
    internal const bool IsTemporarilyDisabled = true;
    internal const int StatusCode = StatusCodes.Status503ServiceUnavailable;
    internal const string ErrorMessage = "Order creation is temporarily disabled for maintenance.";
}
