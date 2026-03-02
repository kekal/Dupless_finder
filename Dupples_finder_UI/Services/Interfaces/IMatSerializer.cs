using OpenCvSharp;

namespace Dupples_finder_UI.Services.Interfaces
{
    public interface IMatSerializer
    {
        byte[] Serialize(Mat mat);
        Mat Deserialize(byte[] data, int rows, int cols);
    }
}
