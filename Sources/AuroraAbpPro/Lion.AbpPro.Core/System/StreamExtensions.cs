namespace System;

public static class StreamExtensions
{
    public static async Task<Stream> ToReadableStreamAsync(this Stream originalStream)
    {
        if (originalStream.CanRead && originalStream.CanSeek)
        {
            originalStream.Position = 0;
            return originalStream;
        }

        var memoryStream = new MemoryStream();
        await originalStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;
        return memoryStream;
    }

    public static async Task<MemoryStream> CloneAsync(this Stream stream)
    {
        var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        ms.Position = 0;
        return ms;
    }
}
