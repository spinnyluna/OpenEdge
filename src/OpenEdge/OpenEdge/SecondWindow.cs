using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfAnimatedGif;

namespace OpenEdge;

public partial class SecondWindow : Grid, IComponentConnector
{
	private int rows = 12;

	private int collumns = 6;

	public CustomUiElement[] medias = new CustomUiElement[1];

	public int previousMedialength = -1;

	private Random random = new Random();

	public int mediasCounter;

	public List<string> imagePortraitPaths = new List<string>();

	public List<string> imageLandscapePaths = new List<string>();

	public List<string> imageCaptionPaths = new List<string>();

	private BitmapImage[] nextBitImageLandscapes = new BitmapImage[10];

	private BitmapImage[] nextBitGif = new BitmapImage[2];

	private int landscapePoint;

	private int portraitPoint;

	private int captionPoint;

	private BitmapImage[] nextBitImagePortraits = new BitmapImage[14];

	private int gifPoint = -1;

	public ImageTagger imageTagger;

	private List<TextBlock> blocks = new List<TextBlock>();

	public List<string> censorText = new List<string>();

	public float censorIntensity;

	public int censorMode;

	public FrameWindow eventHandeler;

	private int currentMediaScreen = 1;

	public List<string> videoPaths = new List<string>();

	private string currentVideoRelativePath = "";

	private int consecutiveVideoFailures;

	public List<string> gifPaths = new List<string>();

	private MainWindow mw;

	public List<string> tagPaths = new List<string>();

	private int displayedVideosAmount;

	public bool loaded = true;

	public List<string> chromaText = new List<string>();

	private string chromaString = "";

	private string tauntString = "";

	private bool shouldBeMuted;

	private bool hypnosisOn;

	private bool imgLocked;

	private bool videoLoops;

	private bool fromBeginning;

	private bool showingCaption;

	public double videoVolumeValue = 0.5;

	private Brush foreground;

	private Brush background;

	private MediaCatalogService mediaCatalog;

	public SecondWindow(MediaCatalogService mediaCatalog)
	{
		this.mediaCatalog = mediaCatalog;
		InitializeComponent();
		foreground = new SolidColorBrush(Color.FromArgb(byte.MaxValue, 240, 105, 225));
		foreground.Freeze();
		background = new SolidColorBrush(Color.FromArgb(byte.MaxValue, 40, 5, 50));
		background.Freeze();
	}

	public SecondWindow getThis()
	{
		return this;
	}

	public string GetPerformanceSnapshot()
	{
		try
		{
			if (!Dispatcher.CheckAccess())
			{
				return Dispatcher.Invoke(GetPerformanceSnapshot, DispatcherPriority.Background);
			}
			return "mediaScreen=" + currentMediaScreen
				+ " medias=" + (medias?.Length ?? 0)
				+ " mediasCounter=" + mediasCounter
				+ " imageGridChildren=" + imageGrid.Children.Count
				+ " censorGridChildren=" + censorGrid.Children.Count
				+ " textBlocks=" + blocks.Count
				+ " videoVisible=" + videoWindow.Visibility
				+ " videoSource=" + (videoWindow.Source == null ? "none" : System.IO.Path.GetFileName(videoWindow.Source.LocalPath));
		}
		catch (Exception ex)
		{
			return "secondWindowSnapshotError=" + ex.GetType().Name;
		}
	}

	public async void changeBg(string bgName, bool hypnosisOn)
	{
		this.hypnosisOn = hypnosisOn;
		string bgPath = RuntimePaths.Resource(bgName + ".png");
		try
		{
			BitmapImage bitmap = new BitmapImage();
			bitmap.BeginInit();
			bitmap.CacheOption = BitmapCacheOption.OnLoad;
			bitmap.UriSource = new Uri(bgPath);
			bitmap.EndInit();
			bitmap.Freeze();
			await Task.Delay(100);
			await base.Dispatcher.InvokeAsync(delegate
			{
				heartbg.Source = bitmap;
			});
		}
		catch (Exception ex)
		{
			SessionTraceLogger.Error("background-decode", "Failed to load background: " + bgPath, ex);
		}
	}

	public void loadSecondWindowForUse()
	{
		Task.Run(delegate
		{
			loadImages();
		});
		Task.Run((Action)censorScreen);
		Task.Run((Action)censorWords);
		Task.Run((Action)chromaStringBuilder);
	}

	public void setMw(MainWindow mw)
	{
		this.mw = mw;
	}

	public void setImgLocked(bool isLocked, string path = "")
	{
		if (isLocked)
		{
			if (Enumerable.Contains(mw.displayOptions, 4))
			{
				mediaScreen(4);
			}
			else
			{
				mediaScreen(5);
			}
			mw.censorCheck(forceCensor: false, 0);
			mediaScreen(0, path);
			imgLocked = true;
		}
		else
		{
			imgLocked = false;
			mw.setNewMediaFormat();
		}
	}

