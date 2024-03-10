using Azure.Storage.Blobs;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

const string LatestImageCacheKey = "LatestImage";
const string containerName = "images";
const string latestFileName = "latest.jpg";

MemoryCache cache = new(new MemoryCacheOptions());

app.UseStaticFiles();
app.UseHttpsRedirection();

var bearerToken = Environment.GetEnvironmentVariable("CALLBACK_BEARER_TOKEN");
var connectionString = Environment.GetEnvironmentVariable("BLOB_STORAGE_CONNECTION_STRING");

byte[] FetchLatestImageFromCache(string connectionString)
{
    var lastBlobClient = new BlobClient(connectionString, containerName, latestFileName);

    var initialStream = new MemoryStream();
    var response = lastBlobClient.DownloadTo(initialStream);
    initialStream.Seek(0, SeekOrigin.Begin);

    var imageBytes = initialStream.ToArray();

    var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(60));

    cache.Set(LatestImageCacheKey, imageBytes, cacheEntryOptions);

    return imageBytes;
}

app.MapPost("/callback", context =>
{
    if (string.IsNullOrWhiteSpace(bearerToken) || string.IsNullOrWhiteSpace(connectionString))
    {
        // the app isn't setup right, ignore this request
        context.Response.StatusCode = 403;
        return Task.CompletedTask;
    }

    var header = context.Request.Headers.Authorization;
    var firstValue = header.FirstOrDefault();
    if (firstValue == null)
    {
        // no authorization header found, reject this and ignore
        context.Response.StatusCode = 403;
        return Task.CompletedTask;
    }

    var values = firstValue.Split(" ");
    if (values.Length == 2
        && values[0].Equals("Bearer", StringComparison.Ordinal)
        && values[1].Equals(bearerToken, StringComparison.Ordinal))
    {
        // we trust the source of this request, let's update the cached value
        FetchLatestImageFromCache(connectionString);
        return Task.CompletedTask;
    }

    // you don't have to go home but you can't stay here
    context.Response.StatusCode = 403;
    return Task.CompletedTask;
});

// TODO: set cache headers
app.MapGet("/latest/image", async context =>
{
    bool exists = cache.TryGetValue(LatestImageCacheKey, out byte[]? cachedValue);
    if (exists)
    {
        await context.Response.BodyWriter.WriteAsync(cachedValue);
    }
    else
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            context.Response.StatusCode = 403;
            return;
        }

        var imageBytes = FetchLatestImageFromCache(connectionString);

        context.Response.Headers["Content-Type"] = "image/jpeg";
        context.Response.Headers["Cache-Control"] = "max=age: 600";
        await context.Response.BodyWriter.WriteAsync(imageBytes);
    }
});

// TODO: add some stylings to default page
app.MapGet("/", () => Results.Extensions.RazorSlice("/Slices/Index.cshtml"));
app.Run();
