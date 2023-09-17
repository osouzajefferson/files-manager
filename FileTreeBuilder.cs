using Amazon.S3.Model;

namespace FileManager
{
    public static class FileTreeBuilder
    {
        public static void InsertObjectIntoTree(FilesTreeNode rootNode, S3Object s3Object, string prefix)
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
                        Path = $"{AppConstants.GetFullPath}/{s3Object.Key}",
                        FileExtension = isDirectory ? string.Empty : Path.GetExtension(part)
                    };


                    if (childNode.IsDirectory)
                        childNode.BreadCrumbs = childNode.Path.Replace($"{AppConstants.GetFullPath}", "").TrimEnd('/');

                    currentNode.Children.Add(childNode);
                }

                currentNode = childNode;
            }
        }
        public static void RecursiveSearch(FilesTreeNode node, string searchText, List<FilesTreeNode> results)
        {
            if (node.Name.Contains(searchText, StringComparison.InvariantCultureIgnoreCase))
                results.Add(new FilesTreeNode
                {
                    Name = node.Name,
                    IsDirectory = node.IsDirectory,
                    BreadCrumbs = node.BreadCrumbs,
                    FileExtension = node.FileExtension,
                    Children = new(),
                    Path = node.Path
                });

            foreach (var child in node.Children)
            {
                RecursiveSearch(child, searchText, results);
            }
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