	public void showCaptionImage()
	{
		if (imageCaptionPaths.Count > 0)
		{
			showingCaption = true;
			setImgLocked(isLocked: true, imageCaptionPaths[captionPoint % imageCaptionPaths.Count]);
			captionPoint++;
		}
	}

	public void hideCaptionImage()
	{
		if (showingCaption)
		{
			showingCaption = false;
			setImgLocked(isLocked: false);
		}
	}

	public void setVideoLocked(bool isLocked)
	{
		if (isLocked)
		{
			mw.censorCheck(forceCensor: false, 0);
			if (Enumerable.Contains(mw.displayOptions, 8))
			{
				mediaScreen(8);
			}
			mediaScreen();
			removeTextOverlay();
			imgLocked = true;
		}
		else
		{
			imgLocked = false;
			mw.setNewMediaFormat();
		}
	}

	public string removeCurrentImage()
	{
		string source = "";
		base.Dispatcher.Invoke(delegate
		{
			try
			{
				source = medias.Last().location;
			}
			catch
			{
			}
		});
		source = source.Replace("file:///", "");
		if (File.Exists(source))
		{
			File.Delete(source);
		}
		mw.reloadImagesVideos();
		imgLocked = false;
		return source;
	}

	public string removeCurrentVideo()
	{
		string source = "";
		base.Dispatcher.Invoke(delegate
		{
			try
			{
				source = videoWindow.Source.AbsolutePath;
			}
			catch
			{
			}
		});
		source = source.Replace("file:///", "");
		if (File.Exists(source))
		{
			File.Delete(source);
		}
		mw.reloadImagesVideos();
		imgLocked = false;
		return source;
	}

	private void loadImages(int i = -1)
	{
		i++;
		if (imageLandscapePaths.Count >= 6 && currentMediaScreen < 8 && i % 2 == 0)
		{
			nextBitImageLandscapes[i / 2 % nextBitImageLandscapes.Length] = loading(imageLandscapePaths[i / 2 % imageLandscapePaths.Count], vertical: false);
		}
		if (imagePortraitPaths.Count >= 6 && currentMediaScreen < 8 && i % 2 == 1)
		{
			nextBitImagePortraits[i / 2 % nextBitImagePortraits.Length] = loading(imagePortraitPaths[i / 2 % imagePortraitPaths.Count]);
		}
		if (gifPaths.Count >= 4 && ((i % 20 == 0 && currentMediaScreen == 9) || nextBitGif[i / 20 % nextBitGif.Length] == null))
		{
			nextBitGif[i / 20 % nextBitGif.Length] = loading(gifPaths[i / 20 % gifPaths.Count]);
		}
		Task.Delay(80).ContinueWith(delegate
		{
			loadImages(i);
		});
	}

	private BitmapImage loading(string path, bool vertical = true)
	{
		if (path != null)
		{
			try
			{
				BitmapImage bitmapImage = new BitmapImage();
				bitmapImage.BeginInit();
				bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
				bitmapImage.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
				if (vertical)
				{
					bitmapImage.DecodePixelHeight = Math.Max(1, (int)base.ActualHeight);
				}
				else
				{
					bitmapImage.DecodePixelWidth = Math.Max(1, (int)base.ActualWidth);
				}
				bitmapImage.UriSource = new Uri(RuntimePaths.ResolveRuntimePath(path));
				bitmapImage.EndInit();
				bitmapImage.Freeze();
				return bitmapImage;
			}
			catch (Exception ex)
			{
				SessionTraceLogger.Error("session-media-decode", "Failed to load media image: " + path, ex);
				SessionTraceLogger.Memory("session-media-decode", "after media image decode failure");
			}
		}
		return null;
	}

	private TransformedBitmap Transform(BitmapImage bitmap, double gridSizeX, double gridSizeY)
	{
		int pixelHeight = bitmap.PixelHeight;
		int pixelWidth = bitmap.PixelWidth;
		double num = 1.0;
		if (gridSizeY != 0.0)
		{
			num = ((!((double)pixelHeight / gridSizeY < (double)pixelWidth / gridSizeX)) ? (gridSizeX / (double)pixelWidth) : (gridSizeY / (double)pixelHeight));
		}
		TransformedBitmap transformedBitmap = new TransformedBitmap();
		transformedBitmap.BeginInit();
		transformedBitmap.Source = bitmap;
		transformedBitmap.Transform = new ScaleTransform(num, num);
		transformedBitmap.EndInit();
		transformedBitmap.Freeze();
		return transformedBitmap;
	}

