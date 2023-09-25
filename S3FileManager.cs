using Amazon.S3.Model;
using Amazon.S3;
using GemBox.Document;
using GemBox.Pdf;
using System.IO;
using Microsoft.AspNetCore.Http;

namespace FileManager
{
    public class S3FileManager
    {
        private readonly IAmazonS3 _amazonS3Client;

        public S3FileManager(IAmazonS3 amazonS3)
        {
            _amazonS3Client = amazonS3;
            GemBox.Document.ComponentInfo.SetLicense("FREE-LIMITED-KEY");
            GemBox.Pdf.ComponentInfo.SetLicense("FREE-LIMITED-KEY");
        }

        public async Task<FilesTreeNode> GetAll(string path = "", string query = "")
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

            if (string.IsNullOrEmpty(query))
                return rootNode;

            List<FilesTreeNode> results = new();
            FileTreeBuilder.RecursiveSearch(rootNode, query, results);

            FilesTreeNode searchNode = new() { Children = results, IsDirectory = true };

            return searchNode;
        }

        public async Task Upload(IFormFile file, string path)
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

            var thumnailFilePath = $"{AppConstants.ThumbnailFolder}/{keyName}";
            await CreatePdfThumbnail(file, thumnailFilePath);
        }

        private async Task CreatePdfThumbnail(IFormFile file, string thumnailFilePath)
        {
            if (file.ContentType == "application/pdf")
            {
                using var memoryStream2 = new MemoryStream();
                await file.CopyToAsync(memoryStream2);
                await CreateThumbFromMemoryStream(memoryStream2, thumnailFilePath);
            }
        }

        private async Task CreateThumbFromMemoryStream(MemoryStream memoryStream, string thumnailFilePath)
        {
            var document = PdfDocument.Load(memoryStream);
            var page = document.Pages[0];

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

        public async Task Rename(string currentKey, string newKey)
        {
            var copyRequest = new CopyObjectRequest
            {
                SourceBucket = AppConstants.BucketName,
                SourceKey = currentKey,
                DestinationBucket = AppConstants.BucketName,
                DestinationKey = newKey,
            };

            var copyResponse = await _amazonS3Client.CopyObjectAsync(copyRequest);

            if (copyResponse.HttpStatusCode != System.Net.HttpStatusCode.OK)
                throw new Exception(copyResponse.ToString());

            var premissionRequest = new PutACLRequest
            {
                BucketName = AppConstants.BucketName,
                Key = newKey,
                CannedACL = S3CannedACL.PublicRead
            };

            await _amazonS3Client.PutACLAsync(premissionRequest);

            var deleteRequest = new DeleteObjectRequest
            {
                BucketName = AppConstants.BucketName,
                Key = currentKey
            };

            var deleteResponse = await _amazonS3Client.DeleteObjectAsync(deleteRequest);

            if (deleteResponse.HttpStatusCode != System.Net.HttpStatusCode.NoContent)
                throw new Exception(deleteResponse.ToString());

            if (currentKey.EndsWith(".pdf") && newKey.EndsWith(".pdf"))
            {
                var getRequest = new GetObjectRequest
                {
                    BucketName = AppConstants.BucketName,
                    Key = newKey
                };

                var thumnailFilePath = $"{AppConstants.ThumbnailFolder}/{newKey}";

                using var getResponse = await _amazonS3Client.GetObjectAsync(getRequest);
                using var fileStream = getResponse.ResponseStream;

                var memoryStream = new MemoryStream();
                fileStream.CopyTo(memoryStream);
                await CreateThumbFromMemoryStream(memoryStream, thumnailFilePath);
            }
        }
    }
}