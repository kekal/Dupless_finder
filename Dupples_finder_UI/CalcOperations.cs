using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using OpenCvSharp;
using OpenCvSharp.Flann;
using OpenCvSharp.XFeatures2D;

namespace Dupples_finder_UI
{
    internal class OperationsWithProgress
    {
        private static MainViewModel _vm;

        private int[] _iterations;
        private int[] _completedIterations;

        protected OperationsWithProgress(MainViewModel vm)
        {
            _vm = vm;
        }

        protected void EnablePublishingProgress()
        {
            _iterations = new[] { -1 };
            _completedIterations = new[] { -1 };
            _vm.Dispatcher?.BeginInvoke(new Func<bool>(() =>
            {
                _vm.CalcProgress = 0;
                _vm.IsProgrVisible = Visibility.Visible;
                return false;
            }));
            Thread.Sleep(1);
        }

        protected static void DisiblePublishingProgress()
        {
            _vm.Dispatcher?.BeginInvoke(new Func<bool>(() =>
            {
                _vm.IsProgrVisible = Visibility.Collapsed;
                return false;
            }));
            Thread.Sleep(1);
        }

        protected void UpdateIterationsCount()
        {
            Interlocked.Increment(ref _completedIterations[0]);

            var step = _iterations[0] / 500 + 1;
            double progress = 100.0 * _completedIterations[0] / _iterations[0];
            if (_completedIterations[0] % step == 0)
            {
                _vm.Dispatcher?.BeginInvoke(new Func<bool>(() =>
                {
                    _vm.CalcProgress = progress;
                    return false;
                }));
            }
            //Thread.Sleep(1);
        }

        protected void SetProgressIterationsScope(List<Task> elements)
        {
            _iterations[0] = elements.Count;
        }
    }

    internal class CalcOperations : OperationsWithProgress
    {
        public CalcOperations(MainViewModel vm) : base(vm) {}

        public ConcurrentDictionary<string, MatOfFloat> CalcSiftHashes(IEnumerable<ImageInfo> infos, out Task result, int thumbSize = 100)
        {
            Trace.WriteLine("CalcSiftHashes started");

            EnablePublishingProgress();

            var hashesDict = new ConcurrentDictionary<string, MatOfFloat>();

            var tasks = new List<Task>();
            foreach (ImageInfo info in infos)
            {
                var task = new Task(() =>
                {
                    Thread.CurrentThread.Priority = ThreadPriority.Lowest;

                    //Thread.Sleep(1);

                    //var mem = new MemoryStream();

                    //// copy to byte array
                    //int stride = ((BitmapImage)info.Image).PixelWidth * 4;
                    //byte[] buffer = new byte[stride * ((BitmapImage)info.Image).PixelHeight];
                    //((BitmapImage)info.Image).CopyPixels(buffer, stride, 0);

                    //// create bitmap
                    //Bitmap bitmap = new Bitmap(((BitmapImage)info.Image).PixelWidth, ((BitmapImage)info.Image).PixelHeight, PixelFormat.Format32bppArgb);

                    //// lock bitmap data
                    //BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, bitmap.PixelFormat);

                    //// copy byte array to bitmap data
                    //Marshal.Copy(buffer, 0, bitmapData.Scan0, buffer.Length);

                    //// unlock
                    //bitmap.UnlockBits(bitmapData);

                    //bitmap.Save(mem,ImageFormat.Bmp);


                    //Mat sourceMat = Cv2.ImDecode(mem.GetBuffer(), ImreadModes.Unchanged);

                    //var resizedMat = sourceMat.Resize(new OpenCvSharp.Size(thumbSize, thumbSize), 0, 0, InterpolationFlags.Nearest);

                    //var scale = (double)thumbSize / Max(sourceMat.Width, sourceMat.Height);
                    //var resizedMat = sourceMat.Resize(new OpenCvSharp.Size(0, 0), scale, scale, InterpolationFlags.Nearest);

                    //var grayScaledMat = new Mat();
                    //Cv2.CvtColor(resizedMat, grayScaledMat, ColorConversionCodes.BGR2GRAY);

                    //var siftPoints = SURF.Create(400);
                    SIFT siftPoints = SIFT.Create();

                    MatOfFloat descriptors = new MatOfFloat();

                    //var keypoints = siftPoints.Detect(info.StoredMat).ToArray();
                    //siftPoints.Compute(info.StoredMat, ref keypoints, descriptors);

                    double scale = Math.Min((float) thumbSize / info.StoredMat.Width, (float) thumbSize / info.StoredMat.Height);
                    Mat resized = info.StoredMat.Resize(new OpenCvSharp.Size(0, 0), scale, scale, InterpolationFlags.Area);
                    siftPoints.DetectAndCompute(resized, null, out KeyPoint[] keypoints, descriptors);
                    resized.Release();

                    if (!ValidateDescriptor(descriptors))
                    {
                        descriptors.Release();
                        descriptors = new MatOfFloat(thumbSize, thumbSize);
                    }
                    

                    hashesDict.TryAdd(info.FilePath, descriptors);

                    //resizedMat?.Dispose();
                    siftPoints.Dispose();
                    //grayScaledMat.Dispose();
                    //resizedMat.Release();
                    //sourceMat.Release();
                    UpdateIterationsCount();
                });

                tasks.Add(task);
            }

            SetProgressIterationsScope(tasks);

            foreach (var task in tasks)
            {
                task.Start();
            }

            result = Task.WhenAll(tasks.ToArray());
            result.ContinueWith(o => DisiblePublishingProgress());

            return hashesDict;
        }

