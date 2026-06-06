using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.Vorbis;
using NAudio.Wave;

namespace KinetixModManager;

public class SplashScreen : Form
{
	private WaveOutEvent? _outputDevice;

	private VorbisWaveReader? _audioFile;

	private bool _isClosing;

	private AppSettings _settings;

	public SplashScreen()
	{
		_settings = AppSettings.Load();
		Text = "Kinetix Mod Manager";
		base.FormBorderStyle = FormBorderStyle.None;
		base.StartPosition = FormStartPosition.CenterScreen;
		base.Size = new Size(600, 400);
		BackColor = Color.Black;
		base.KeyPreview = true;
		Label value = new Label
		{
			Text = "Starting Manager...\nPress Enter to Skip",
			ForeColor = Color.White,
			Font = new Font("Segoe UI", 18f, FontStyle.Bold),
			TextAlign = ContentAlignment.MiddleCenter,
			Dock = DockStyle.Fill
		};
		base.Controls.Add(value);
		base.Load += async delegate
		{
			await PlayLogo();
		};
		base.KeyDown += async delegate(object? s, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Return)
			{
				await CloseWithFade();
			}
		};
	}

	private async Task PlayLogo()
	{
		try
		{
			// The splash runs before Form1, so mirror the in-app rule here: follow the active
			// game's theme unless the user has opted into manual theme selection.
			string path = _settings.AllowManualTheme
				? (_settings.CurrentTheme ?? "Default")
				: AppSettings.ThemeForGame(_settings.ActiveGame);
			string text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sounds", path, "logo");
			if (!Directory.Exists(text) || Directory.GetFiles(text, "*.ogg").Length == 0)
			{
				text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sounds", "Default", "logo");
			}
			if (Directory.Exists(text))
			{
				string[] files = Directory.GetFiles(text, "*.ogg");
				if (files.Length != 0)
				{
					string fileName = files[0];
					if (_settings.RandomLogoStartup)
					{
						Random random = new Random();
						fileName = files[random.Next(files.Length)];
					}
					else if (!string.IsNullOrEmpty(_settings.SelectedLogoFile))
					{
						string text2 = Path.Combine(text, _settings.SelectedLogoFile);
						if (File.Exists(text2))
						{
							fileName = text2;
						}
					}
					_audioFile = new VorbisWaveReader(fileName);
					_outputDevice = new WaveOutEvent();
					_outputDevice.Volume = (float)_settings.SoundVolume / 100f;
					_outputDevice.Init(_audioFile);
					_outputDevice.Play();
					while (_outputDevice.PlaybackState == PlaybackState.Playing && !_isClosing)
					{
						await Task.Delay(100);
					}
					if (!_isClosing)
					{
						Invoke(delegate
						{
							Close();
						});
					}
				}
				else
				{
					Close();
				}
			}
			else
			{
				Close();
			}
		}
		catch
		{
			Close();
		}
	}

	private async Task CloseWithFade()
	{
		if (_isClosing)
		{
			return;
		}
		_isClosing = true;
		if (_outputDevice != null)
		{
			float volume = _outputDevice.Volume;
			for (float v = volume; v > 0f; v -= 0.1f)
			{
				_outputDevice.Volume = Math.Max(0f, v);
				await Task.Delay(50);
			}
			_outputDevice.Stop();
		}
		Close();
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
