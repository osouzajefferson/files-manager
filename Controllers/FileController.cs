using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;
using System.IO;

namespace FileManager.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FileController : Controller
    {
        private const string accessKey = "AKIA4T5ULDU5WOCLDIAY";
        private const string secretKey = "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";
        private const string fileNameAttribute = "x-amz-meta-custom-filename";
        private const string bucketName = "test-public-files-prov";

        private readonly IAmazonS3 _amazonS3Client;

        public FileController()
        {
            _amazonS3Client = new AmazonS3Client(accessKey, secretKey, Amazon.RegionEndpoint.SAEast1);
        }

        [HttpGet]
        public async Task<IActionResult> Index(string directory = "")
        {
            var rootNode = new FilesTreeNode { Name = directory, IsDirectory = true };
            await BuildTree(directory, rootNode);
            return Ok(rootNode);
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return BadRequest("Query parameter is required.");
            }

            var rootNode = new FilesTreeNode { Name = "", IsDirectory = true };
            await BuildTree("", rootNode);

            var matchingItems = FilterTreeNodes(rootNode, query).ToList();

            var ret = TreeNodeHelper.GetAllNodesAndChildren(matchingItems);
            foreach (var item in ret)
                item.Children = new();

            return Ok(ret);
        }

        private IEnumerable<FilesTreeNode> FilterTreeNodes(FilesTreeNode node, string query)
        {
            var matchingNodes = new List<FilesTreeNode>();

            if (node.Name.Contains(query))
            {
                matchingNodes.Add(node);
            }

            foreach (var child in node.Children)
            {
                matchingNodes.AddRange(FilterTreeNodes(child, query));
            }

            return matchingNodes;
        }

        [HttpPost]
        public async Task<IActionResult> CreatePath(string path)
        {
            if (!path.EndsWith("/"))
                path += "/";

            var putObjectRequest = new PutObjectRequest
            {
                BucketName = bucketName,
                Key = path,
                ContentBody = "",
            };

            var response = await _amazonS3Client.PutObjectAsync(putObjectRequest);

            if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
                return BadRequest(new { Message = "Erro ao criar o diretório no S3." });

            return Ok(new { Message = "Diretório criado com sucesso!" });
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadFile(IFormFile file, string prefix, string fileName)
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

            //request.Metadata.Add(fileNameAttribute, fileName);
            var response = await _amazonS3Client.PutObjectAsync(request);

            if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
                return BadRequest(new { Message = "Erro ao enviar o arquivo para o S3." });


            var premissionRequest = new PutACLRequest
            {
                BucketName = bucketName,
                Key = keyName,
                CannedACL = S3CannedACL.PublicRead
            };

            await _amazonS3Client.PutACLAsync(premissionRequest);

            return Ok(new { Message = "Upload realizado com sucesso!", FileName = keyName });
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

            if (response.HttpStatusCode != System.Net.HttpStatusCode.NoContent)
                return BadRequest(new { Message = "Erro ao deletar o arquivo do S3." });

            return Ok(new { Message = "Arquivo deletado com sucesso!" });
        }

        private async Task BuildTree(string directory, FilesTreeNode currentNode)
        {
            var listObjectsRequest = new ListObjectsV2Request
            {
                BucketName = bucketName,
                Prefix = directory,
                Delimiter = $"{directory}/"
            };

            var listObjectsResponse = await _amazonS3Client.ListObjectsV2Async(listObjectsRequest);

            foreach (var commonPrefix in listObjectsResponse.CommonPrefixes)
            {
                var node = new FilesTreeNode
                {
                    Name = Path.GetFileName(commonPrefix.TrimEnd('/')),
                    IsDirectory = true
                };
                currentNode.Children.Add(node);
                await BuildTree(commonPrefix, node);
            }

            foreach (var s3Object in listObjectsResponse.S3Objects)
            {
                var node = new FilesTreeNode
                {
                    Name = Path.GetFileName(s3Object.Key),
                    IsDirectory = s3Object.Key.EndsWith("/"),
                    Path = $"https://{bucketName}.s3.{Amazon.RegionEndpoint.SAEast1.SystemName}.amazonaws.com/{s3Object.Key}",
                    FileExtension = Path.GetExtension(s3Object.Key)
                };

                currentNode.Children.Add(node);
            }
        }
    }

    public class TreeNodeHelper
    {
        public static List<FilesTreeNode> GetAllNodesAndChildren(IEnumerable<FilesTreeNode> nodes)
        {
            List<FilesTreeNode> result = new List<FilesTreeNode>();

            foreach (var node in nodes)
            {
                result.Add(node);
                result.AddRange(GetAllNodesAndChildren(node.Children));
            }

            return result;
        }
    }

    public class FilesTreeNode
    {
        public string Name { get; set; } = string.Empty;
        public bool IsDirectory { get; set; } = false;
        public string Path { get; set; } = string.Empty;
        public string FileExtension { get; set; } = string.Empty;
        public List<FilesTreeNode> Children { get; set; } = new List<FilesTreeNode>();
    }

}