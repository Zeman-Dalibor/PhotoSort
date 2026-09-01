namespace PhotoSort.Services;

/// <summary>Turns a file on disk into a bitmap no larger than <c>maxEdge</c> on its longest side.</summary>
public interface IImageDecoder
{
    DecodedImage Decode(string path, int maxEdge);
}
