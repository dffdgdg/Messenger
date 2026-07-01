namespace API.Web.Configuration;

public static class StaticFilesConfiguration
{
    public static WebApplication UseMessengerStaticFiles(this WebApplication app)
    {
        var webRootPath = app.Environment.WebRootPath ?? "wwwroot";
        var uploadsPath = Path.Combine(webRootPath, "uploads");

        Directory.CreateDirectory(uploadsPath);

        return app;
    }
}