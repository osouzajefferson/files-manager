﻿using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;
using System.IO;

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
        public async Task<IActionResult> Index(string directory = "")
        {
            var rootNode = new FilesTreeNode { Name = "", IsDirectory = true, Path = "/" };

            var listObjectsRequest = new ListObjectsV2Request
            {
                BucketName = AppConstants.BucketName,
                Prefix = directory
            };

            ListObjectsV2Response response;

            do
            {
                response = await _amazonS3Client.ListObjectsV2Async(listObjectsRequest);
                foreach (var s3Object in response.S3Objects)
                {
                    FileTreeBuilder.InsertObjectIntoTree2(rootNode, s3Object, directory);
                }

                listObjectsRequest.ContinuationToken = response.NextContinuationToken;

            } while (response.IsTruncated);

            return Ok(rootNode);
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search(string query)
        {
            //if (string.IsNullOrEmpty(query))
            //{
            //    return BadRequest("Query parameter is required.");
            //}

            //var rootNode = new FilesTreeNode { Name = "", IsDirectory = true };
            //await BuildTree("", rootNode);

            //var matchingItems = FilterTreeNodes(rootNode, query).ToList();

            //var ret = TreeNodeHelper.GetAllNodesAndChildren(matchingItems);
            //foreach (var item in ret)
            //    item.Children = new();

            return Ok("");
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
                BucketName = AppConstants.BucketName,
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
                BucketName = AppConstants.BucketName,
                Key = keyName,
                InputStream = memoryStream,
                ContentType = file.ContentType
            };

            var response = await _amazonS3Client.PutObjectAsync(request);

            if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
                return BadRequest(new { Message = "Erro ao enviar o arquivo para o S3." });

            var premissionRequest = new PutACLRequest
            {
                BucketName = AppConstants.BucketName,
                Key = keyName,
                CannedACL = S3CannedACL.PublicRead
            };

            await _amazonS3Client.PutACLAsync(premissionRequest);

            return Ok(new { Message = "Upload realizado com sucesso!", FileName = keyName });
        }

        [HttpDelete("delete")]
        public async Task<IActionResult> DeleteFile(string objectKey)
        {
            var deleteObjectRequest = new DeleteObjectRequest
            {
                BucketName = AppConstants.BucketName,
                Key = objectKey
            };

            var response = await _amazonS3Client.DeleteObjectAsync(deleteObjectRequest);

            if (response.HttpStatusCode != System.Net.HttpStatusCode.NoContent)
                return BadRequest(new { Message = "Erro ao deletar o arquivo do S3." });

            return Ok(new { Message = "Arquivo deletado com sucesso!" });
        }
    }

    public static class TreeNodeHelper
    {
        public static List<FilesTreeNode> GetAllNodesAndChildren(IEnumerable<FilesTreeNode> nodes)
        {
            List<FilesTreeNode> result = new();

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
        public string BreadCrumbs { get; set; } = string.Empty;
    }
}