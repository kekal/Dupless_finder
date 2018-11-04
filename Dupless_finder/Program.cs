using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;


namespace Dupless_finder
{
    class Program
    {
        static void Main(string[] args)
        {
            var size = 16;


            double qwer;
            var bmp1 = new Bitmap("7.jpg");
            var bmp2 = new Bitmap("8.jpg");
            var bmp1c = SquareCrop(bmp1);
            var bmp2c = SquareCrop(bmp2);
            var bmp1d = DownScale(bmp1c, size);
            var bmp2d = DownScale(bmp2c, size);
            var bmp1g = ConvertToGray(bmp1d);
            var bmp2g = ConvertToGray(bmp2d);
            var bmp1a = AutoContrast(bmp1g);
            var bmp2a = AutoContrast(bmp2g);
            var bmp1p = Threashold(bmp1a);
            var bmp2p = Threashold(bmp2a);



//            bmp1g.Save("7t.png");
//            bmp2g.Save("8t.png");
            bmp1p.Save("7a.png");
            bmp2p.Save("8a.png");

            qwer = Compare2(bmp1p, bmp2p, true);
            bmp1p.Save("7diff.png");



            Console.WriteLine(qwer * 100 + "%");
        }

        private static Bitmap SquareCrop(Bitmap bmp)
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

        private static Bitmap DownScale(Bitmap input, int edge)
        {
            using (var brush = new SolidBrush(Color.White))
            {
                float scale = (float) edge / Math.Max(input.Width, input.Height);

                int scaleWidth = (int) (input.Width * scale);
                int scaleHeight = (int) (input.Height * scale);

                var output = new Bitmap(scaleWidth, scaleHeight,input.PixelFormat);
                using (var graph = Graphics.FromImage(output))
                {
                    graph.FillRectangle(brush, new RectangleF(0, 0, scaleWidth, scaleHeight));
                    graph.DrawImage(input, 0, 0, scaleWidth, scaleHeight);
                    return output;
                }
            }
        }

        private static Bitmap ConvertToGray(Bitmap inputBitmap)
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

        private static Bitmap AutoContrast(Bitmap inputBitmap)
        {
            BitmapInfo inputInfo = new BitmapInfo(inputBitmap);

            var preset = 0;
            var max = inputInfo.RgbValues.Max();// - (inputInfo.RgbValues.Max() + preset <= byte.MaxValue ? -preset : 0);

            var min = inputInfo.RgbValues.Min() + (inputInfo.RgbValues.Min() - preset >= 0             ?  preset : 0);

            int range = max - min + 1;

            for (var i = 0; i < inputInfo.RgbValues.Length; i++)
            {
                inputInfo.RgbValues[i] = (byte)(((double)inputInfo.RgbValues[i] - min) / range * byte.MaxValue);
            }

            inputInfo.Dispose();
            return inputInfo.PopulateToBitmap();
        }

        private static Bitmap Threashold(Bitmap inputBitmap, byte threashold)
        {
            BitmapInfo qwer = new BitmapInfo(inputBitmap);
            for (int i = 0; i < qwer.RgbValues.Length; i++)
            {
                qwer.RgbValues[i] = qwer.RgbValues[i] > threashold ? byte.MaxValue : byte.MinValue;
            }

            qwer.Dispose();
            return qwer.PopulateToBitmap();
        }

        private static Bitmap Threashold(Bitmap inputBitmap)
        {
            BitmapInfo qwer = new BitmapInfo(inputBitmap);

            byte[] temp = new byte[qwer.RgbValues.Length];

            Array.Copy(qwer.RgbValues, temp, qwer.RgbValues.Length);
            Array.Sort(temp);
            var threashold = temp[qwer.RgbValues.Length / 2];

            qwer.Dispose();
            return Threashold(inputBitmap, threashold);
        }
 

        private static double Compare2(Bitmap bmp1, Bitmap bmp2, bool showdiff = false)
        {
            if (ReferenceEquals(bmp1,bmp2))
            {
                return 1;
            }

            Rectangle rect = new Rectangle(0, 0, bmp1.Width, bmp1.Height);
            BitmapData bmpData1 = bmp1.LockBits(rect, ImageLockMode.ReadOnly, bmp1.PixelFormat);
            BitmapData bmpData2 = bmp2.LockBits(rect, ImageLockMode.ReadOnly, bmp2.PixelFormat);

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
                    if (showdiff) {rgbValues1[i] = byte.MaxValue;}
                    
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

            return (double)equalCount / bitmapSize;
        }

        private static double Compare(Bitmap bmp1, Bitmap bmp2)
        {
            ulong equalCount = 0;
            ulong totalCount = 0;
            Rectangle rect = new Rectangle(0, 0, bmp1.Width, bmp1.Height);
            BitmapData bmpData1 = bmp1.LockBits(rect, ImageLockMode.ReadOnly, bmp1.PixelFormat);
            BitmapData bmpData2 = bmp2.LockBits(rect, ImageLockMode.ReadOnly, bmp2.PixelFormat);

            unsafe
            {
                byte* ptr1 = (byte*) bmpData1.Scan0.ToPointer();
                byte* ptr2 = (byte*) bmpData2.Scan0.ToPointer();
                int width = rect.Width * 3; // for 24bpp pixel data

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
            return (double)equalCount / totalCount;
        }


        class BitmapInfo : IDisposable
        {
            public Rectangle BoundaryRect;
            public BitmapData BmpData;
            public Bitmap Bitmap;
            public IntPtr Ptr;
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

            public void Dispose()
            {
                if (Bitmap != null && BmpData != null)
                {
                    try
                    {
                        Bitmap.UnlockBits(BmpData);
                    }
                    catch (Exception){}
                }
            }

            ~BitmapInfo()
            {
                Dispose();
            }
        }
    }
}
