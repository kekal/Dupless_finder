using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

namespace Dupless_finder
{
    public static class ImageProcessing
    {

        public static Bitmap GrayScaleThumbnail(Bitmap bmp, int size = 200)
        {
            //color to wipe the canvas
            var brush = new SolidBrush(Color.White);
            //smaller pic size
            int lowerSize = Math.Min(bmp.Width, bmp.Height);

            //matrix correction for gray scales for each channel
            ColorMatrix colorMatrix = new ColorMatrix(
                new float[][]
                {
                    new float[] {.3f,   .3f,    .3f,        0,      0},
                    new float[] {.59f,  .59f,   .59f,       0,      0},
                    new float[] {.11f,  .11f,   .11f,       0,      0},
                    new float[] {0,        0,      0,       1,      0},
                    new float[] {0,        0,      0,       0,      1}
                });
            ImageAttributes attributes = new ImageAttributes();
            attributes.SetColorMatrix(colorMatrix);

            //result bitmap
            var output = new Bitmap(size, size, bmp.PixelFormat);
            using (var graph = Graphics.FromImage(output))
            {
                //wiping white
                graph.FillRectangle(brush, new RectangleF(0, 0, size, size));
                //print result
                graph.DrawImage(
                    image: bmp, 
                    destRect: new Rectangle(0, 0, output.Width, output.Height), 
                    srcX: 0, 
                    srcY: 0, 
                    srcWidth: lowerSize, 
                    srcHeight: lowerSize, 
                    srcUnit: GraphicsUnit.Pixel, 
                    imageAttr: attributes);
            }

            //output.Save("7t.png");



            //BitmapInfo inputInfo = new BitmapInfo(output);
            //var max1 = inputInfo.RgbValues.Max();
            //var min1 = inputInfo.RgbValues.Min();
            //int range1 = max1 - min1;


            //==================================================================================

            //var val = (byte)(1 - min1) / range1 * byte.MaxValue;

            //colorMatrix = new ColorMatrix(
            //    new float[][]
            //    {
            //        new float[] {1, 0,  0,      0,      0},
            //        new float[] {0, 1,  0,      0,      0},
            //        new float[] {0, 0,  1,      0,      0},
            //        new float[] {0, 0,  0,      1,      0},
            //        new float[] {44f/255,   44f/255,    44f/255,        0,      1}
            //    });
            //attributes = new ImageAttributes();
            //attributes.SetColorMatrix(colorMatrix);

            //var output2 = new Bitmap(size, size, bmp.PixelFormat);
            //using (var graph = Graphics.FromImage(output2))
            //{
            //    graph.FillRectangle(brush, new RectangleF(0, 0, size, size));
            //    graph.DrawImage(bmp, new Rectangle(0, 0, output2.Width, output2.Height), 0, 0, lowerSize, lowerSize, GraphicsUnit.Pixel, attributes);

            //}

            //output.Save("7t1.png");

            //inputInfo = new BitmapInfo(output2);
            //var max2 = inputInfo.RgbValues.Max();
            //var min2 = inputInfo.RgbValues.Min();
            //int range2 = max2 - min2;

            return output;
        }


        public static Bitmap SquareCrop(Bitmap bmp)
        {
            int lowerSize = Math.Min(bmp.Width, bmp.Height);

            Rectangle cropRect = new Rectangle(0, 0, lowerSize, lowerSize);
            Bitmap output = new Bitmap(cropRect.Width, cropRect.Height, bmp.PixelFormat);

            using (var g = Graphics.FromImage(output))
            {
                g.DrawImage(bmp, new Rectangle(0, 0, output.Width, output.Height), cropRect, GraphicsUnit.Pixel);
            }
            return output;
        }

        public static Bitmap DownScale(Bitmap input, int edge)
        {
            using (var brush = new SolidBrush(Color.White))
            {
                float scale = (float) edge / Math.Max(input.Width, input.Height);

                int scaleWidth = (int) (input.Width * scale);
                int scaleHeight = (int) (input.Height * scale);

                var output = new Bitmap(scaleWidth, scaleHeight, input.PixelFormat);
                using (var graph = Graphics.FromImage(output))
                {
                    graph.FillRectangle(brush, new RectangleF(0, 0, scaleWidth, scaleHeight));
                    graph.DrawImage(input, 0, 0, scaleWidth, scaleHeight);
                    return output;
                }
            }
        }

