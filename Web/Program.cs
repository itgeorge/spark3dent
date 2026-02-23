var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => Results.Ok("Spark3Dent Web - Phase 0"));

app.Run();
