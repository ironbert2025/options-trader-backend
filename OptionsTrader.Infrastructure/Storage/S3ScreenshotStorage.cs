using Amazon.S3;
using Amazon.S3.Model;
using OptionsTrader.Application.Interfaces;

namespace OptionsTrader.Infrastructure.Storage;

public class S3ScreenshotStorage : IScreenshotStorage
{
    private readonly string _bucketName;
    private readonly IAmazonS3 _s3Client;

    public S3ScreenshotStorage(IAmazonS3 s3Client, string bucketName)
    {
        _s3Client   = s3Client;
        _bucketName = bucketName;
    }

    public async Task<string> UploadAsync(Stream imageStream, string fileName)
    {
        var key = $"screenshots/{fileName}";

        var request = new PutObjectRequest
        {
            BucketName  = _bucketName,
            Key         = key,
            InputStream = imageStream,
            ContentType = "image/png"
        };

        await _s3Client.PutObjectAsync(request);

        return $"https://{_bucketName}.s3.amazonaws.com/{key}";
    }
}