	public void useTaggedPaths(bool isTemp = false)
	{
		if (tagPaths.Count <= 0)
		{
			return;
		}
		string mediaPath = tagPaths[random.Next(tagPaths.Count)];
		if (imageTagger.MediaCatalog.IsVideoPath(mediaPath))
		{
			currentMediaScreen = 8;
			removeTextOverlay();
			base.Dispatcher.Invoke(delegate
			{
				createVideo(mediaPath);
			});
		}
		else if (imageTagger.MediaCatalog.IsImagePath(mediaPath))
		{
			currentMediaScreen = 4;
			base.Dispatcher.Invoke(delegate
			{
				createSingleTagImage(mediaPath);
				videoWindow.Source = null;
			});
			removeTextOverlay();
		}
		if (isTemp)
		{
			Task.Delay(6000).ContinueWith(delegate
			{
				tagPaths.Clear();
			});
		}
	}

	public void mediaScreen(int mode = 0, string path = "")
	{
		if (mode != 0 || !string.IsNullOrWhiteSpace(path))
		{
			SessionTraceLogger.Info("media-screen", "mode=" + mode + " path=" + path + " current=" + currentMediaScreen + " locked=" + imgLocked + " tagged=" + tagPaths.Count);
		}
		if (tagPaths.Count != 0)
		{
			return;
		}
		if (!imgLocked)
		{
			switch (mode)
			{
			case 0:
				if (currentMediaScreen != 8)
				{
					Task.Run(delegate
					{
						newMedias(path);
					});
					break;
				}
				removeTextOverlay();
				Task.Run(delegate
				{
					createVideo();
				});
				break;
			case 1:
				base.Dispatcher.Invoke(create2by12);
				break;
			case 2:
				base.Dispatcher.Invoke(createLargePortrait);
				break;
			case 3:
				base.Dispatcher.Invoke(create1by6);
				break;
			case 4:
				base.Dispatcher.Invoke(createSinglePortrait);
				break;
			case 5:
				base.Dispatcher.Invoke(createSingleLandscape);
				break;
			case 6:
				base.Dispatcher.Invoke(create2by6);
				break;
			case 7:
				base.Dispatcher.Invoke(createLargePortraitSurroundingLandscapes);
				break;
			case 8:
				removeTextOverlay();
				base.Dispatcher.Invoke(delegate
				{
					createVideo();
				});
				break;
			case 9:
				base.Dispatcher.Invoke(createGifImage);
				break;
			case 99:
				base.Dispatcher.Invoke(createRandomLocation);
				break;
			}
		}
		if (mode != 0)
		{
			currentMediaScreen = mode;
		}
		if (currentMediaScreen != 8 && currentMediaScreen != 0)
		{
			base.Dispatcher.InvokeAsync(delegate
			{
				removeTextOverlay();
				videoWindow.Source = null;
			});
		}
	}

	public int getCurrentMediaScreen()
	{
		return currentMediaScreen;
	}

	public void setCurrentMediaScreen(int newScreen)
	{
		currentMediaScreen = newScreen;
	}

	public void muteVideo(bool mute)
	{
		base.Dispatcher.Invoke(delegate
		{
			videoWindow.IsMuted = mute;
		});
		shouldBeMuted = mute;
	}

	private void createVideo(string path = "")
	{
		SessionTraceLogger.Info("media-video", "createVideo path=" + path);
		base.Dispatcher.Invoke(delegate
		{
			videoWindow.Visibility = Visibility.Hidden;
			if (path == "")
			{
				if (videoPaths.Count == 0)
				{
					SessionTraceLogger.Error("media-video", "No video paths available for createVideo");
					return;
				}
				path = videoPaths[displayedVideosAmount % videoPaths.Count];
			}
			if (!TrySetVideoWindowSource(path))
			{
				return;
			}
			List<string> list = imageTagger.activeTags(path);
			double num = 1.0;
			if (list.Contains("Always Mute"))
			{
				videoWindow.IsMuted = true;
			}
			else if (list.Contains("Always Audio"))
			{
				videoWindow.IsMuted = false;
			}
			else
			{
				videoWindow.IsMuted = shouldBeMuted;
			}
			num = (list.Contains("Increase Volume") ? 1.3 : ((!list.Contains("Reduce Volume")) ? 1.0 : 0.7));
			videoWindow.Volume = (float)(videoVolumeValue * num);
			if (list.Contains("Loop"))
			{
				videoLoops = true;
			}
			else
			{
				videoLoops = false;
			}
			if (list.Contains("From Beginning"))
			{
				fromBeginning = true;
			}
			else
			{
				fromBeginning = false;
			}
			list = new List<string>();
			displayedVideosAmount++;
			medias = new CustomUiElement[1];
			if (censorMode == 3)
			{
				mediaBlurEffect.Radius = 60f * censorIntensity;
				TextBlock element = makeTextOverlay();
				Grid.SetRow(element, 0);
				Grid.SetColumn(element, 0);
				Grid.SetRowSpan(element, 12);
				Grid.SetColumnSpan(element, 6);
				censorGrid.Children.Add(element);
			}
			else
			{
				mediaBlurEffect.Radius = 0.0;
			}
			while (imageGrid.Children.Count > 2)
			{
				imageGrid.Children.RemoveAt(2);
			}
		});
		previousMedialength = 0;
		mediasCounter = 1;
	}

