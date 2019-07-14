using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using OpenCvSharp.XFeatures2D;
using static System.Math;
using Size = OpenCvSharp.Size;


namespace Dupless_finder
{
    public class Program
    {
        private static int KEYPOINTS_NUMBER = 1000;
        private static int THUMB_SIZE = 200;

        static void Main(string[] args)
        {
            DateTime time1, time2;
            time1 = DateTime.Now;
            var size = THUMB_SIZE;
            var images = DirSearch(args[0], ".png", ".jpg");


            if (args.Any(o => o.Equals("hard")))
            {
                var hashes = CreateHashes(images, size);

                var sourceCollection = CreateMatchCollection(hashes);

                File.WriteAllLines("log.txt", sourceCollection.Reverse().Select(r => r.ToString()));

                foreach (var result in sourceCollection.Reverse())
                {
                    Console.WriteLine(result);
                }
                
            }
            else
            {
                IEnumerable<IDictionary<string, Bitmap[]>> sourceCollection2 = CreateSourceCollection2(images, size);

                IEnumerable<KeyValuePair<string, Bitmap>> flatDict = CreateFlatBitmapCollection(sourceCollection2);


                foreach (var result in GetMatches(flatDict).OrderBy(kv => kv.Match))
                {
                    Console.Write(result.Match * 100 + " %\t");
                    Console.Write(result.Name1 + "\t");
                    Console.WriteLine(result.Name2);
                }
            }
            time2 = DateTime.Now;


        }


        public static IDictionary<string, MatOfFloat> CreateHashes(IEnumerable<string> pathes, int thumbSize)
        {
            var hashesDict = new ConcurrentDictionary<string, MatOfFloat>();
            var tasks = new List<Task>();
            foreach (var path in pathes)
            {
                var task = new Task(() =>
                {
                    var sourceMat = new Mat(path);

                    var scale = (double) thumbSize / Max(sourceMat.Width, sourceMat.Height);
                    sourceMat = sourceMat.Resize(new Size(0, 0), scale, scale, InterpolationFlags.Nearest);
                    var gray = new Mat();


                    Cv2.CvtColor(sourceMat, gray, ColorConversionCodes.BGR2GRAY);

                    var sift = SIFT.Create();

                    var descriptors = new MatOfFloat();
                    
                    Console.WriteLine("Creating hash for " + path);
                    //var keypoints = sift.Detect(gray).Take(KEYPOINTS_NUMBER).ToArray();
                    //sift.Compute(gray, ref keypoints, descriptors);
                    sift.DetectAndCompute(gray, null, out KeyPoint[] keypoints, descriptors);
                    hashesDict.TryAdd(path, descriptors);
                });
                tasks.Add(task);

                task.Start();
            }
            Task.WaitAll(tasks.ToArray());
            return hashesDict;
        }


        private static IEnumerable<IDictionary<string, Bitmap[]>> CreateSourceCollection(string[] qwer1)
        {
            var size = THUMB_SIZE;

            Console.WriteLine("bmps");
            var bmps = qwer1.Select(o => new Bitmap(o)).ToList();
            Console.WriteLine("crops");
            var crops = bmps.Select(ImageProcessing.SquareCrop).ToList();
            Console.WriteLine("resizes");
            var resizes = crops.Select(o => ImageProcessing.DownScale(o, size)).ToList();
            Console.WriteLine("grayScale");
            var grayScale = resizes.Select(ImageProcessing.ConvertToGray).ToList();
            Console.WriteLine("contrasts");
            var contrasts = grayScale.Select(ImageProcessing.AutoContrast).ToList();
            Console.WriteLine("blackandwhite");
            var blackandwhite = contrasts.Select(ImageProcessing.Threashold).ToList();
            Console.WriteLine("transformations");
            var transformations = blackandwhite.Select(ImageProcessing.CreateAllTransformations).ToList();


            var sourceCollection = new List<IDictionary<string, Bitmap[]>>();

            for (var i = 0; i < qwer1.Length; i++)
            {
                sourceCollection.Add(new Dictionary<string, Bitmap[]> { { qwer1[i], transformations.ElementAt(i) } });
            }

            return sourceCollection;
        }

        private static IEnumerable<IDictionary<string, Bitmap[]>> CreateSourceCollection2(string[] qwer1, int size = 200)
        {

            Console.WriteLine("bmps");
            var bmps = qwer1.Select(o => new Bitmap(o)).ToList();
            Console.WriteLine("crops");
            var grayThumbs = bmps.Select(bitmap => ImageProcessing.GrayScaleThumbnail(bitmap, size)).ToList();
            Console.WriteLine("blackandwhite");
            var blackandwhite = grayThumbs.Select(ImageProcessing.Threashold).ToList();
            Console.WriteLine("transformations");
            var transformations = blackandwhite.Select(ImageProcessing.CreateAllTransformations).ToList();


            var sourceCollection = new List<IDictionary<string, Bitmap[]>>();

            for (var i = 0; i < qwer1.Length; i++)
            {
                sourceCollection.Add(new Dictionary<string, Bitmap[]> { { qwer1[i], transformations.ElementAt(i) } });
            }

            return sourceCollection;
        }

