using Openize.Heic.Decoder;

namespace HEIC_To_JPG
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");
            var path = "D:\\hsl";

            var files = Directory.GetFiles(path,"*.jpg");
            foreach (var file in files)
            {
                using (var fs = new FileStream("filename.heic", FileMode.Open))
                {
                    HeicImage image = HeicImage.Load(fs);

                    var pixels = image.GetByteArray(Openize.Heic.Decoder.PixelFormat.Bgra32);
                    var width = (int)image.Width;
                    var height = (int)image.Height;

                    var wbitmap = new System.Windows.Media.Imaging.WriteableBitmap(width, height, 72, 72, PixelFormats.Bgra32, null);
                    var rect = new Int32Rect(0, 0, width, height);
                    wbitmap.WritePixels(rect, pixels, 4 * width, 0);

                    using FileStream saveStream = new FileStream("output.jpg", FileMode.OpenOrCreate);
                    JpegBitmapEncoder encoder = new JpegBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(wbitmap));
                    encoder.Save(saveStream);
                }
            }

            Console.ReadLine("done..");
        }
    }
}