	private bool TrySetVideoWindowSource(string relativePath)
	{
		currentVideoRelativePath = relativePath ?? "";
		try
		{
			string fullPath = RuntimePaths.ResolveRuntimePath(currentVideoRelativePath);
			if (!File.Exists(fullPath))
			{
				SessionTraceLogger.Error("media-video", "Video file not found relative=" + currentVideoRelativePath + " full=" + fullPath);
				return false;
			}
			SessionTraceLogger.Info("media-video", "load relative=" + currentVideoRelativePath + " full=" + fullPath);
			videoWindow.Source = new Uri(fullPath, UriKind.Absolute);
			return true;
		}
		catch (Exception ex)
		{
			SessionTraceLogger.Error("media-video", "Failed to set video source relative=" + currentVideoRelativePath, ex);
			videoWindow.Source = null;
			return false;
		}
	}

	private void removeTextOverlay()
	{
		base.Dispatcher.InvokeAsync(delegate
		{
			while (blocks.Count > 0)
			{
				censorGrid.Children.Remove(blocks[0]);
				blocks.Remove(blocks[0]);
			}
		});
	}

	private TextBlock makeTextOverlay()
	{
		TransformGroup transformGroup = new TransformGroup();
		transformGroup.Children.Add(new RotateTransform(random.Next(-20, 20)));
		TextBlock textBlock = new TextBlock
		{
			FontSize = 60 - medias.Length * 3,
			FontFamily = new FontFamily("Times New Roman"),
			Foreground = new SolidColorBrush(Color.FromRgb((byte)random.Next(200, 255), (byte)random.Next(0, 150), (byte)random.Next(200, 255))),
			Background = Brushes.Transparent,
			IsHitTestVisible = false,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			LayoutTransform = transformGroup,
			TextAlignment = TextAlignment.Center,
			Visibility = Visibility.Hidden
		};
		if (random.Next(100) > 90)
		{
			textBlock.FontWeight = FontWeights.Bold;
		}
		if (random.Next(100) > 90)
		{
			textBlock.TextDecorations = TextDecorations.Underline;
			textBlock.TextDecorations = TextDecorations.OverLine;
		}
		if (random.Next(100) > 90)
		{
			textBlock.FontStyle = FontStyles.Italic;
		}
		else if (random.Next(100) > 88)
		{
			textBlock.FontStyle = FontStyles.Oblique;
		}
		textBlock.Text = chromaString;
		blocks.Add(textBlock);
		return textBlock;
	}

	private void setTextOverlays()
	{
		Thread.Sleep(100);
		int i;
		for (i = 0; i < medias.Length; i++)
		{
			if (medias[i] != null)
			{
				base.Dispatcher.Invoke(delegate
				{
					medias[i].setText(makeTextOverlay());
				});
			}
			else
			{
				Task.Run((Action)setTextOverlays);
			}
		}
	}

	private void CreateNewMediaElement(int gridX, int gridY, int rangeX, int rangeY, int positionInArray, bool landscapeMode, bool fill = true)
	{
		base.Dispatcher.Invoke(delegate
		{
			CustomUiElement customUiElement = new CustomUiElement(new Image(), landscapeMode, new TextBlock(), foreground, background);
			if (fill)
			{
				customUiElement.getMediaElement().Stretch = Stretch.UniformToFill;
				customUiElement.getMediaCopy().Stretch = Stretch.UniformToFill;
			}
			else
			{
				customUiElement.getMediaElement().Stretch = Stretch.Uniform;
				customUiElement.setImgCopyAsBg();
			}
			Grid.SetRow(customUiElement, gridY);
			Grid.SetColumn(customUiElement, gridX);
			Grid.SetColumnSpan(customUiElement, rangeX);
			Grid.SetRowSpan(customUiElement, rangeY);
			customUiElement.getMediaElement().HorizontalAlignment = HorizontalAlignment.Center;
			customUiElement.getMediaCopy().HorizontalAlignment = HorizontalAlignment.Center;
			customUiElement.getMediaElement().VerticalAlignment = VerticalAlignment.Center;
			customUiElement.getMediaCopy().VerticalAlignment = VerticalAlignment.Center;
			if (medias.Length > positionInArray)
			{
				medias[positionInArray] = customUiElement;
				imageGrid.Children.Add(medias[positionInArray]);
			}
		}, DispatcherPriority.SystemIdle);
	}

