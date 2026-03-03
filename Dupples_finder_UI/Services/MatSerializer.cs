using System.Runtime.InteropServices;
using Dupples_finder_UI.Services.Interfaces;
using OpenCvSharp;

namespace Dupples_finder_UI.Services;

public class MatSerializer : IMatSerializer
{
    public Mat Deserialize(byte[] data, int rows, int cols)
    {
        if (data == null || data.Length == 0)
        {
            return null;
        }

        var mat = new Mat(rows, cols, MatType.CV_32FC1);
        Marshal.Copy(data, 0, mat.Data, data.Length);
        return mat;
    }

    public byte[] Serialize(Mat mat)
    {
        if (mat == null || mat.Empty())
        {
            return null;
        }

        var totalBytes = (int)(mat.Total() * mat.ElemSize());
        var bytes = new byte[totalBytes];
        Marshal.Copy(mat.Data, bytes, 0, totalBytes);
        return bytes;
    }
}