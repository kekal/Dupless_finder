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
    internal class OperationsWithProgress
    {
        private static MainViewModel _vm;

        protected int[] Iterations;
        protected int[] CompletedIterations;

        public OperationsWithProgress(MainViewModel vm)
        {
            _vm = vm;
        }

        protected void EnablePublishingProgress()
        {
            Iterations = new[] { -1 };
            CompletedIterations = new[] { -1 };
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
            Interlocked.Increment(ref CompletedIterations[0]);
            PublishProgress();
        }

        protected void SetProgressIterationsScope(List<Task> elements)
        {
            Iterations[0] = elements.Count;
        }

        private void PublishProgress()
        {
            _vm.Dispatcher?.BeginInvoke(new Func<bool>(() =>
            {
                var progress = 100.0 * CompletedIterations[0] / Iterations[0];
                if (progress - _vm.CalcProgress > 0.1 )
                {
                    _vm.CalcProgress = progress;
                }
                return false;
            }));
            Thread.Sleep(1);
        }
    }

    internal class CalcOperations : OperationsWithProgress
    {
        public CalcOperations(MainViewModel vm) : base(vm) {}

        public ConcurrentDictionary<string, MatOfFloat> CalcSiftHashes(IEnumerable<ImageInfo> infos, out Task result, int thumbSize = 100)
        {
            Trace.WriteLine($"CalcSiftHashes started");

            EnablePublishingProgress();

            var hashesDict = new ConcurrentDictionary<string, MatOfFloat>();

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
                    SIFT siftPoints = SIFT.Create();

                    var descriptors = new MatOfFloat();

                    //var keypoints = siftPoints.Detect(info.StoredMat).ToArray();
                    //siftPoints.Compute(info.StoredMat, ref keypoints, descriptors);

                    double scale = Math.Min((float) thumbSize / info.StoredMat.Width, (float) thumbSize / info.StoredMat.Height);
                    Mat resized = info.StoredMat.Resize(new OpenCvSharp.Size(0, 0), scale, scale, InterpolationFlags.Area);
                    siftPoints.DetectAndCompute(resized, null, out KeyPoint[] keypoints, descriptors);
                    resized.Release();

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

        public IEnumerable<Result> CreateMatchCollection(IDictionary<string, MatOfFloat> hasheDict)
        {
            EnablePublishingProgress();
            var matchList = new ConcurrentBag<Result>();

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
            return matchList.Where(o1 => o1.Match < 1000000).OrderBy(o => Math.Abs(o.Match));
        }

        private static Tuple<double, double> CalcLinearFactors(KeyValuePair<string, MatOfFloat>[] hashArray, int j, int i, out float[] matchPoints)
        {
            Thread.CurrentThread.Priority = ThreadPriority.Lowest;
            var matcher = new BFMatcher(NormTypes.L2SQR, crossCheck: true);
            var bfMatches = matcher.Match(hashArray[j].Value, hashArray[i].Value).OrderBy(o => o.Distance).Take(10).Select(o => o.Distance).ToList();
            matcher.Dispose();

            matchPoints = bfMatches.Count < 2 ? new[] { Single.MaxValue, Single.MaxValue } : bfMatches.ToArray();

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
    }
}