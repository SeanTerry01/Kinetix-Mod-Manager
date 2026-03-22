using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.Wave;
using NAudio.Vorbis;
using System.Linq;

namespace StardewAccessibleManager
{
    public class SplashScreen : Form
    {
        private WaveOutEvent? _outputDevice;
        private VorbisWaveReader? _audioFile;
        private bool _isClosing = false;
        private AppSettings _settings;

        public SplashScreen()
        {
            _settings = AppSettings.Load();
            this.Text = "Stardew Valley Accessible Mod Manager";
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new Size(600, 400);
            this.BackColor = Color.Black;
            this.KeyPreview = true;

            Label lbl = new Label
            {
                Text = "Starting Manager...\nPress Enter to Skip",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            this.Controls.Add(lbl);

            this.Load += async (s, e) => await PlayLogo();
            this.KeyDown += async (s, e) => {
                if (e.KeyCode == Keys.Enter) await CloseWithFade();
            };
        }

        private async Task PlayLogo()
        {
            try
            {
                string theme = _settings.CurrentTheme ?? "Default";
                string soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sounds", theme, "logo");
                
                if (!Directory.Exists(soundPath) || Directory.GetFiles(soundPath, "*.ogg").Length == 0)
                {
                    soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sounds", "Default", "logo");
                }

                if (Directory.Exists(soundPath))
                {
                    string[] files = Directory.GetFiles(soundPath, "*.ogg");
                    if (files.Length > 0)
                    {
                        string fileToPlay = files[0];

                        if (_settings.RandomLogoStartup)
                        {
                            var rnd = new Random();
                            fileToPlay = files[rnd.Next(files.Length)];
                        }
                        else if (!string.IsNullOrEmpty(_settings.SelectedLogoFile))
                        {
                            // Try to find the specific file selected in settings
                            string target = Path.Combine(soundPath, _settings.SelectedLogoFile);
                            if (File.Exists(target)) fileToPlay = target;
                        }

                        _audioFile = new VorbisWaveReader(fileToPlay);
                        _outputDevice = new WaveOutEvent();
                        _outputDevice.Volume = _settings.SoundVolume / 100f;
                        _outputDevice.Init(_audioFile);
                        _outputDevice.Play();

                        while (_outputDevice.PlaybackState == PlaybackState.Playing && !_isClosing)
                        {
                            await Task.Delay(100);
                        }

                        if (!_isClosing) this.Invoke(new Action(() => this.Close()));
                    }
                    else this.Close();
                }
                else this.Close();
            }
            catch { this.Close(); }
        }

        private async Task CloseWithFade()
        {
            if (_isClosing) return;
            _isClosing = true;

            if (_outputDevice != null)
            {
                float startVol = _outputDevice.Volume;
                for (float v = startVol; v > 0; v -= 0.1f)
                {
                    _outputDevice.Volume = Math.Max(0, v);
                    await Task.Delay(50);
                }
                _outputDevice.Stop();
            }
            this.Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _outputDevice?.Dispose();
                _audioFile?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}