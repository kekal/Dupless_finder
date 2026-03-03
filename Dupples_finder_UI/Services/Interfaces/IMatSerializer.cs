using OpenCvSharp;

namespace Dupples_finder_UI.Services.Interfaces;

public interface IMatSerializer
{
    Mat Deserialize(byte[] data, int rows, int cols);
    byte[] Serialize(Mat mat);
}