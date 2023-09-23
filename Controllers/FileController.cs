﻿using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;
using GemBox.Pdf;
using GemBox.Pdf.Content;
using System.Drawing;
using GemBox.Document;

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
        public async Task<IActionResult> Index(string path = "", string query = "")
        {
            var rootNode = await GetAllFiles(path);

            if (string.IsNullOrEmpty(query))
                return Ok(rootNode);

            List<FilesTreeNode> results = new();
            FileTreeBuilder.RecursiveSearch(rootNode, query, results);

            FilesTreeNode searchNode = new() { Children = results, IsDirectory = true };
            return Ok(searchNode);

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
            GemBox.Document.ComponentInfo.SetLicense("FREE-LIMITED-KEY");
            GemBox.Pdf.ComponentInfo.SetLicense("FREE-LIMITED-KEY");

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

            var thumnailFilePath = $"{AppConstants.ThumbnailFolder}/{keyName}";

            if (file.ContentType == "application/pdf")
            {
                using var memoryStream2 = new MemoryStream();
                await file.CopyToAsync(memoryStream2);

                // Load PDF with GemBox.Pdf
                var document = PdfDocument.Load(memoryStream2);
                var page = document.Pages[0];

                // Extract the content of the first page into a new temporary PDF
                using var tempStream = new MemoryStream();
                using var tempDocument = new PdfDocument();
                tempDocument.Pages.AddClone(page);
                tempDocument.Save(tempStream);

                var imageOpt = new GemBox.Document.ImageSaveOptions
                {
                    PageCount = 1,
                    PageNumber = 0,
                    Format = GemBox.Document.ImageSaveFormat.Png,
                    DpiX = 96,
                    DpiY = 96,
                };

                using var imageStream = new MemoryStream();

                DocumentModel.Load(tempStream, new GemBox.Document.PdfLoadOptions { LoadType = PdfLoadType.HighFidelity }).Save(imageStream, imageOpt);

                var imageRequest = new PutObjectRequest
                {
                    BucketName = AppConstants.BucketName,
                    Key = thumnailFilePath,
                    InputStream = imageStream,
                    ContentType = "image/png"
                };

                var imageResponse = await _amazonS3Client.PutObjectAsync(imageRequest);
                if (imageResponse.HttpStatusCode != System.Net.HttpStatusCode.OK)
                    throw new Exception(imageResponse.ToString());

                var imagePermissionRequest = new PutACLRequest
                {
                    BucketName = AppConstants.BucketName,
                    Key = thumnailFilePath,
                    CannedACL = S3CannedACL.PublicRead
                };

                await _amazonS3Client.PutACLAsync(imagePermissionRequest);
            }
        }
    }
}