using System.IO.Compression;
using System.Text;

namespace WebApi.Helpers;

public sealed class Helpers
{
    public static byte[] ZipStringBuilderToStringBuilder(StringBuilder stringBuilder)
    {
        // Convert StringBuilder content to byte array
        var bytes = Encoding.UTF8.GetBytes(stringBuilder.ToString());

        // Create a memory stream to hold the zipped bytes
        using MemoryStream memoryStream = new();
        // Create a new zip archive in the memory stream
        using ZipArchive archive = new(memoryStream, ZipArchiveMode.Create, true);
        // Create a zip entry for the content
        var zipEntry = archive.CreateEntry("data"); // The file name inside the zip

        // Write the content to the zip entry
        using var entryStream = zipEntry.Open();
        entryStream.Write(bytes, 0, bytes.Length);

        // Return the array of compressed bytes
        // Note: It's important to do this after the 'using' block closes the archive
        // Otherwise, the archive would not be written to the MemoryStream correctly
        return memoryStream.ToArray();
    }

    public static StringBuilder UnzipToStringBuilder(byte[] zippedData)
    {
        // Create a memory stream from the zipped byte array
        using MemoryStream zippedStream = new(zippedData);

        // Create a zip archive to read from the memory stream
        using ZipArchive archive = new(zippedStream);

        // Find the zip entry that contains our data
        // If you have more than one entry, you might need to adjust this code to find the correct one
        ZipArchiveEntry? entry = archive.GetEntry("data");
        if (entry is null)
        {
            throw new InvalidOperationException("The zip archive does not contain an entry named 'data'");
        }

        // Use a StringBuilder to collect all the text
        var stringBuilder = new StringBuilder();

        // Read the contents of the zip entry
        using var entryStream = entry.Open();
        using var streamReader = new StreamReader(entryStream, Encoding.UTF8);

        // Use the StreamReader to read the text content into the StringBuilder
        while (!streamReader.EndOfStream)
        {
            stringBuilder.AppendLine(streamReader.ReadLine());
        }

        return stringBuilder;
    }

    public static byte[] Compress(string data)
    {
        // convert the source string into a memory stream
        using (MemoryStream inMemStream = new MemoryStream(Encoding.UTF8.GetBytes(data)),
               outMemStream = new MemoryStream())
        {
            // create a compression stream with the output stream
            using (var zipStream = new DeflateStream(outMemStream, CompressionMode.Compress, true))
                // copy the source string into the compression stream
                inMemStream.WriteTo(zipStream);

            // return the compressed bytes in the output stream
            return outMemStream.ToArray();
        }
    }

    public static string Decompress(byte[] data)
    {
        // load the byte array into a memory stream
        using var inMemStream = new MemoryStream(data);
        using var decompressionStream = new DeflateStream(inMemStream, CompressionMode.Decompress);
        using var streamReader = new StreamReader(decompressionStream, Encoding.UTF8);
        return streamReader.ReadToEnd();
    }
}