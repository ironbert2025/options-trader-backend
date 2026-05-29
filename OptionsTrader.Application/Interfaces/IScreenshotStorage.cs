namespace OptionsTrader.Application.Interfaces;

public interface IScreenshotStorage
{
    Task<string> UploadAsync(Stream imageStream, string fileName);
}