        public static Bitmap ConvertToGray(Bitmap inputBitmap)
        {
            Bitmap newBitmap = new Bitmap(inputBitmap.Width, inputBitmap.Height);


            ColorMatrix colorMatrix = new ColorMatrix(
                new float[][]
                {
                    new float[] {.3f, .3f, .3f, 0, 0},
                    new float[] {.59f, .59f, .59f, 0, 0},
                    new float[] {.11f, .11f, .11f, 0, 0},
                    new float[] {0, 0, 0, 1, 0},
                    new float[] {0, 0, 0, 0, 1}
                });

            ImageAttributes attributes = new ImageAttributes();
            attributes.SetColorMatrix(colorMatrix);
            using (Graphics g = Graphics.FromImage(newBitmap))
            {
                g.DrawImage(inputBitmap, new Rectangle(0, 0, inputBitmap.Width, inputBitmap.Height), 0, 0, inputBitmap.Width, inputBitmap.Height, GraphicsUnit.Pixel, attributes);
            }

            return newBitmap;
        }

        public static Bitmap AutoContrast(Bitmap inputBitmap)
        {
            BitmapInfo inputInfo = new BitmapInfo(inputBitmap);

            var preset = 0;
            var max = inputInfo.RgbValues.Max(); // - (inputInfo.RgbValues.Max() + preset <= byte.MaxValue ? -preset : 0);

            var min = inputInfo.RgbValues.Min() + (inputInfo.RgbValues.Min() - preset >= 0 ? preset : 0);

            int range = max - min + 1;

            for (var i = 0; i < inputInfo.RgbValues.Length; i++)
            {
                inputInfo.RgbValues[i] = (byte) (((double) inputInfo.RgbValues[i] - min) / range * byte.MaxValue);
            }

            inputInfo.Dispose();
            return inputInfo.PopulateToBitmap();
        }

        public static Bitmap Threashold(Bitmap inputBitmap, byte threashold)
        {
            BitmapInfo qwer = new BitmapInfo(inputBitmap);
            for (int i = 0; i < qwer.RgbValues.Length; i++)
            {
                qwer.RgbValues[i] = qwer.RgbValues[i] > threashold ? byte.MaxValue : byte.MinValue;
            }

            qwer.Dispose();
            return qwer.PopulateToBitmap();
        }

        public static Bitmap Threashold(Bitmap inputBitmap)
        {
            BitmapInfo inputBitmapInfo = new BitmapInfo(inputBitmap);

            byte[] temp = new byte[inputBitmapInfo.RgbValues.Length];

            Array.Copy(inputBitmapInfo.RgbValues, temp, inputBitmapInfo.RgbValues.Length);
            Array.Sort(temp);
            var threashold = temp[inputBitmapInfo.RgbValues.Length / 2];

            inputBitmapInfo.Dispose();
            return Threashold(inputBitmap, threashold);
        }

        public static Bitmap[] CreateAllTransformations(Bitmap bmp)
        {
            Bitmap[] transformations = new Bitmap[7];
            for (int i = 0; i < 7; i++)
            {
                transformations[i] = (Bitmap) bmp.Clone();
                transformations[i].RotateFlip((RotateFlipType) i);
            }
            return transformations;
        }

