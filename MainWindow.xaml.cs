using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading; // for Dispatcher
using Microsoft.Win32;         // WPF's OpenFileDialog
using WinForms = System.Windows.Forms;  // Alias for FolderBrowserDialog, etc;

namespace DVConverter
{
    public partial class MainWindow : Window
    {
        private Process _ffmpegProcess;
        private bool _isConverting;
        private TimeSpan _estimatedTotalDuration = TimeSpan.Zero;

        public MainWindow()
        {
            InitializeComponent();
        }

        // ---------- DRAG & DROP HANDLERS ----------
        private void bdDropZone_DragEnter(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                bdDropZone.Background = new SolidColorBrush(System.Windows.Media.Colors.LightSteelBlue);
            }
        }

        private void bdDropZone_DragLeave(object sender, System.Windows.DragEventArgs e)
        {
            // revert the color
            bdDropZone.Background = new SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#BDBDBD"));
        }

        private void bdDropZone_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    string ext = Path.GetExtension(files[0]).ToLower();
                    e.Effects = (ext == ".mp4" || ext == ".mkv" || ext == ".ts")
                        ? System.Windows.DragDropEffects.Copy
                        : System.Windows.DragDropEffects.None;
                }
            }
            e.Handled = true;
        }

        private void bdDropZone_Drop(object sender, System.Windows.DragEventArgs e)
        {
            // revert color
            bdDropZone.Background = new SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#BDBDBD"));

            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                var files = (string[]) e.Data.GetData(System.Windows.DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    txtInput.Text = files[0];
                    // also populate the output dir with the same folder
                    txtOutputDir.Text = Path.GetDirectoryName(files[0]);
                }
            }
        }

        // ---------- BROWSE INPUT ----------
        private void btnBrowseInput_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Video Files|*.mp4;*.mkv;*.ts|All Files|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                txtInput.Text = ofd.FileName;
            }
        }

        // ---------- BROWSE OUTPUT FOLDER ----------
        private void btnBrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new WinForms.FolderBrowserDialog();
            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
            {
                txtOutputDir.Text = dlg.SelectedPath;
            }
        }

        // ---------- The core logic ----------
        private void onBtnClick(object sender, RoutedEventArgs e)
        {
            if (_isConverting)
            {
                CancelConversion();
            }
            else
            {
                TxtDropZone.Text = "Conversion started.. Please wait";
                Convert();
            }
        }

        private void Convert()
        {
            string inputPath = txtInput.Text;
            if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
            {
                System.Windows.MessageBox.Show("Please choose a valid input file.");
                return;
            }

            // 1) Which color space? (HDR10 / SDR / HLG)
            string colorSpace = "hdr10"; // default
            if (rbSDR.IsChecked == true) colorSpace = "sdr";
            if (rbHLG.IsChecked == true) colorSpace = "hlg";
            if (rbNone.IsChecked == true) colorSpace = "none";

            // 2) Which codec? (h264 / h265)
            // We'll pick the correct ffmpeg encoder name
            string videoCodec = "libx265";
            if (rbH264.IsChecked == true)
            {
                videoCodec = "libx264";
            }

            // 3) Parse the user-chosen average bitrate
            int bitrate = 15000;
            if (!string.IsNullOrWhiteSpace(txtBitrate.Text))
            {
                int.TryParse(txtBitrate.Text, out bitrate);
                if (bitrate < 1000) bitrate = 15000; // fallback
            }

            // Output directory optional
            string outputDir = txtOutputDir.Text;
            if (string.IsNullOrWhiteSpace(outputDir) || !Directory.Exists(outputDir))
            {
                // default = same folder as input
                outputDir = Path.GetDirectoryName(inputPath);
            }

            // Build output file name
            string ext = Path.GetExtension(inputPath);
            string baseName = Path.GetFileNameWithoutExtension(inputPath);
            string outputPath = Path.Combine(outputDir, $"{baseName}_{colorSpace}_{videoCodec}_{bitrate}k{ext}");

            // Get total duration for progress
            _estimatedTotalDuration = GetVideoDuration(inputPath);

            // FFmpeg path
            string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "ffmpeg.exe");

            // Build the color filter & x265 params
            string videoFilter = GetVideoFilter(colorSpace);

            // If user chose x264, we skip x265-specific params
            // or you can add your own x264 param logic.
            string x265Params = "";
            if (videoCodec == "libx265")
            {
                x265Params = GetX265Params(colorSpace);
            }

            // Final FFmpeg arguments
            // We'll share a common structure, but adapt for h264 or h265
            // For h264, we might skip the x265-specific param
            // For h265, we skip x264 params, obviously.
            string arguments = 
                "-nostdin " +
                "-loglevel error " +
                "-stats " +
                "-y " +
                "-init_hw_device vulkan=vulkan " +
                "-filter_hw_device vulkan " +
                $"-i \"{inputPath}\" " +
                $"-vf \"{videoFilter}\" " +
                $"-c:v {videoCodec} " + // This can be libx264 or libx265
                "-c:a copy " +
                "-c:s copy " +
                $"-b:v {bitrate}k ";

            // If we do x265, add -x265-params
            if (videoCodec == "libx265" && !string.IsNullOrEmpty(x265Params))
            {
                arguments += $"-x265-params \"{x265Params}\" ";
            }

            arguments += $"\"{outputPath}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            _ffmpegProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _ffmpegProcess.ErrorDataReceived += OnDataReceived;
            _ffmpegProcess.OutputDataReceived += OnDataReceived;
            _ffmpegProcess.Exited += OnProcessExited;

            try
            {
                _ffmpegProcess.Start();
                _ffmpegProcess.BeginErrorReadLine();
                _ffmpegProcess.BeginOutputReadLine();
                BtnStart.Content = "Cancel";
                _isConverting = true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error starting ffmpeg:\n" + ex.Message);
            }
        }
        
         // ---------- FILTERS & PARAMS ----------
         private string GetVideoFilter(string encodingType)
         {
             switch (encodingType.ToLower())
             {
                 case "hdr10":
                     return "hwupload,libplacebo=peak_detect=false:colorspace=9:color_primaries=9:color_trc=16:range=tv:format=yuv420p10le,hwdownload,format=yuv420p10le";
                 case "sdr":
                     return "hwupload,libplacebo=peak_detect=false:colorspace=bt709:color_primaries=bt709:color_trc=bt709:range=tv:format=yuv420p10le,hwdownload,format=yuv420p10le";
                 case "hlg":
                     return "hwupload,libplacebo=peak_detect=false:colorspace=9:color_primaries=9:color_trc=14:range=tv:format=yuv420p10le,hwdownload,format=yuv420p10le";
                 case "none":
                     return "hwupload,libplacebo=colorspace=bt709:color_primaries=bt709:color_trc=bt709:range=tv:format=yuv420p,hwdownload,format=yuv420p";
                 default:
                     throw new ArgumentException("Invalid encoding type (hdr10, sdr, hlg, hdr10plus).");
             }
         }


        private string GetX265Params(string encodingType)
        {
            switch (encodingType.ToLower())
            {
                case "hdr10":
                    return
                        "repeat-headers=1:sar=1:hrd=1:aud=1:open-gop=0:hdr10=1:sao=0:rect=0:cutree=0:deblock=-3-3:strong-intra-smoothing=0:chromaloc=2:aq-mode=1:vbv-maxrate=160000:vbv-bufsize=160000:max-luma=1023:max-cll=0,0:master-display=G(8500,39850)B(6550,23000)R(35400,15650)WP(15635,16450)L(10000000,1):preset=slow";
                case "sdr":
                    return "deblock=-3-3:vbv-bufsize=62500:vbv-maxrate=50000:fast-pskip=0:dct-decimate=0:level=5.1:ref=5:psy-rd=1.05,0.15:subme=7:me=umh:me_range=48:preset=slow";
                case "hlg":
                    return "open-gop=0:atc-sei=18:pic_struct=0:preset=slow";
                case "none":
                    return "preset=medium";
                default:
                    return "";
            }
        }
        
        //buy me a coffee function BuyCoffee_Click
        private void BuyCoffee_Click(object sender, RoutedEventArgs e)
        {
            string link = "https://paypal.me/SlavomirDurej?country.x=GB&locale.x=en_GB";
    
            try
            {
                // For .NET Core and .NET 5+
                Process.Start(new ProcessStartInfo
                {
                    FileName = link,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                // Optional: Handle exceptions (e.g., log the error or notify the user)
                System.Windows.Forms.MessageBox.Show("Unable to open the link. Please try again later.", "Error", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
            }
        }

        private void CancelConversion()
        {
            if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
            {
                _ffmpegProcess.Kill();
                BtnStart.Content = "Start Conversion";
                _isConverting = false;
                progressBar.Value = 0;
            }
        }

        // ---------- HANDLING OUTPUT ----------
        private void OnDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data)) return;

            Dispatcher.Invoke(() =>
            {
                // parse e.Data for time=...
                if (TryParseTime(e.Data, out TimeSpan currentPos) && _estimatedTotalDuration.TotalSeconds > 0)
                {
                    double progress = (currentPos.TotalSeconds / _estimatedTotalDuration.TotalSeconds) * 100.0;
                    progressBar.Value = progress;

                    double remainSec = _estimatedTotalDuration.TotalSeconds - currentPos.TotalSeconds;
                    TxtDropZone.Text = $"Converting... {progress:0.0}%  " +
                                       $"ETA: {TimeSpan.FromSeconds(remainSec):hh\\:mm\\:ss}";
                }
            });
            
            //log e.Data here to a console
            Console.WriteLine(e.Data);
        }

        private void OnProcessExited(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                _isConverting = false;
                progressBar.Value = 100;
                TxtDropZone.Text = "Done!";
                BtnStart.Content = "Start Conversion";
            });
        }

        // ---------- TIME PARSING ----------
        private bool TryParseTime(string ffmpegLine, out TimeSpan result)
        {
            result = TimeSpan.Zero;
            var match = Regex.Match(ffmpegLine, @"time=(\d+:\d+:\d+\.\d+)");
            if (match.Success)
            {
                if (TimeSpan.TryParse(match.Groups[1].Value, out TimeSpan t))
                {
                    result = t;
                    return true;
                }
            }
            return false;
        }

        // ---------- DURATION ----------
        private TimeSpan GetVideoDuration(string inputFile)
        {
            TimeSpan duration = TimeSpan.Zero;
            string ffprobePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Tools", "ffprobe.exe"
            );

            // format=duration gives container-level duration
            var args = $"-v error -show_entries format=duration " +
                       $"-of default=noprint_wrappers=1:nokey=1 \"{inputFile}\"";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(psi))
                {
                    string output = proc?.StandardOutput.ReadToEnd();
                    proc?.WaitForExit();

                    if (double.TryParse(
                            output,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out double seconds))
                    {
                        duration = TimeSpan.FromSeconds(seconds);
                    }
                }
            }
            catch
            {
                // If ffprobe fails, no duration. We'll skip progress %.
            }

            return duration;
        }

       
    }
}
