using Azure.Storage.Blobs;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

var app = builder.Build();

const string LatestImageCacheKey = "LatestImage";
const string containerName = "images";
const string latestFileName = "latest.jpg";

MemoryCache cache = new(new MemoryCacheOptions());

app.UseStaticFiles();
app.UseHttpsRedirection();

if (app.Environment.IsDevelopment())
{
    // source: https://www.meziantou.net/list-all-routes-in-an-asp-net-core-application.htm
    app.MapGet("/debug/routes", (IEnumerable<EndpointDataSource> endpointSources) =>
        string.Join("\n", endpointSources.SelectMany(source => source.Endpoints)));
}

var bearerToken = Environment.GetEnvironmentVariable("CALLBACK_BEARER_TOKEN");
var connectionString = Environment.GetEnvironmentVariable("BLOB_STORAGE_CONNECTION_STRING");

async ValueTask<byte[]> FetchLatestImageFromCache(string connectionString)
{
    var lastBlobClient = new BlobClient(connectionString, containerName, latestFileName);

    var initialStream = new MemoryStream();
    await lastBlobClient.DownloadToAsync(initialStream);
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
        //
        // muting this warning as we don't need to await this method completing
#pragma warning disable CS4014 
        FetchLatestImageFromCache(connectionString);
#pragma warning restore CS4014
        return Task.CompletedTask;
    }

    // you don't have to go home but you can't stay here
    context.Response.StatusCode = 403;
    return Task.CompletedTask;
});

async Task WriteImageToResponse(HttpResponse response, byte[] imageBytes)
{
    response.Headers.ContentType = "image/jpeg";
    response.Headers.CacheControl = "max-age=600";
    await response.BodyWriter.WriteAsync(imageBytes);
}

app.MapGet("/latest/image", async context =>
{
    bool exists = cache.TryGetValue(LatestImageCacheKey, out byte[]? cachedValue);
    if (exists && cachedValue != null)
    {
        await WriteImageToResponse(context.Response, cachedValue);
    }
    else
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            context.Response.StatusCode = 403;
            return;
        }

        var imageBytes = await FetchLatestImageFromCache(connectionString);
        await WriteImageToResponse(context.Response, imageBytes);
    }
});

app.MapRazorPages();

app.Run();
