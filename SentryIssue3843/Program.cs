using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http.Extensions;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Sentry.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseSentry(c =>
{
    c.Environment = builder.Environment.EnvironmentName;
    c.UseOpenTelemetry();
});

builder.Services.AddOpenTelemetry()
    .WithMetrics(b => b
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddMeter("Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Server.Kestrel")
        .AddView("request-duration",
            new ExplicitBucketHistogramConfiguration
            {
                Boundaries = [0, 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10],
            })
    )
    .WithTracing(b => b
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("API Gateway"))
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("Yarp.ReverseProxy")
        .AddSentry()
    );

// YARP
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddHttpClient();

var app = builder.Build();
app.UseRouting();

app.Use(async (context, next) =>
{
    var httpClient = context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient();
    await httpClient.GetAsync("https://www.google.com");
    await next();
});

app.MapGet("/", () => "Hello World!");

app.Map("/C7821CD3-484C-4C5C-9D26-25E6DA314DE4/{**catchall}", async (HttpRequest request, ClaimsPrincipal user) =>
{
    StringBuilder returnString = new("Hello I am the test endpoint and this is the request I received:");
    returnString.AppendLine();
    returnString.AppendLine();
    returnString.Append(request.Method).Append(' ').AppendLine(request.GetDisplayUrl());
    request.Headers.ToList().ForEach(header => returnString.Append(header.Key).Append(": ").AppendLine(header.Value));
    returnString.AppendLine();
    returnString.AppendLine("User-Claims:");
    foreach (Claim claim in user.Claims)
    {
        returnString
            .Append(claim.Type)
            .Append(": ")
            .AppendLine(claim.Value);
    }

    var body = await new StreamReader(request.Body).ReadToEndAsync();
    returnString.AppendLine();
    returnString.AppendLine("Body:");
    returnString.AppendLine(body);

    return returnString.ToString();
});
    
app.MapReverseProxy();

app.Run();
