using Openize.Heic.Decoder;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HEICTOJPG
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            RunFile(".HEIC");
            RunFile(".HEIF");


            System.Windows.Forms.MessageBox.Show("Íê³É");
        }


        private void RunFile(string fileExtension)
        {
            var path = textBox1.Text;
            var files = Directory.GetFiles(path, $"*{fileExtension}");
            foreach (var file in files)
            {
                using (var fs = new FileStream(file, FileMode.Open))
                {
                    var newFileName = file.Replace(fileExtension, ".JPG",StringComparison.OrdinalIgnoreCase);
                    if (File.Exists(newFileName))
                    {
                        continue;
                    }
                    HeicImage image = HeicImage.Load(fs);

                    var pixels = image.GetByteArray(Openize.Heic.Decoder.PixelFormat.Bgra32);
                    var width = (int)image.Width;
                    var height = (int)image.Height;

                    var wbitmap = new System.Windows.Media.Imaging.WriteableBitmap(width, height, 72, 72, PixelFormats.Bgra32, null);
                    var rect = new Int32Rect(0, 0, width, height);
                    wbitmap.WritePixels(rect, pixels, 4 * width, 0);
                    using FileStream saveStream = new FileStream(newFileName, FileMode.OpenOrCreate);
                    JpegBitmapEncoder encoder = new JpegBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(wbitmap));
                    encoder.Save(saveStream);
                }
            }
        }
    }
}
