namespace FileManager
{
    public static class AppConstants
    {
        public static string AccessKey => "AKIA4T5ULDU5WOCLDIAY";
        public static string SecretKey => "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";
        public static string BucketName => "test-public-files-prov";
        public static string GetFullPath => $"https://{BucketName}.s3.{Amazon.RegionEndpoint.SAEast1.SystemName}.amazonaws.com";
        public static string ThumbnailFolder => "_thumbs";
        public static string ThumbnailPath => $"{GetFullPath}/{ThumbnailFolder}";

    }
}