        public static double Compare2(Bitmap bmp1, Bitmap bmp2, bool showdiff = false)
        {
            if (ReferenceEquals(bmp1, bmp2))
            {
                return 1;
            }

            Rectangle rect = new Rectangle(0, 0, bmp1.Width, bmp1.Height);
            BitmapData bmpData1 = bmp1.LockBits(rect, ImageLockMode.ReadWrite, bmp1.PixelFormat);
            BitmapData bmpData2 = bmp2.LockBits(rect, ImageLockMode.ReadWrite, bmp2.PixelFormat);

            var ptr1 = bmpData1.Scan0;
            var ptr2 = bmpData2.Scan0;

            int subPixelCount = bmpData1.Stride / bmp1.Width;
            int bitmapSize = Math.Abs(bmpData1.Stride) * bmpData1.Height;

            byte[] rgbValues1 = new byte[bitmapSize];
            byte[] rgbValues2 = new byte[bitmapSize];

            Marshal.Copy(ptr1, rgbValues1, 0, bitmapSize);
            Marshal.Copy(ptr2, rgbValues2, 0, bitmapSize);

            ulong equalCount = 0;
            for (var i = 0; i < bitmapSize; i++)
            {
                if (rgbValues1[i] == rgbValues2[i])
                {
                    equalCount++;
                    if (showdiff)
                    {
                        rgbValues1[i] = byte.MaxValue;
                    }
                }
                else if (showdiff)
                {
                    rgbValues1[i] = 0;
                    rgbValues1[i + 1] = 0;
                    rgbValues1[i + 2] = 0;
                    i += 2;
                }
            }

            Marshal.Copy(rgbValues1, 0, ptr1, bitmapSize);

            bmp1.UnlockBits(bmpData1);
            bmp2.UnlockBits(bmpData2);

            return (double) equalCount / bitmapSize;
        }

        public static double Compare(Bitmap bmp1, Bitmap bmp2)
        {
            ulong equalCount = 0;
            ulong totalCount = 0;
            Rectangle rect = new Rectangle(0, 0, bmp1.Width, bmp1.Height);
            BitmapData bmpData1 = bmp1.LockBits(rect, ImageLockMode.ReadOnly, bmp1.PixelFormat);
            BitmapData bmpData2 = bmp2.LockBits(rect, ImageLockMode.ReadOnly, bmp2.PixelFormat);

            int subPixelCount = bmpData1.Stride / bmp1.Width;
            unsafe
            {
                byte* ptr1 = (byte*) bmpData1.Scan0.ToPointer();
                byte* ptr2 = (byte*) bmpData2.Scan0.ToPointer();
                int width = rect.Width * subPixelCount; // for 24bpp pixel data

                for (int y = 0; y < rect.Height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        totalCount++;
                        if (*ptr1 == *ptr2)
                        {
                            equalCount++;
                        }
                        ptr1++;
                        ptr2++;
                    }
                    ptr1 += bmpData1.Stride - width;
                    ptr2 += bmpData2.Stride - width;
                }
            }
            bmp1.UnlockBits(bmpData1);
            bmp2.UnlockBits(bmpData2);
            return (double) equalCount / totalCount;
        }

        public class BitmapInfo : IDisposable
        {
            public Rectangle BoundaryRect;
            public BitmapData BmpData;
            public Bitmap Bitmap;
            private IntPtr Ptr;
            public int SubPixelCount;
            public int BitmapSize;
            public byte[] RgbValues;

            public BitmapInfo(Bitmap bitmap, ImageLockMode lockMode = ImageLockMode.ReadOnly)
            {
                Bitmap = bitmap;
                BoundaryRect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                BmpData = bitmap.LockBits(BoundaryRect, lockMode, bitmap.PixelFormat);
                Ptr = BmpData.Scan0;
                BitmapSize = Math.Abs(BmpData.Stride) * bitmap.Height;
                RgbValues = new byte[BitmapSize];
                Marshal.Copy(Ptr, RgbValues, 0, BitmapSize);
                SubPixelCount = BmpData.Stride / bitmap.Width;
            }

            public Bitmap PopulateToBitmap()
            {
                Bitmap outputBitmap = new Bitmap(BoundaryRect.Width, BoundaryRect.Height, Bitmap.PixelFormat);
                var outputInfo = new BitmapInfo(outputBitmap);
                Marshal.Copy(RgbValues, 0, outputInfo.Ptr, BitmapSize);
                outputInfo.Dispose();
                return outputInfo.Bitmap;
            }

            private bool IsDisposed { get; set; }
            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            protected virtual void Dispose(bool disposing)
            {
                if (!IsDisposed)
                {
                    if (disposing && (Bitmap != null && BmpData != null))
                    {
                        try
                        {
                            Bitmap.UnlockBits(BmpData);
                        }
                        catch {}
                    }
                    IsDisposed = true;
                }
            }

            ~BitmapInfo()
            {
                Dispose(false);
            }
        }
    }
}