        public static IEnumerable<Result> CreateMatchCollection(IDictionary<string, MatOfFloat> hashes)
        {
            var matchList = new ConcurrentBag<Result>();

            var tasks = new List<Task>();

            var hashArray = hashes.ToArray();
            for (var j = 0; j < hashArray.Length; j++)
            {
                for (var i = j + 1; i < hashArray.Length; i++)
                {
                    if (hashArray[j].Key == hashArray[i].Key)
                    {
                        continue;
                    }

                    var i1 = i;
                    var j1 = j;
                    var task = new Task(() =>
                    {
                        //Console.WriteLine("Calculate matchpoint for " + hashArray[i1].Key);
                        if (i1 % (hashArray.Length / 10 + 1) == 0)
                        {
                            Console.WriteLine("Calculate matchpoint for " + i1 + " and " + j1 + " of " + hashArray.Length);
                        }
                        var linearFactors = CalcLinearFactors(hashArray, j1, i1);
                        matchList.Add(new Result(hashArray[j1].Key, hashArray[i1].Key, linearFactors.Item1));
                    });

                    tasks.Add(task);

                    task.Start();
                }
            }

            Task.WaitAll(tasks.ToArray());
            return matchList.OrderBy(o => Abs(o.Match));
        }

        private static Tuple<double, double> CalcLinearFactors(KeyValuePair<string, MatOfFloat>[] hashArray, int j, int i)
        {
            //Console.WriteLine("Calculate matchpoint for " + hashArray[j].Key + " and " + hashArray[i].Key);

            var bfMatches = new BFMatcher(NormTypes.L2, crossCheck: true).Match(hashArray[j].Value, hashArray[i].Value).OrderBy(o => o.Distance).Select(o => o.Distance);
            var matches = bfMatches.Take(bfMatches.Count() / 2).ToArray();

            var xes = new List<double>();
            var yes = new List<double>();

            for (int k = 0; k < matches.Length; k++)
            {
                xes.Add(k);
                yes.Add(matches[k]);
            }
            var linearFactors = MathNet.Numerics.Fit.Line(xes.ToArray(), yes.ToArray());
            //Console.WriteLine("\t\t x * " + linearFactors.Item2 + " + " + linearFactors.Item1);
            return linearFactors;
        }

        private static IEnumerable<KeyValuePair<string, Bitmap>> CreateFlatBitmapCollection(IEnumerable<IDictionary<string, Bitmap[]>> list)
        {
            var flatDict = new List<KeyValuePair<string, Bitmap>>();
            foreach (var dict in list)
            {
                foreach (var kv in dict)
                {
                    foreach (var bitmap in kv.Value)
                    {
                        flatDict.Add(new KeyValuePair<string, Bitmap>(kv.Key, bitmap));
                    }
                }
            }
            return flatDict;
        }

        private static IEnumerable<Result> GetMatches(IEnumerable<KeyValuePair<string, Bitmap>> flatDict)
        {
            var results = new List<Result>();
            var pairs = flatDict as IList<KeyValuePair<string, Bitmap>> ?? flatDict.ToList();
            for (var i = 0; i < pairs.Count; i++)
            {
                Console.WriteLine("Comparing" + pairs[i].Key);
                for (var j = i + 1; j < pairs.Count; j++)
                {
                    if (pairs[i].Key == pairs[j].Key)
                    {
                        continue;
                    }
                    var match = ImageProcessing.Compare(pairs[i].Value, pairs[j].Value);
                    var result = new Result(pairs[i].Key, pairs[j].Key, match);

                    if (!results.Contains(result))
                    {
                        results.Add(result);
                    }
                    else
                    {
                        var storedItem = results.Find(r => r.Equals(result));
                        if (storedItem.Match < match)
                        {
                            storedItem.Match = match;
                        }
                    }
                }
            }
            return results;
        }


        public class Result : IEquatable<Result>
        {
            public string Name1;
            public string Name2;
            public double Match;

            public Result(string name1, string name2, double match)
            {
                Name1 = name1;
                Name2 = name2;
                Match = match;
            }


            public bool Equals(Result other)
            {
                if (other == null) return false;

                var otherName1 = other.Name1;
                var otherName2 = other.Name2;
                var equal = (Name1 == otherName1 && Name2 == otherName2) || (Name1 == otherName2 && Name2 == otherName1);
                return equal;
            }

            public override string ToString()
            {
                return "\n====================\n" + Name1 + "\n" + Name2 + "\nhas best homogenized feature offset\n" + Match + "\n====================\n";
            }
        }

        static string[] DirSearch(string sDir, params string[] types)
        {
            var list = new List<string>();
            try
            {
                foreach (string f in Directory.GetFiles(sDir))
                {
                    var type = Path.GetExtension(f);
                    if (types.Any(o => o.Equals(type)))
                    {
                        list.Add(f);
                    }
                }
                foreach (string d in Directory.GetDirectories(sDir))
                {
                    list.AddRange(DirSearch(d, types));
                }
            }
            catch (Exception excpt)
            {
                Console.WriteLine(excpt.Message);
            }
            return list.ToArray();
        }
    }
}
