using System.Net;
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

// TODO: in this handler we need
//        - check the cached value
//        - if found
//                - return data with cache headers
//        - otherwise we need to fetch latest version
//                -  build up connection
//                - query for binary data
//                - store image in cache
//                - return data with cache headers
//
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

            cache.Set(LatestImageCacheKey, imageBytes);

            await context.Response.BodyWriter.WriteAsync(imageBytes);
        }
    }
});

// TODO: add some stylings
app.MapGet("/", () => Results.Extensions.RazorSlice("/Slices/Index.cshtml"));
app.Run();
