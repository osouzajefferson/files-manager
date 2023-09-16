using Amazon.S3.Model;
using FileManager.Controllers;

namespace FileManager
{
    public static class FileTreeBuilder
    {
        public static void InsertObjectIntoTree2(FilesTreeNode rootNode, S3Object s3Object, string prefix)
        {
            var relativePath = s3Object.Key.Substring(prefix.Length).Trim('/');
            var parts = relativePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var currentNode = rootNode;

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var isDirectory = i < parts.Length - 1 || s3Object.Key.EndsWith("/");

                var childNode = currentNode.Children.FirstOrDefault(x => x.Name == part);
                if (childNode == null)
                {
                    childNode = new FilesTreeNode
                    {
                        Name = part,
                        IsDirectory = isDirectory,
                        Path = s3Object.Key,
                        FileExtension = isDirectory ? string.Empty : System.IO.Path.GetExtension(part)
                    };


                    if (childNode.IsDirectory)
                        childNode.BreadCrumbs = childNode.Path.Replace($"{AppConstants.GetFullPath}", "").TrimEnd('/');

                    currentNode.Children.Add(childNode);
                }

                currentNode = childNode;
            }
        }



        public static void InsertObjectIntoTree(FilesTreeNode rootNode, S3Object s3Object)
        {
            var parts = s3Object.Key.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var currentNode = rootNode;

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var isDirectory = i < parts.Length - 1 || s3Object.Key.EndsWith("/");

                var childNode = currentNode.Children.FirstOrDefault(x => x.Name == part);
                if (childNode == null)
                {
                    childNode = new FilesTreeNode
                    {
                        Name = part,
                        IsDirectory = isDirectory,
                        Path = s3Object.Key,
                        FileExtension = isDirectory ? string.Empty : Path.GetExtension(part)
                    };

                    if (childNode.IsDirectory)
                        childNode.BreadCrumbs = childNode.Path.Replace($"{AppConstants.GetFullPath}", "").TrimEnd('/');

                    currentNode.Children.Add(childNode);
                }

                currentNode = childNode;
            }
        }
    }
}