	private void create2by12()
	{
		previousMedialength = medias.Length;
		medias = new CustomUiElement[collumns / 2];
		for (int i = 0; i < collumns / 2; i++)
		{
			CreateNewMediaElement(i * 2, 0, 2, rows, i, landscapeMode: false);
		}
		Task.Run((Action)setTextOverlays);
	}

	private void create2by6()
	{
		previousMedialength = medias.Length;
		medias = new CustomUiElement[collumns];
		int num = 0;
		for (int i = 0; i < collumns / 2; i++)
		{
			for (int j = 0; j < 2; j++)
			{
				CreateNewMediaElement(i * 2, j * (rows / 2), 2, rows / 2, num, landscapeMode: true);
				num++;
			}
		}
		Task.Run((Action)setTextOverlays);
	}

	private void create1by6()
	{
		previousMedialength = medias.Length;
		medias = new CustomUiElement[collumns * 2];
		int num = 0;
		for (int i = 0; i < collumns; i++)
		{
			for (int j = 0; j < 2; j++)
			{
				CreateNewMediaElement(i, j * (rows / 2), 1, rows / 2, num, landscapeMode: false);
				num++;
			}
		}
		Task.Run((Action)setTextOverlays);
	}

	private void createRandomLocation()
	{
		previousMedialength = medias.Length;
		medias = new CustomUiElement[20];
		for (int i = 0; i < medias.Length; i++)
		{
			int num = 1;
			int num2 = 1;
			int num3 = random.Next(1, 2);
			int gridX = random.Next(0, collumns - num);
			if (random.Next(0, 1) == 1)
			{
				num = 2;
			}
			else
			{
				num2 = 6;
			}
			num = num3 * num;
			num2 = num3 * num2;
			int gridY = random.Next(0, rows - num2);
			CreateNewMediaElement(gridX, gridY, num, num2, i, landscapeMode: false);
		}
		Task.Run((Action)setTextOverlays);
	}

	private void createLargePortrait()
	{
		previousMedialength = medias.Length;
		medias = new CustomUiElement[collumns * 2 - 3];
		int num = 0;
		for (int i = 0; i < collumns; i++)
		{
			for (int j = 0; j < 2; j++)
			{
				if (i == 2 && j == 0)
				{
					CreateNewMediaElement(i, j * (rows / 2), 2, rows, num, landscapeMode: false);
					num++;
				}
				else if (i < 2 || i > 3)
				{
					CreateNewMediaElement(i, j * (rows / 2), 1, rows / 2, num, landscapeMode: false);
					num++;
				}
			}
		}
		Task.Run((Action)setTextOverlays);
	}

	private void createLargePortraitSurroundingLandscapes()
	{
		previousMedialength = medias.Length;
		medias = new CustomUiElement[collumns - 1];
		int num = 0;
		for (int i = 0; i < collumns / 2; i++)
		{
			for (int j = 0; j < 2; j++)
			{
				if (i == 1 && j == 0)
				{
					CreateNewMediaElement(i * 2, j * (rows / 2), 2, rows, num, landscapeMode: false);
					num++;
				}
				else if (i != 1)
				{
					CreateNewMediaElement(i * 2, j * (rows / 2), 2, rows / 2, num, landscapeMode: true);
					num++;
				}
			}
		}
		Task.Run((Action)setTextOverlays);
	}

	private void createSingleLandscape()
	{
		previousMedialength = medias.Length;
		medias = new CustomUiElement[1];
		CreateNewMediaElement(0, 0, collumns, rows, 0, landscapeMode: true);
		Task.Run((Action)setTextOverlays);
	}

	private void createSinglePortrait()
	{
		previousMedialength = medias.Length;
		medias = new CustomUiElement[1];
		CreateNewMediaElement(0, 0, collumns, rows, 0, landscapeMode: false, fill: false);
		Task.Run((Action)setTextOverlays);
	}

	private void createSingleTagImage(string mediaPath)
	{
		previousMedialength = medias.Length;
		if (medias.Length != 1 || medias[0] == null)
		{
			medias = new CustomUiElement[1];
			CreateNewMediaElement(0, 0, collumns, rows, 0, landscapeMode: false, fill: false);
			Task.Run((Action)setTextOverlays);
		}
		try
		{
			medias[0].setMediaElement(loading(mediaPath), hypnosisOn, mediaPath);
		}
		catch
		{
			medias = new CustomUiElement[1];
			CreateNewMediaElement(0, 0, collumns, rows, 0, landscapeMode: false, fill: false);
			medias[0].setMediaElement(loading(mediaPath), hypnosisOn, mediaPath);
		}
	}

