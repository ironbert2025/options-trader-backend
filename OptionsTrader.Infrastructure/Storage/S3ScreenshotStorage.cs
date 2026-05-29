using OptionsTrader.Application.Interfaces;

namespace OptionsTrader.Infrastructure.Storage;

public class S3ScreenshotStorage : IScreenshotStorage
{
    public Task<string> UploadAsync(Stream imageStream, string fileName)
    {
        // TODO: implement AWS S3 upload using AWSSDK.S3
        throw new NotImplementedException();
    }
}