        private bool ValidateDescriptor(MatOfFloat descriptors)
        {
            return descriptors.Width > 0 && descriptors.Height > 0;
        }

        public IEnumerable<PairSimilarityInfo> CreateMatchCollection(IDictionary<string, MatOfFloat> hasheDict)
        {
            EnablePublishingProgress();
            var similarities = new ConcurrentBag<PairSimilarityInfo>();
            var tasks = new List<Task>();
            var hashes = hasheDict.ToArray();

            for (int j = 0; j < hashes.Length; j++)
            {
                var j1 = j;
                for (var i = j + 1; i < hashes.Length; i++)
                {
                    if (hashes[j].Key == hashes[i].Key)
                    {
                        continue;
                    }
                    var i1 = i;

                    var task = new Task(() =>
                    {
                        //Thread.Sleep(1);
                        Thread.CurrentThread.Priority = ThreadPriority.Lowest;

                        double similarity = CalcSimilarity((hashes[j1], hashes[i1]));

                        similarities.Add(new PairSimilarityInfo(hashes[j1].Key, hashes[i1].Key, similarity));

                        UpdateIterationsCount();
                    });

                    tasks.Add(task);
                }
            }
            SetProgressIterationsScope(tasks);

            foreach (var t in tasks)
            {
                t.Start();
            }

            Task.WaitAll(tasks.ToArray());

            DisiblePublishingProgress();
            return similarities.OrderByDescending(o => o.Match);
        }

        private static double CalcSimilarity((KeyValuePair<string, MatOfFloat>, KeyValuePair<string, MatOfFloat>) pairOfHashes)
        {
            Thread.CurrentThread.Priority = ThreadPriority.Lowest;

            Mat image1 = pairOfHashes.Item1.Value.Resize(new OpenCvSharp.Size(64, 64));
            Mat image2 = pairOfHashes.Item2.Value.Resize(new OpenCvSharp.Size(64, 64));

            var result = GetPSNR(image1, image2);

            image1.Release();
            image2.Release();

            return result;

            var matchers = new List<DescriptorMatcher>
            {
                new BFMatcher(NormTypes.L2SQR, crossCheck: true),
                new FlannBasedMatcher(new KDTreeIndexParams(2), new SearchParams()),
                new FlannBasedMatcher(new LshIndexParams(10, 10, 2)),
                new FlannBasedMatcher()
            };

            DMatch[][] matches = { };
            foreach (var matcher in matchers)
            {
                try
                {
                    matches = matcher.KnnMatch(pairOfHashes.Item1.Value, pairOfHashes.Item2.Value, 2);
                    break;
                }
                catch {}
            }
           
            

            var goodMatches = new List<DMatch>();
            double ratio_thresh = 0.9;
            foreach (DMatch[] match in matches)
            {
                if (match.Length > 1 && match[0].Distance < ratio_thresh * match[1].Distance)
                {
                    goodMatches.Add(match[0]);
                }
            }


            if (matches.Length < 2)
            {
                matches = new List<DMatch[]> { new[] {DMatch.Empty(), DMatch.Empty()}}.ToArray();
                Trace.WriteLine($"All Flann matchers failed:\n\t{pairOfHashes.Item1.Key}\n\t{pairOfHashes.Item2.Key}");
            }

            matchers.ForEach(m => m.Dispose());

            return 1000.0 / goodMatches.Count;
        }

        private static double GetPSNR(Mat image1, Mat image2)
        {
            var difference = new Mat();
            Cv2.Absdiff(image1, image2, difference);                                    // |I1 - I2|

            var differenceInfloat = new Mat();
            difference.ConvertTo(differenceInfloat, MatType.CV_32F);                    // convert difference pixel data to float

            MatExpr squaredDifference = differenceInfloat.Mul(differenceInfloat);       // difference^2

            Scalar s = Cv2.Sum(squaredDifference);                                      // create float summs for each channel

            double total = s.Val0 + s.Val1 + s.Val2;                                    // sum all channels

            if (total <= 1e-10)                                                         // if no essential data, return 0
            {
                return 0;
            }

            double mse = total / (image1.Channels() * image1.Total());                  // average squared brightness amount of the pixel (Mean squared error)

            double psnr = 10.0 * Math.Log10(255 * 255 / mse);                           // get back to 0-255 brightness range and calc dB level of maximum image difference (Peak signal-to-noise ratio)
            
            
            difference.Release();
            differenceInfloat.Release();
            squaredDifference.Dispose();
            image1.Release();
            image2.Release();

            return psnr;
        }

    }
}