	private void createGifImage()
	{
		previousMedialength = medias.Length;
		medias = new CustomUiElement[1];
		CreateNewMediaElement(0, 0, collumns, rows, 0, landscapeMode: false, fill: false);
		newMedias();
		Task.Run((Action)setTextOverlays);
	}

	private void newMedias(string path = "")
	{
		base.Dispatcher.InvokeAsync(delegate
		{
			videoWindow.Visibility = Visibility.Collapsed;
			videoWindow.Source = null;
		});
		CustomUiElement media = null;
		if ((mediasCounter % 2 == 0 || medias.Length == 1) && medias.Length != 0)
		{
			media = medias[mediasCounter % medias.Length / 2];
		}
		else if (medias.Length != 0)
		{
			media = medias[medias.Length - 1 - mediasCounter % medias.Length / 2];
		}
		if (media == null)
		{
			return;
		}
		if (path != "")
		{
			media.setMediaElement(loading(path), hypnosisOn, path);
			mediasCounter++;
		}
		else if (currentMediaScreen == 9)
		{
			gifPoint++;
			if (nextBitGif[gifPoint % nextBitGif.Length] == null)
			{
				Task.Delay(100).ContinueWith(delegate
				{
					newMedias();
				});
				return;
			}
			base.Dispatcher.InvokeAsync(delegate
			{
				ImageBehavior.SetRepeatBehavior(media.getMediaElement(), RepeatBehavior.Forever);
				ImageBehavior.SetAnimatedSource(media.getMediaElement(), nextBitGif[gifPoint % nextBitGif.Length]);
			}, DispatcherPriority.SystemIdle);
		}
		else if (media.getLandscapeMode())
		{
			BitmapImage nextImg = nextBitImageLandscapes[landscapePoint % nextBitImageLandscapes.Length];
			if (nextImg != null && nextImg.UriSource != null)
			{
				base.Dispatcher.Invoke(delegate
				{
					media.setMediaElement(nextImg, hypnosisOn, nextImg.UriSource.ToString());
				});
				landscapePoint++;
				mediasCounter++;
			}
			else
			{
				bool flag = false;
				int num = 0;
				while (!flag && num < nextBitImagePortraits.Length)
				{
					BitmapImage bit = nextBitImageLandscapes[random.Next(0, nextBitImageLandscapes.Length)];
					if (bit != null)
					{
						base.Dispatcher.Invoke(delegate
						{
							media.setMediaElement(bit, hypnosisOn, bit.UriSource.ToString());
						});
						mediasCounter++;
						flag = true;
						Thread.Sleep(10);
					}
					num++;
				}
			}
		}
		else
		{
			BitmapImage nextImg2 = nextBitImagePortraits[portraitPoint % nextBitImagePortraits.Length];
			if (nextImg2 != null && nextImg2.UriSource != null)
			{
				base.Dispatcher.Invoke(delegate
				{
					media.setMediaElement(nextImg2, hypnosisOn, nextImg2.UriSource.ToString());
				});
				portraitPoint++;
				mediasCounter++;
			}
			else
			{
				bool flag2 = false;
				int num2 = 0;
				while (!flag2 && num2 < nextBitImagePortraits.Length)
				{
					BitmapImage bit2 = nextBitImagePortraits[random.Next(0, nextBitImagePortraits.Length)];
					if (bit2 != null)
					{
						base.Dispatcher.Invoke(delegate
						{
							media.setMediaElement(bit2, hypnosisOn, bit2.UriSource.ToString());
						});
						mediasCounter++;
						flag2 = true;
						Thread.Sleep(10);
					}
					num2++;
				}
			}
		}
		base.Dispatcher.Invoke(delegate
		{
			media.blurMediaElement(this);
		});
		base.Dispatcher.Invoke(delegate
		{
			if (mediasCounter >= medias.Length + 2)
			{
				while (imageGrid.Children.Count > 2 + medias.Length)
				{
					imageGrid.Children.RemoveAt(2);
				}
			}
			else if (imageGrid.Children.Count > medias.Length + previousMedialength + 3)
			{
				imageGrid.Children.RemoveAt(2);
			}
		});
	}

