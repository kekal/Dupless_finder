using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using OpenCvSharp;
using OpenCvSharp.XFeatures2D;

namespace Dupples_finder_UI
{
    internal class CalcOperations
    {
        private static MainViewModel _vm;

        public CalcOperations(MainViewModel vm)
        {
            _vm = vm;
        }

        private static void EnablePublishingProgress()
        {
            _vm.Dispatcher?.BeginInvoke(new Func<bool>(() =>
            {
                _vm.IsProgrVisible = Visibility.Visible;
                return false;
            }));
        }

        private static void PublishProgress(int[] completedIterations, int[] iterations)
        {
            _vm.Dispatcher?.BeginInvoke(new Func<bool>(() =>
            {
                _vm.CalcProgress = 100.0 * completedIterations[0] / iterations[0];
                return false;
            }));
        }

        public IEnumerable<Result> CreateMatchCollection(IDictionary<string, MatOfFloat> hasheDict)
        {
            EnablePublishingProgress();
            var matchList = new ConcurrentBag<Result>();

            int[] iterations = {-1};
            int[] completedIterations = {-1};

            var tasks = new List<Task>();
            var hashes = hasheDict.ToArray();
            for (var j = 0; j < hashes.Length; j++)
            {
                for (var i = j + 1; i < hashes.Length; i++)
                {
                    if (hashes[j].Key == hashes[i].Key)
                    {
                        continue;
                    }
                    //Thread.Sleep(1);
                    
                    var i1 = i;
                    var j1 = j;
                    var task = new Task(() =>
                    {
                        Thread.CurrentThread.Priority = ThreadPriority.Lowest;
 

                        var linearFactors = CalcLinearFactors(hashes, j1, i1, out float[] matchPoints);

                        matchList.Add(new Result(hashes[j1].Key, hashes[i1].Key, linearFactors.Item1, matchPoints));

                        Interlocked.Increment(ref completedIterations[0]);
                        PublishProgress(completedIterations, iterations);
                    });

                    tasks.Add(task);
                }
            }
            iterations[0] = tasks.Count;

            foreach (var t in tasks)
            {
                t.Start();
            }

            Task.WaitAll(tasks.ToArray());
            return matchList.Where(o1 => o1.Match < 1000000).OrderBy(o => Math.Abs(o.Match));
        }

        private static Tuple<double, double> CalcLinearFactors(KeyValuePair<string, MatOfFloat>[] hashArray, int j, int i, out float[] matchPoints)
        {
            Thread.CurrentThread.Priority = ThreadPriority.Lowest;
            var matcher = new BFMatcher(NormTypes.L2SQR, crossCheck: true);
            var bfMatches = matcher.Match(hashArray[j].Value, hashArray[i].Value).OrderBy(o => o.Distance).Take(10).Select(o => o.Distance).ToList();
            matcher.Dispose();

            matchPoints = bfMatches.Count < 2 ? new[] {Single.MaxValue, Single.MaxValue} : bfMatches.ToArray();

            var xes = new List<double>();
            var yes = new List<double>();

            for (int k = 0; k < matchPoints.Length; k++)
            {
                xes.Add(k);
                yes.Add(matchPoints[k]);
            }

            var linearFactors = MathNet.Numerics.Fit.Line(xes.ToArray(), yes.ToArray());
            return linearFactors;
        }

        public static Task CalcSiftHashes(MainViewModel mainViewModel, IEnumerable<ImageInfo> infos, int thumbSize = 100)
        {
            Trace.WriteLine($"CalcSiftHashes started");
            var currentProgress = 0.0;
            var minProgressStep = 100.0 / infos.Count();

            if (mainViewModel._hashesDict?.Values != null)
            {
                foreach (var mat in mainViewModel._hashesDict.Values)
                {
                    mat?.Release();
                }
            }

            mainViewModel._hashesDict = new ConcurrentDictionary<string, MatOfFloat>();

            var tasks = new List<Task>();
            foreach (ImageInfo info in infos)
            {
                var task = new Task(() =>
                {
                    Thread.CurrentThread.Priority = ThreadPriority.Lowest;

                    Thread.Sleep(1);

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
                    var siftPoints = SIFT.Create();

                    var descriptors = new MatOfFloat();

                    //var keypoints = siftPoints.Detect(info.StoredMat).ToArray();
                    //siftPoints.Compute(info.StoredMat, ref keypoints, descriptors);

                    double scale = Math.Min(60.0 / info.StoredMat.Width, 60.0 / info.StoredMat.Height);
                    Mat resized = info.StoredMat.Resize(new OpenCvSharp.Size(0, 0), scale, scale,
                        InterpolationFlags.Area);
                    siftPoints.DetectAndCompute(resized, null, out KeyPoint[] keypoints, descriptors);
                    resized.Release();

                    mainViewModel._hashesDict.TryAdd(info.FilePath, descriptors);

                    //resizedMat?.Dispose();
                    siftPoints.Dispose();
                    //grayScaledMat.Dispose();
                    //resizedMat.Release();
                    //sourceMat.Release();

                    currentProgress += minProgressStep;
                    mainViewModel.Dispatcher?.BeginInvoke(new Func<bool>(() =>
                    {
                        mainViewModel.CalcProgress = currentProgress;
                        if (Math.Abs(currentProgress - 100) < 0.1)
                        {
                            mainViewModel.IsProgrVisible = Visibility.Collapsed;
                        }
                        return false;
                    }));
                });

                tasks.Add(task);
            }
            foreach (var task in tasks)
            {
                task.Start();
            }
            return Task.WhenAll(tasks.ToArray());
        }
    }
}