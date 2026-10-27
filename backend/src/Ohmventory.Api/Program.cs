using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Ohmventory.Api.Assistant;
using Ohmventory.Api.Data;
using Ohmventory.Api.Endpoints;
using Ohmventory.Api.Llm;
using Ohmventory.Api.Recognition;
using Ohmventory.Api.Search;
using Ohmventory.Api.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<OhmventoryDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Ohmventory")));

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
        .AllowAnyHeader().AllowAnyMethod()));

builder.Services.Configure<ImageStorageOptions>(builder.Configuration.GetSection(ImageStorageOptions.Section));
builder.Services.Configure<LabelOptions>(builder.Configuration.GetSection(LabelOptions.Section));
builder.Services.Configure<SearxngOptions>(builder.Configuration.GetSection(SearxngOptions.Section));
builder.Services.AddSingleton<ImageStorage>();
builder.Services.AddHttpClient<SearxngClient>((sp, http) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SearxngOptions>>().Value;
    http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    http.Timeout = TimeSpan.FromSeconds(20);
});

builder.Services.AddLlm(builder.Configuration);
builder.Services.AddScoped<InventoryQueries>();
builder.Services.AddScoped<PartRecognitionService>();
builder.Services.AddScoped<ProjectAssistantService>();

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<OhmventoryDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseCors();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapGet("/api/health", () => TypedResults.Ok(new { status = "ok" }));
app.MapPartEndpoints();
app.MapContainerEndpoints();
app.MapRecognitionEndpoints();
app.MapProjectEndpoints();
app.MapShoppingListEndpoints();
app.MapLlmEndpoints();

app.Run();

public partial class Program;