	public void censorWords()
	{
		if (censorMode == 1)
		{
			base.Dispatcher.InvokeAsync(delegate
			{
				TransformGroup transformGroup = new TransformGroup
				{
					Children = { (Transform)new RotateTransform(random.Next(-20, 20)) }
				};
				string singleCensorText = mw.getSingleCensorText();
				transformGroup.Children.Add(new TranslateTransform(random.Next(-550, 550), random.Next(-400, 400)));
				double num2 = random.NextDouble() + ((double)(censorIntensity / 2f) + 0.1);
				transformGroup.Children.Add(new ScaleTransform(num2, num2));
				Label label = new Label
				{
					HorizontalAlignment = HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center,
					FontFamily = new FontFamily("Times New Roman"),
					Opacity = (random.NextDouble() + 0.4 + (double)censorIntensity) / 2.0,
					Content = singleCensorText,
					FontSize = 140.0,
					RenderTransform = transformGroup,
					RenderTransformOrigin = new Point(0.5, 0.5),
					Foreground = new SolidColorBrush(Color.FromRgb((byte)random.Next(200, 255), (byte)random.Next(0, 100), (byte)random.Next(200, 255))),
					Background = new SolidColorBrush(Color.FromRgb(byte.MaxValue, byte.MaxValue, byte.MaxValue)),
					IsHitTestVisible = false,
					Margin = new Thickness(-1000.0)
				};
				Panel.SetZIndex(label, 100);
				Grid.SetColumn(label, 0);
				Grid.SetRow(label, 0);
				Grid.SetColumnSpan(label, 6);
				Grid.SetRowSpan(label, 12);
				TrimCensorGridOverlays(24);
				censorGrid.Children.Add(label);
				Task.Delay(random.Next(7000, 12000)).ContinueWith((Task t) => base.Dispatcher.InvokeAsync(delegate
				{
					censorGrid.Children.Remove(label);
				}));
			}, DispatcherPriority.SystemIdle);
		}
		int num = 2200 - (int)(censorIntensity * 1000f);
		if (num < 600)
		{
			num = 600;
		}
		Task.Delay(num).ContinueWith(delegate
		{
			censorWords();
		});
	}

	public void censorScreen()
	{
		if (censorMode == 2)
		{
			makeTauntString();
			base.Dispatcher.InvokeAsync(delegate
			{
				TextBlock textBlock = new TextBlock();
				textBlock.HorizontalAlignment = HorizontalAlignment.Center;
				textBlock.VerticalAlignment = VerticalAlignment.Center;
				Grid.SetColumn(textBlock, 0);
				Grid.SetRow(textBlock, 0);
				Grid.SetColumnSpan(textBlock, 6);
				Grid.SetRowSpan(textBlock, 12);
				Panel.SetZIndex(textBlock, 100);
				textBlock.RenderTransformOrigin = new Point(0.5, 0.5);
				TransformGroup renderTransform = new TransformGroup
				{
					Children = 
					{
						(Transform)new RotateTransform(random.Next(-75, 75)),
						(Transform)new TranslateTransform(random.Next(-600, 600), random.Next(-400, 400))
					}
				};
				double num2 = (random.NextDouble() + 0.1 + (double)censorIntensity) / 2.0 * 255.0;
				if (num2 > 255.0)
				{
					num2 = 255.0;
				}
				textBlock.Background = new SolidColorBrush(Color.FromArgb((byte)num2, (byte)random.Next(200, 255), (byte)random.Next(0, 100), (byte)random.Next(200, 255)));
				textBlock.Foreground = Brushes.White;
				textBlock.RenderTransform = renderTransform;
				textBlock.FontFamily = new FontFamily("Times New Roman");
				textBlock.TextAlignment = TextAlignment.Center;
				textBlock.FontSize = (double)random.Next(40, 110) * ((double)(censorIntensity / 2f) + 0.3);
				textBlock.Text = tauntString;
				textBlock.IsHitTestVisible = false;
				TrimCensorGridOverlays(24);
				censorGrid.Children.Add(textBlock);
				textBlock.Margin = new Thickness(-4000.0);
				Task.Delay(random.Next(1200, 36000)).ContinueWith((Task t) => base.Dispatcher.InvokeAsync(delegate
				{
					censorGrid.Children.Remove(textBlock);
				}));
			}, DispatcherPriority.ApplicationIdle);
		}
		int num = 2600 - (int)(censorIntensity * 1000f);
		if (num < 400)
		{
			num = 400;
		}
		Task.Delay(num).ContinueWith(delegate
		{
			censorScreen();
		});
	}

	private void TrimCensorGridOverlays(int maxChildren)
	{
		while (censorGrid.Children.Count > maxChildren && censorGrid.Children.Count > 1)
		{
			censorGrid.Children.RemoveAt(1);
		}
	}

	public Color InvertColor(Color color)
	{
		return Color.FromRgb((byte)(255 - color.R), (byte)(255 - color.G), (byte)(255 - color.B));
	}

