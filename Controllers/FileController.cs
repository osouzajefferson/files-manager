using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Util.Internal;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.IO.Compression;

namespace FileManager.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FileController : Controller
    {
        private readonly IAmazonS3 _amazonS3Client;

        public FileController()
        {
            _amazonS3Client = new AmazonS3Client(AppConstants.AccessKey, AppConstants.SecretKey, Amazon.RegionEndpoint.SAEast1);
        }

        [HttpGet]
        public async Task<IActionResult> Index(string path = "")
        {
            var nodes = await GetAllFiles(path);
            return Ok(nodes);
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search(string rootPath, string query)
        {
            var rootNode = await GetAllFiles(rootPath);

            List<FilesTreeNode> results = new();
            FileTreeBuilder.RecursiveSearch(rootNode, query, results);

            FilesTreeNode node = new() { Children = results, IsDirectory = true };

            return Ok(node);
        }

        [HttpPost]
        public async Task<IActionResult> CreatePath(string path)
        {
            if (!path.EndsWith("/"))
                path += "/";

            var putObjectRequest = new PutObjectRequest
            {
                BucketName = AppConstants.BucketName,
                Key = path,
                ContentBody = "",
            };

            var response = await _amazonS3Client.PutObjectAsync(putObjectRequest);

            if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
                return BadRequest(new { Message = "Erro ao criar o diretório no S3." });

            return Ok(new { Message = "Diretório criado com sucesso!" });
        }

        [HttpPost("upload/single")]
        public async Task<IActionResult> UploadFile(IFormFile file, string path)
        {
            await UploadSingleFile(file, path);
            return Ok();
        }

        [HttpPost("upload/multiple")]
        public async Task<IActionResult> UploadFiles(IFormFile[] file, string path)
        {
            foreach (var f in file)
                await UploadSingleFile(f, path);

            return Ok();
        }

        [HttpGet("download")]
        public async Task<IActionResult> DownloadObjects([FromQuery] List<string> paths)
        {
            if (paths == null || paths.Count == 0)
                return BadRequest("At least one key is required.");

            if (paths.Count == 1)
            {
                using var response = await _amazonS3Client.GetObjectAsync(AppConstants.BucketName, paths.First());
                if (response.ResponseStream != null)
                {
                    var memory = new MemoryStream();
                    response.ResponseStream.CopyTo(memory);

                    return File(memory.ToArray(), "application/octet-stream", Path.GetFileName(paths.First()));
                }

                return NotFound();
            }

            var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                foreach (var key in paths)
                {
                    try
                    {
                        using var response = await _amazonS3Client.GetObjectAsync(AppConstants.BucketName, key);
                        var entry = archive.CreateEntry(Path.GetFileName(key));

                        using var entryStream = entry.Open();
                        response.ResponseStream.CopyTo(entryStream);
                    }
                    catch (AmazonS3Exception ex)
                    {
                        return StatusCode(500, $"Error while fetching {key} from S3: {ex.Message}");
                    }
                }
            }

            memoryStream.Position = 0;
            return File(memoryStream, "application/zip", "files.zip");
        }

        [HttpGet("download/folder")]
        public async Task<IActionResult> DownloadFolderObjects(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest("The 'path' parameter is required.");

            if (!path.EndsWith("/"))
                path += "/";

            var request = new ListObjectsV2Request { BucketName = AppConstants.BucketName, Prefix = path };
            var response = await _amazonS3Client.ListObjectsV2Async(request);

            if (!response.S3Objects.Any())
                return NotFound($"No files found in the specified path: '{path}'.");

            var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                foreach (var s3Object in response.S3Objects)
                {
                    try
                    {
                        var objectResponse = await _amazonS3Client.GetObjectAsync(AppConstants.BucketName, s3Object.Key);
                        var entryName = s3Object.Key.Replace(path, "");

                        if (string.IsNullOrEmpty(entryName))
                            continue;

                        var entry = archive.CreateEntry(entryName);

                        using var entryStream = entry.Open();
                        using var stream = objectResponse.ResponseStream;
                        await stream.CopyToAsync(entryStream);
                    }
                    catch (AmazonS3Exception ex)
                    {
                        return StatusCode(500, $"Error while fetching {s3Object.Key} from S3: {ex.Message}");
                    }
                }
            }

            memoryStream.Position = 0;

            return File(memoryStream, "application/zip", $"{Path.GetFileName(path.TrimEnd('/'))}.zip");
        }

        [HttpDelete("delete")]
        public async Task<IActionResult> DeleteFile(string key)
        {
            if (!key.EndsWith("/"))
                key += "/";

            var deleteObjectRequest = new DeleteObjectRequest
            {
                BucketName = AppConstants.BucketName,
                Key = key
            };

            var response = await _amazonS3Client.DeleteObjectAsync(deleteObjectRequest);

            if (response.HttpStatusCode != System.Net.HttpStatusCode.NoContent)
                return BadRequest(new { Message = "Erro ao deletar o arquivo do S3." });

            return Ok(new { Message = "Arquivo deletado com sucesso!" });
        }

        private async Task<FilesTreeNode> GetAllFiles(string path = "")
        {
            var rootNode = new FilesTreeNode
            {
                Name = "",
                IsDirectory = true,
                Path = "/",
                BreadCrumbs = path
            };

            var listObjectsRequest = new ListObjectsV2Request
            {
                BucketName = AppConstants.BucketName,
                Prefix = path
            };

            ListObjectsV2Response response;

            do
            {
                response = await _amazonS3Client.ListObjectsV2Async(listObjectsRequest);
                foreach (var s3Object in response.S3Objects)
                {
                    FileTreeBuilder.InsertObjectIntoTree(rootNode, s3Object, path);
                }

                listObjectsRequest.ContinuationToken = response.NextContinuationToken;

            } while (response.IsTruncated);

            return rootNode;
        }

        private async Task UploadSingleFile(IFormFile file, string path)
        {
            var keyName = $"{path}/{file.FileName}";

            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);

            var request = new PutObjectRequest
            {
                BucketName = AppConstants.BucketName,
                Key = keyName,
                InputStream = memoryStream,
                ContentType = file.ContentType
            };

            var response = await _amazonS3Client.PutObjectAsync(request);

            if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
                throw new Exception(response.ToString());

            var premissionRequest = new PutACLRequest
            {
                BucketName = AppConstants.BucketName,
                Key = keyName,
                CannedACL = S3CannedACL.PublicRead
            };

            await _amazonS3Client.PutACLAsync(premissionRequest);
        }
    }
}