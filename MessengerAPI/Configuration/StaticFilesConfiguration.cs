using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace MessengerAPI.Configuration;

public static class StaticFilesConfiguration
{
    public static WebApplication UseMessengerStaticFiles(this WebApplication app)
    {
        var webRootPath = app.Environment.WebRootPath ?? "wwwroot";
        var uploadsPath = Path.Combine(webRootPath, "uploads");
        var avatarsPath = Path.Combine(webRootPath, "avatars");

        Directory.CreateDirectory(uploadsPath);
        Directory.CreateDirectory(avatarsPath);

        var contentTypeProvider = CreateContentTypeProvider();

        app.UseStaticFiles();

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(uploadsPath),
            RequestPath = "/uploads",
            ContentTypeProvider = contentTypeProvider,
            ServeUnknownFileTypes = true,
            DefaultContentType = "application/octet-stream"
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(avatarsPath),
            RequestPath = "/avatars",
            ContentTypeProvider = contentTypeProvider,
            OnPrepareResponse = ctx =>
            {
                ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store");
                ctx.Context.Response.Headers.Append("Pragma", "no-cache");
                ctx.Context.Response.Headers.Append("Expires", "-1");
            }
        });

        return app;
    }

    private static FileExtensionContentTypeProvider CreateContentTypeProvider()
    {
        var provider = new FileExtensionContentTypeProvider();

        var additionalTypes = new Dictionary<string, string>
        {
            [".ipynb"] = "application/x-ipynb+json",
            [".json"] = "application/json",
            [".md"] = "text/markdown",
            [".yaml"] = "application/x-yaml",
            [".yml"] = "application/x-yaml",
            [".csv"] = "text/csv",
            [".py"] = "text/x-python",
            [".cs"] = "text/plain",
            [".ts"] = "text/typescript",
            [".tsx"] = "text/typescript",
            [".jsx"] = "text/javascript",
            [".webp"] = "image/webp"
        };

        foreach (var (ext, mimeType) in additionalTypes)
        {
            provider.Mappings[ext] = mimeType;
        }

        return provider;
    }
}