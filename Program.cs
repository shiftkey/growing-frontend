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
        var connectionString = Environment.GetEnvironmentVariable("BLOB_STORAGE_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            context.Response.StatusCode = 404;
        }
        else
        {
            var lastBlobClient = new BlobClient(connectionString, containerName, latestFileName);

            var initialStream = new MemoryStream();
            var response = lastBlobClient.DownloadTo(initialStream);
            initialStream.Seek(0, SeekOrigin.Begin);

            var imageBytes = initialStream.ToArray();

            var cacheEntryOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromMinutes(5));

            cache.Set(LatestImageCacheKey, imageBytes, cacheEntryOptions);

            await context.Response.BodyWriter.WriteAsync(imageBytes);
        }
    }
});

// TODO: add some stylings to default page
app.MapGet("/", () => Results.Extensions.RazorSlice("/Slices/Index.cshtml"));
app.Run();
