namespace FileManager
{
    public static class StringExtensinos
    {
        public static string RemoveLastSegment(this string input, char delimiter = '/')
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            string[] parts = input.Split(delimiter);

            if (parts.Length <= 1)
            {
                return input;
            }

            return string.Join(delimiter.ToString(), parts.Take(parts.Length - 1));
        }
    }
}
