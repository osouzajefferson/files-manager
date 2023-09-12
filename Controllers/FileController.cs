using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;

namespace FileManager.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FileController : Controller
    {
        private const string accessKey = "AKIA4T5ULDU5WOCLDIAY";
        private const string secretKey = "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";
        private const string bucketName = "test-public-files-prov";

        private readonly IAmazonS3 _amazonS3Client;

        public FileController()
        {
            _amazonS3Client = new AmazonS3Client(accessKey, secretKey, Amazon.RegionEndpoint.SAEast1);
        }

        [HttpGet]
        public async Task<IActionResult> Index(string prefix = "")
        {
            var paths = new Dictionary<string, string>();
            var listObjectsRequest = new ListObjectsV2Request
            {
                BucketName = bucketName,
                Prefix = prefix
            };

            ListObjectsV2Response listObjectsResponse;
            do
            {

                listObjectsResponse = await _amazonS3Client.ListObjectsV2Async(listObjectsRequest);
                listObjectsRequest.ContinuationToken = listObjectsResponse.NextContinuationToken;

                foreach (var objectKey in listObjectsResponse.S3Objects.Select(x => x.Key))
                {
                    paths.Add(objectKey, $"https://{bucketName}.s3.{Amazon.RegionEndpoint.USWest2.SystemName}.amazonaws.com/{objectKey}");
                }

            } while (listObjectsResponse.IsTruncated);

            return Ok(paths);
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadFile(IFormFile file, string prefix)
        {
            var keyName = $"{prefix}/{file.FileName}";

            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);

            var request = new PutObjectRequest
            {
                BucketName = bucketName,
                Key = keyName,
                InputStream = memoryStream,
                ContentType = file.ContentType
            };

            var response = await _amazonS3Client.PutObjectAsync(request);

            if (response.HttpStatusCode == System.Net.HttpStatusCode.OK)
            {
                var premissionRequest = new PutACLRequest
                {
                    BucketName = bucketName,
                    Key = keyName,
                    CannedACL = S3CannedACL.PublicRead
                };

                await _amazonS3Client.PutACLAsync(premissionRequest);

                return Ok(new { Message = "Upload realizado com sucesso!", FileName = keyName });
            }
            else
            {
                return BadRequest(new { Message = "Erro ao enviar o arquivo para o S3." });
            }
        }

        [HttpDelete("delete/{objectKey}")]
        public async Task<IActionResult> DeleteFile(string objectKey)
        {
            var deleteObjectRequest = new DeleteObjectRequest
            {
                BucketName = bucketName,
                Key = objectKey
            };

            var response = await _amazonS3Client.DeleteObjectAsync(deleteObjectRequest);

            if (response.HttpStatusCode == System.Net.HttpStatusCode.NoContent)
            {
                return Ok(new { Message = "Arquivo deletado com sucesso!" });
            }
            else
            {
                return BadRequest(new { Message = "Erro ao deletar o arquivo do S3." });
            }
        }
    }
}