	public Color brightenColor(Color color)
	{
		double num = 0.0;
		if ((double)(int)color.R > num)
		{
			num = (int)color.R;
		}
		if ((double)(int)color.G > num)
		{
			num = (int)color.G;
		}
		if ((double)(int)color.B > num)
		{
			num = (int)color.B;
		}
		num = 255.0 / num;
		return Color.FromRgb((byte)(num * (double)(int)color.R), (byte)(num * (double)(int)color.G), (byte)(num * (double)(int)color.B));
	}

	private void chromaStringBuilder()
	{
		string text = "";
		int num = -1;
		for (int i = 0; i < 2; i++)
		{
			string text2 = "";
			int num2 = 0;
			while (num2 < 2)
			{
				int num3 = random.Next(0, chromaText.Count);
				if (num != num3)
				{
					text2 = text2 + chromaText[num3] + " - ";
					num = num3;
					num2++;
				}
			}
			while (text2.Count() < 120)
			{
				text2 += text2;
			}
			text2 = text2.Substring(text2.Count() - 120);
			text = text + text2 + "\n";
		}
		for (int j = 0; j < 4; j++)
		{
			text += text;
		}
		chromaString = text;
		Task.Delay(10000).ContinueWith(delegate
		{
			chromaStringBuilder();
		});
	}

	private void makeTauntString()
	{
		Task.Run(delegate
		{
			int num = -1;
			string text = "";
			for (int i = 0; i < 2; i++)
			{
				int num2 = 0;
				while (num2 < 2)
				{
					int num3 = random.Next(0, chromaText.Count);
					if (num != num3)
					{
						text = text + chromaText[num3] + " - ";
						num = num3;
						num2++;
					}
				}
			}
			while (text.Count() < 500)
			{
				text += text;
			}
			tauntString = text;
		});
	}

	private void secondWindow_KeyDown(object sender, KeyEventArgs e)
	{
		eventHandeler.OnButtonKeyDown(sender, e);
	}

	private void videoWindow_MediaFailed(object sender, ExceptionRoutedEventArgs e)
	{
		SessionTraceLogger.Error("media-video", "MediaElement failed relative=" + currentVideoRelativePath + " source=" + (videoWindow.Source?.LocalPath ?? ""), e.ErrorException);
		videoWindow.Stop();
		videoWindow.Source = null;
		videoWindow.Visibility = Visibility.Hidden;
		consecutiveVideoFailures++;
		if (videoPaths.Count > 1 && consecutiveVideoFailures < videoPaths.Count)
		{
			mediaScreen(8);
		}
	}

	private void videoWindow_MediaOpened(object sender, RoutedEventArgs e)
	{
		consecutiveVideoFailures = 0;
		if (videoWindow.NaturalDuration.HasTimeSpan && !fromBeginning)
		{
			double totalSeconds = videoWindow.NaturalDuration.TimeSpan.TotalSeconds;
			if (!(totalSeconds < 15.0))
			{
				if (totalSeconds < 30.0)
				{
					videoWindow.Position += TimeSpan.FromSeconds(random.Next((int)(totalSeconds * 0.5)));
				}
				else
				{
					videoWindow.Position += TimeSpan.FromSeconds(random.Next((int)(totalSeconds * 0.05), (int)(totalSeconds * 0.75)));
				}
			}
			checkIfLoaded(videoWindow.Position, videoWindow);
			return;
		}
		if (videoWindow.NaturalDuration.HasTimeSpan && fromBeginning)
		{
			checkIfLoaded(videoWindow.Position, videoWindow);
			return;
		}
		Task.Delay(100).ContinueWith(delegate
		{
			base.Dispatcher.InvokeAsync(delegate
			{
				videoWindow_MediaOpened(sender, e);
			});
		});
	}

	private void checkIfLoaded(TimeSpan time, MediaElement myMediaElement, int repeat = 0)
	{
		if (repeat < 40)
		{
			base.Dispatcher.InvokeAsync(delegate
			{
				if (myMediaElement.Position > time)
				{
					videoWindow.Visibility = Visibility.Visible;
					if (blocks.Count > 0)
					{
						blocks[blocks.Count - 1].Visibility = Visibility.Visible;
					}
				}
				else
				{
					Task.Delay(50).ContinueWith(delegate
					{
						checkIfLoaded(time, myMediaElement, repeat + 1);
					});
				}
			});
		}
		else
		{
			mediaScreen();
		}
	}

	private void videoWindow_MediaEnded(object sender, RoutedEventArgs e)
	{
		bool shortVideo = videoWindow.NaturalDuration.HasTimeSpan && videoWindow.NaturalDuration.TimeSpan.TotalSeconds < 8.0;
		if (imgLocked || shortVideo || (videoLoops && videoWindow.NaturalDuration.HasTimeSpan))
		{
			videoWindow.Position = TimeSpan.Zero;
		}
		else
		{
			mediaScreen();
		}
	}
}
