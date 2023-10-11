using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Xml.Linq;
using static FoxyLayout.FoxyUtilities;

namespace FoxyLayout
{
	/* Event flows:
     * 1) MouseDown -> MOVE -> OnMovingTimerTick -> MouseUp
     * 2) MouseDown -> SIZE -> MouseUp
     */
	internal sealed class FoxyWindow : IDisposable
	{
		private const int movingTimerMs = 50;
		private const int resizingTimerMs = 50;

		private HwndSource _hwndSrc;
		private HwndSourceHook _hwndSrcHook;
		private Rect cancelBounds;
		private bool cancelResize;
		private bool codeResize;
		private bool disposedValue;
		private bool eventHandlersSubscribed;
		private bool isMouseDown;
		private bool isSizing;
		private System.Timers.Timer movingTimer;
		private bool movingTimerOnce;
		private List<KeyValuePair<SharedBorder, FoxyWindow>> resizingBorders;
		private System.Timers.Timer resizingTimer;
		private bool resizingTimerOnce;
		private Rect? splittingShape;
		private List<KeyValuePair<int, FoxyWindow>> splittingWindows;
		private int splittingLevel;
		private int splittingCounter;
		private SharedBorder lastSplittingBorder;

		public FoxyWindow(Window window)
		{
			FoxyLog($"FoxyWindow() called: {window}", LogEventLevel.Debug);
			movingTimer = new System.Timers.Timer(movingTimerMs) { AutoReset = true, Enabled = false };
			movingTimer.Elapsed += OnMovingTimerTick;
			resizingTimer = new System.Timers.Timer(resizingTimerMs) { AutoReset = true, Enabled = false };
			resizingTimer.Elapsed += OnResizingTimerTick;
			Window = window;

			InvokeIfNecessary(Window.Dispatcher, () =>
			{
				SetScreen();
				if (Window.IsLoaded)
					OnLoaded(window, null);

				AddEventHandlers();
			});
		}

		public void AddEventHandlers()
		{
			InvokeIfNecessary(Window.Dispatcher, () =>
			{
				if (!eventHandlersSubscribed)
				{
					Window.Loaded += OnLoaded;
					Window.Unloaded += OnUnloaded;
					Window.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
					Window.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
					eventHandlersSubscribed = true;
				}
			});
		}

		private bool IsValidSize(Rect rect)
		{
			bool isValidSize = true;
			InvokeIfNecessary(Window.Dispatcher, () =>
			{
				if (Window.MinWidth > rect.Width || Window.MinHeight > rect.Height)
					isValidSize = false;
			});
			return isValidSize;
		}

		private void Dispose(bool disposing)
		{
			FoxyLog($"FoxyWindow.Dispose({disposing}) called: {Window}", LogEventLevel.Debug);
			if (!disposedValue)
			{
				if (disposing)
				{
					DropEventHandlers();
					movingTimer.Elapsed -= OnMovingTimerTick;
					resizingTimer.Elapsed -= OnResizingTimerTick;
					Window = null;
					resizingBorders = null;
					splittingWindows = null;
				}
				disposedValue = true;
			}
		}

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		public void DropEventHandlers()
		{
			InvokeIfNecessary(Window.Dispatcher, () =>
			{
				if (eventHandlersSubscribed)
				{
					Window.Loaded -= OnLoaded;
					Window.Unloaded -= OnUnloaded;
					Window.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
					Window.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
					eventHandlersSubscribed = false;
				}
			});
		}
		// NT problems: NT windows rarely if ever throw SIZING and dont always throw SIZE when resizing
		// This forces us to tie everything into the mouse up event to 'clean up' when a size event didnt get tossed
		// For now im not gonna fix this, similar problem with the cancelresize stuff
		private IntPtr FilterMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
		{
			//FoxyLog(Enum.IsDefined(typeof(WindowMessages), msg) ? Enum.GetName(typeof(WindowMessages), msg) : msg.ToString(), LogEventLevel.Verbose);
			switch (msg)
			{
				case (int)WindowMessages.MOVE:
					if (isSizing || !isMouseDown)
					{
						movingTimer.Enabled = false;
						movingTimerOnce = false;
					}
					else if (!movingTimer.Enabled)
						movingTimer.Enabled = true;
					break;
				case (int)WindowMessages.SIZE:
					if (!codeResize)
					{
						if (isMouseDown)
						{
							isSizing = true;
							resizingTimer.Enabled = true;
						}
						movingTimer.Enabled = false;
						movingTimerOnce = false;
					}
					break;
			}

			return IntPtr.Zero;
		}

		private void OnLoaded(object sender, RoutedEventArgs e)
		{
			UpdateBounds();
			_hwndSrc = HwndSource.FromDependencyObject(sender as Window) as HwndSource;
			_hwndSrcHook = FilterMessage;
			_hwndSrc.AddHook(_hwndSrcHook);
		}

		public void ResizeWithout()
		{
			FoxyLog($"FoxyWindow.ResizeWithout() called: {Window}", LogEventLevel.Debug);
			if (IsLocked)
			{
				Dictionary<ResizeDirection, List<FoxyWindow>> windowsToResize = new Dictionary<ResizeDirection, List<FoxyWindow>>();
				foreach (ResizeDirection direction in Enum.GetValues(typeof(ResizeDirection)))
					windowsToResize.Add(direction, new List<FoxyWindow>());

				foreach (FoxyWindow foxyWindow in FoxyLayoutAddOn.FoxyWindows[NinjaTrader.Core.Globals.ActiveWorkspace])
				{
					if (foxyWindow != this && foxyWindow.Screen == Screen && foxyWindow.IsLocked)
					{
						SharedBorder border = FindSharedBorder(Bounds, foxyWindow.Bounds);
						switch (border)
						{
							case SharedBorder.Top:
								if (ApproxCompare(foxyWindow.Bounds.Width, Bounds.Width) <= 0)
									windowsToResize[ResizeDirection.Down].Add(foxyWindow);
								break;
							case SharedBorder.Bottom:
								if (ApproxCompare(foxyWindow.Bounds.Width, Bounds.Width) <= 0)
									windowsToResize[ResizeDirection.Up].Add(foxyWindow);
								break;
							case SharedBorder.Left:
								if (ApproxCompare(foxyWindow.Bounds.Height, Bounds.Height) <= 0)
									windowsToResize[ResizeDirection.Right].Add(foxyWindow);
								break;
							case SharedBorder.Right:
								if (ApproxCompare(foxyWindow.Bounds.Height, Bounds.Height) <= 0)
									windowsToResize[ResizeDirection.Left].Add(foxyWindow);
								break;
						}
					}
				}

				bool movingHorizontal = false;
				bool movingVertical = false;

				foreach (KeyValuePair<ResizeDirection, List<FoxyWindow>> keyValuePair in windowsToResize)
				{
					foreach (FoxyWindow foxyWindow in keyValuePair.Value)
					{
						foxyWindow.Window.Dispatcher.BeginInvoke((Action)(() => // Because we dont call ResizeWithout when application is exiting IN THEORY we can get away with regular Invoke here but I'm not gonna mess with it unless BeginInvoke is causing problems
						{
							try
							{
								switch (keyValuePair.Key)
								{
									case ResizeDirection.Left:
										if (movingVertical)
											break;

										double pxToMoveLeft = Bounds.Width / windowsToResize.Where(kvp => kvp.Key == ResizeDirection.Left || kvp.Key == ResizeDirection.Right)
																					.SelectMany(kvp => kvp.Value)
																					.Where(fw => ApproxCompare(fw.Bounds.Top, foxyWindow.Bounds.Top) == 0 || ApproxCompare(fw.Bounds.Bottom, foxyWindow.Bounds.Bottom) == 0)
																					.GroupBy(fw => fw.Bounds.Left)
																					.Select(g => g.First())
																					.Count();
										FoxyLog($"\t'{foxyWindow.Window}' pxToMoveLeft: {pxToMoveLeft} count: {Bounds.Width / pxToMoveLeft} windowBounds.Width: {Bounds.Width}");
										foxyWindow.Window.Left -= pxToMoveLeft;
										foxyWindow.Window.Width += pxToMoveLeft;
										movingHorizontal = true;
										break;
									case ResizeDirection.Right:
										if (movingVertical)
											break;

										double pxToMoveRight = Bounds.Width / windowsToResize.Where(kvp => kvp.Key == ResizeDirection.Left || kvp.Key == ResizeDirection.Right)
																					.SelectMany(kvp => kvp.Value)
																					.Where(fw => ApproxCompare(fw.Bounds.Top, foxyWindow.Bounds.Top) == 0 || ApproxCompare(fw.Bounds.Bottom, foxyWindow.Bounds.Bottom) == 0)
																					.GroupBy(fw => fw.Bounds.Right)
																					.Select(g => g.First())
																					.Count();
										FoxyLog($"\t{foxyWindow.Window} pxToMoveRight: {pxToMoveRight} count: {Bounds.Width / pxToMoveRight} windowBounds.Width: {Bounds.Width}");
										foxyWindow.Window.Width += pxToMoveRight;
										movingHorizontal = true;
										break;
									case ResizeDirection.Up:
										if (movingHorizontal)
											break;

										double pxToMoveUp = Bounds.Height / windowsToResize.Where(kvp => kvp.Key == ResizeDirection.Up || kvp.Key == ResizeDirection.Down)
																					.SelectMany(kvp => kvp.Value)
																					.Where(fw => ApproxCompare(fw.Bounds.Left, foxyWindow.Bounds.Left) == 0 || ApproxCompare(fw.Bounds.Right, foxyWindow.Bounds.Right) == 0)
																					.GroupBy(fw => fw.Bounds.Top)
																					.Select(g => g.First())
																					.Count();
										FoxyLog($"\t{foxyWindow.Window} pxToMoveUp: {pxToMoveUp} count: {Bounds.Width / pxToMoveUp} windowBounds.Width: {Bounds.Width}");
										foxyWindow.Window.Top -= pxToMoveUp;
										foxyWindow.Window.Height += pxToMoveUp;
										movingVertical = true;
										break;
									case ResizeDirection.Down:
										if (movingHorizontal)
											break;

										double pxToMoveDown = Bounds.Height / windowsToResize.Where(kvp => kvp.Key == ResizeDirection.Up || kvp.Key == ResizeDirection.Down)
																					.SelectMany(kvp => kvp.Value)
																					.Where(fw => ApproxCompare(fw.Bounds.Left, foxyWindow.Bounds.Left) == 0 || ApproxCompare(fw.Bounds.Right, foxyWindow.Bounds.Right) == 0)
																					.GroupBy(fw => fw.Bounds.Bottom)
																					.Select(g => g.First())
																					.Count();
										FoxyLog($"\t{foxyWindow.Window} pxToMoveDown: {pxToMoveDown} count: {Bounds.Width / pxToMoveDown} windowBounds.Width: {Bounds.Width}");
										foxyWindow.Window.Height += pxToMoveDown;
										movingVertical = true;
										break;
								}
								foxyWindow.UpdateBounds();
							}
							catch (Exception e)
							{
								FoxyLog(e.ToString(), LogEventLevel.Error);
							}
						}));
					}
				}
				IsLocked = false;
			}
		}

		public void RestoreFromXml(string workspaceName)
		{
			FoxyLog($"FoxyWindow.RestoreFromXml('{workspaceName}') called: {Window} {IsRestored} {IsLocked} {Bounds}", LogEventLevel.Debug);
			if (!IsRestored)
			{
				lock (FoxyLayoutAddOn.FoxyRestore)
				{
					WorkspaceOptions workspaceOptions = (Window as IWorkspacePersistence)?.WorkspaceOptions;
					if (FoxyLayoutAddOn.FoxyRestore.ContainsKey(workspaceName))
					{
						XElement element = FoxyLayoutAddOn.FoxyRestore[workspaceName].Element(workspaceOptions?.PersistenceId ?? Window.ToString());
						if (element != null)
						{
							IsLocked = true;
							if (Window is ControlCenter)
							{
								string[] bounds = element.Element("Bounds").Value.Split(',');
								Window.Dispatcher.BeginInvoke((Action)(() =>
								{
									Window.Left = double.Parse(bounds[0]);
									Window.Top = double.Parse(bounds[1]);
									Window.Width = double.Parse(bounds[2]);
									Window.Height = double.Parse(bounds[3]);
									UpdateBounds();
								}));
							}
							IsRestored = true;
						}
					}
				}
			}
			FoxyLog($"FoxyWindow.RestoreFromXml2('{workspaceName}') called: {Window} {IsRestored} {IsLocked} {Bounds}", LogEventLevel.Debug);
		}

		// Remember this is its own thread: EVERYTHING MUST BE THREADSAFE
		private void OnMovingTimerTick(object source, System.Timers.ElapsedEventArgs e)
		{
			try
			{
				FoxyLog($"FoxyWindow.OnMovingTimerTick() called: {Window}", LogEventLevel.Debug);
				Point cursorPosition = new Point();
				InvokeIfNecessary(Window.Dispatcher, () =>
				{
					// Gtting DPI aware cursor position in WPF -_-
					cursorPosition = PresentationSource.FromVisual(Window).CompositionTarget.TransformFromDevice.Transform(Window.PointToScreen(Mouse.GetPosition(Window)));

					FoxyLog($"\tcursorPosition: {cursorPosition}");

					// This code is a bit weird, eventually look why it has to be like this
					if (!movingTimerOnce)
					{
						splittingWindows = new List<KeyValuePair<int, FoxyWindow>>();
						ResizeWithout();
					}

					if (isSizing || !isMouseDown)
					{
						movingTimer.Enabled = false;
						movingTimerOnce = false;
						return;
					}

					if (!movingTimerOnce && (GetKeyState(FoxyUtilities.Options.ModifierVirtualKey) & 0x8000) == 0x8000 != FoxyUtilities.Options.ModifierDisables)
					{
						movingTimerOnce = true;
						codeResize = true;
						Window.Width = 400;
						Window.Height = 400;
						Window.Left = cursorPosition.X - 5;
						Window.Top = cursorPosition.Y - 5;
					}
					else if (codeResize && (GetKeyState(FoxyUtilities.Options.ModifierVirtualKey) & 0x8000) == 0x8000 == FoxyUtilities.Options.ModifierDisables)
					{
						Window.Width = Bounds.Width;
						Window.Height = Bounds.Height;
						Window.Left = cursorPosition.X - 5;
						Window.Top = cursorPosition.Y - 5;
						codeResize = false;
						movingTimerOnce = false;
					}
				});

				splittingCounter += movingTimerMs;

				foreach (FoxyScreen foxyScreen in FoxyLayoutAddOn.FoxyScreens)
				{
					Rect foxyScreenBounds = foxyScreen.GetBounds();
					if (PointInBounds(cursorPosition.X, cursorPosition.Y, foxyScreenBounds))
					{
						Rect? lastSplittingShape = splittingShape;
						List<KeyValuePair<int, FoxyWindow>> newSplittingWindows = new List<KeyValuePair<int, FoxyWindow>>();
						SharedBorder splittingBorder = SharedBorder.None;

						foreach (FoxyWindow foxyWindow in FoxyLayoutAddOn.FoxyWindows[NinjaTrader.Core.Globals.ActiveWorkspace])
						{
							if (foxyWindow != this && foxyWindow.IsLocked && foxyWindow.Screen == foxyScreen && PointInBounds(cursorPosition.X, cursorPosition.Y, foxyWindow.Bounds))
							{
								FoxyWindow[] tempWindows = null;

								if (ApproxCompare(cursorPosition.X, foxyWindow.Bounds.Left + foxyWindow.Bounds.Width / 3.0) <= 0)
								{
									// Left Third
									tempWindows = FoxyLayoutAddOn.FoxyWindows[NinjaTrader.Core.Globals.ActiveWorkspace].Where(fw => fw != this && fw.IsLocked && fw.Screen == foxyScreen && ApproxCompare(fw.Bounds.Left, foxyWindow.Bounds.Left) == 0).OrderBy(fw => fw.Bounds.Top).ToArray();
									splittingBorder = SharedBorder.Left;
									splittingShape = new Rect(foxyWindow.Bounds.Left, foxyWindow.Bounds.Top, foxyWindow.Bounds.Width / 2.0, foxyWindow.Bounds.Height);
								}
								else if (ApproxCompare(cursorPosition.X, foxyWindow.Bounds.Left + (foxyWindow.Bounds.Width / 3.0) * 2.0) >= 0)
								{
									// Right Third
									tempWindows = FoxyLayoutAddOn.FoxyWindows[NinjaTrader.Core.Globals.ActiveWorkspace].Where(fw => fw != this && fw.IsLocked && fw.Screen == foxyScreen && ApproxCompare(fw.Bounds.Right, foxyWindow.Bounds.Right) == 0).OrderBy(fw => fw.Bounds.Top).ToArray();
									splittingBorder = SharedBorder.Right;
									splittingShape = new Rect(foxyWindow.Bounds.Left + foxyWindow.Bounds.Width / 2.0, foxyWindow.Bounds.Top, foxyWindow.Bounds.Width / 2.0, foxyWindow.Bounds.Height);
								}
								else
								{
									if (ApproxCompare(cursorPosition.Y, foxyWindow.Bounds.Top + foxyWindow.Bounds.Height / 2.0) <= 0)
									{
										// Top Half
										tempWindows = FoxyLayoutAddOn.FoxyWindows[NinjaTrader.Core.Globals.ActiveWorkspace].Where(fw => fw != this && fw.IsLocked && fw.Screen == foxyScreen && ApproxCompare(fw.Bounds.Top, foxyWindow.Bounds.Top) == 0).OrderBy(fw => fw.Bounds.Left).ToArray();
										splittingBorder = SharedBorder.Top;
										splittingShape = new Rect(foxyWindow.Bounds.Left, foxyWindow.Bounds.Top, foxyWindow.Bounds.Width, foxyWindow.Bounds.Height / 2.0);
									}
									else
									{
										// Bottom Half
										tempWindows = FoxyLayoutAddOn.FoxyWindows[NinjaTrader.Core.Globals.ActiveWorkspace].Where(fw => fw != this && fw.IsLocked && fw.Screen == foxyScreen && ApproxCompare(fw.Bounds.Bottom, foxyWindow.Bounds.Bottom) == 0).OrderBy(fw => fw.Bounds.Left).ToArray();
										splittingBorder = SharedBorder.Bottom;
										splittingShape = new Rect(foxyWindow.Bounds.Left, foxyWindow.Bounds.Top + foxyWindow.Bounds.Height / 2.0, foxyWindow.Bounds.Width, foxyWindow.Bounds.Height / 2.0);
									}
								}

								int windowIndex = Array.IndexOf(tempWindows, foxyWindow);

								FoxyLog($"\tsplittingBorder: {splittingBorder} tempWindows: {string.Join(", ", tempWindows.Select(fw => fw.Window.ToString()).ToArray())} windowIndex: {windowIndex}");
								List<List<FoxyWindow>> possibilities = new List<List<FoxyWindow>>();

								for (int idx = tempWindows.Length - 1; idx > windowIndex - 1; idx--)
								{
									for (int i = windowIndex; i > -1; i--)
									{
										List<FoxyWindow> temp = new List<FoxyWindow>();
										for (int j = i; j < idx + 1; j++)
											temp.Add(tempWindows[j]);
										possibilities.Add(temp);
									}
								}
								int levelCounter = 0;
								foreach (List<FoxyWindow> possibility in possibilities.OrderBy(p => p.Count))
								{
									foreach (FoxyWindow possibleWindow in possibility)
										newSplittingWindows.Add(new KeyValuePair<int, FoxyWindow>(levelCounter, possibleWindow));
									levelCounter++;
								}
								break;
							}
						}

						bool listsMatch = true;
						if (newSplittingWindows.Count == splittingWindows.Count)
						{
							foreach (KeyValuePair<int, FoxyWindow> kvp in newSplittingWindows)
								if (!splittingWindows.Contains(kvp) || splittingBorder != lastSplittingBorder)
								{
									FoxyLog("\tLists don't match!");
									listsMatch = false;
									break;
								}
						}
						else
							listsMatch = false;

						lastSplittingBorder = splittingBorder;

						if (listsMatch && splittingCounter % (SecondsToChangeLevel * 1000) == 0 && newSplittingWindows.Count > 0)
							splittingLevel = Math.Min(Math.Max(0, newSplittingWindows.Max(kvp => kvp.Key)), splittingLevel + 1);
						if (!listsMatch)
						{
							splittingCounter = 0;
							splittingLevel = 0;
						}

						FoxyLog($"\tsplittingLevel: {splittingLevel} splittingCounter: {splittingCounter}");
						// Here we define the splittingShape based on the splittingLevel and the splittingWindows
						// Height needs to be 1/3 of the smallest height and Width needs to be 1/3 of the smallest width
						if (newSplittingWindows.Count > 1)
						{
							List<KeyValuePair<int, FoxyWindow>> filteredNewSplittingWindows = newSplittingWindows.Where(kvp => kvp.Key == splittingLevel).ToList();
							if (filteredNewSplittingWindows.Count > 0)
							{
								switch (splittingBorder)
								{
									case SharedBorder.Left:
										splittingShape = new Rect(newSplittingWindows[0].Value.Bounds.Left,
											filteredNewSplittingWindows.Min(kvp => kvp.Value.Bounds.Top),
											filteredNewSplittingWindows.Min(kvp => kvp.Value.Bounds.Width) / 2.0,
											filteredNewSplittingWindows.Sum(kvp => kvp.Value.Bounds.Height));
										break;
									case SharedBorder.Right:
										splittingShape = new Rect(newSplittingWindows[0].Value.Bounds.Left + filteredNewSplittingWindows.Min(kvp => kvp.Value.Bounds.Width) / 2.0,
											filteredNewSplittingWindows.Min(kvp => kvp.Value.Bounds.Top),
											filteredNewSplittingWindows.Min(kvp => kvp.Value.Bounds.Width) / 2.0,
											filteredNewSplittingWindows.Sum(kvp => kvp.Value.Bounds.Height));
										break;
									case SharedBorder.Top:
										splittingShape = new Rect(filteredNewSplittingWindows.Min(kvp => kvp.Value.Bounds.Left),
											newSplittingWindows[0].Value.Bounds.Top,
											filteredNewSplittingWindows.Sum(kvp => kvp.Value.Bounds.Width),
											filteredNewSplittingWindows.Min(kvp => kvp.Value.Bounds.Height) / 2.0);
										break;
									case SharedBorder.Bottom:
										splittingShape = new Rect(filteredNewSplittingWindows.Min(kvp => kvp.Value.Bounds.Left),
											filteredNewSplittingWindows.Max(kvp => kvp.Value.Bounds.Top) + filteredNewSplittingWindows.Min(kvp => kvp.Value.Bounds.Height) / 2.0,
											filteredNewSplittingWindows.Sum(kvp => kvp.Value.Bounds.Width),
											filteredNewSplittingWindows.Min(kvp => kvp.Value.Bounds.Height) / 2.0);
										break;
								}
							}
						}

						if (!splittingShape.HasValue && FoxyLayoutAddOn.FoxyWindows[NinjaTrader.Core.Globals.ActiveWorkspace].Where(fw => fw.IsLocked).Count() == 0)
							splittingShape = foxyScreen.Screen.WorkingArea;

						FoxyLog($"\tsplittingShape: {splittingShape}");

						if (((GetKeyState(FoxyUtilities.Options.ModifierVirtualKey) & 0x8000) == 0x8000) == FoxyUtilities.Options.ModifierDisables)
						{
							splittingShape = null;
							foxyScreen?.ClearCanvas();
						}

						if (!lastSplittingShape.HasValue || lastSplittingShape.Value != splittingShape)
						{
							if (splittingShape.HasValue)
								foxyScreen?.DrawRectangle(splittingShape.Value);
						}

						splittingWindows = newSplittingWindows;

						FoxyLog($"\tsplittingWindows: {string.Join(", ", splittingWindows.Select(sw => $"{sw.Key}: {sw.Value.Window}").ToArray())}");
						InvokeIfNecessary(Window.Dispatcher, () => { Window.Topmost = true; });
						break;
					}
				}
			}
			catch (Exception ex)
			{
				Serilog.Log.Error(ex.ToString());
			}
		}

		private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			FoxyLog($"FoxyWindow.OnPreviewMouseLeftButtonDown() called: {Window}", LogEventLevel.Debug);
			isMouseDown = true;
		}

		private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
		{
			FoxyLog($"FoxyWindow.OnPreviewMouseLeftButtonUp() called: {Window}", LogEventLevel.Debug);
			codeResize = false;
			movingTimer.Enabled = false;
			movingTimerOnce = false;
			resizingTimer.Enabled = false;
			resizingTimerOnce = false;
			resizingBorders = null;
			isSizing = false;
			isMouseDown = false;
			Window.Topmost = false;
			lastSplittingBorder = SharedBorder.None;
			SetScreen();
			Screen?.ClearCanvas();

			if (IsLocked)
			{
				if (FoxyUtilities.Options.BringToFront)
				{
					lock (FoxyLayoutAddOn.FoxyWindows)
					{
						foreach (FoxyWindow foxyWindow in FoxyLayoutAddOn.FoxyWindows[NinjaTrader.Core.Globals.ActiveWorkspace])
						{
							if (foxyWindow.Screen == Screen && foxyWindow.IsLocked && foxyWindow.Window != Window) // use window instead of foxy here to not get tripped up by cc singleton
							{
								foxyWindow.Window.Dispatcher.BeginInvoke((Action)(() =>
								{
									WindowInteropHelper interopHelper = new WindowInteropHelper(foxyWindow.Window);
									uint thisWindowThreadId = GetWindowThreadProcessId(interopHelper.Handle, IntPtr.Zero);
									IntPtr currentHandle = GetForegroundWindow();
									uint currentForegroundWindowThreadId = GetWindowThreadProcessId(currentHandle, IntPtr.Zero);
									AttachThreadInput(currentForegroundWindowThreadId, thisWindowThreadId, true);
									SetWindowPos(interopHelper.Handle, new IntPtr(0), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040 | 0x0010);
									AttachThreadInput(currentForegroundWindowThreadId, thisWindowThreadId, false);
								}));
							}
						}
					}
				}

				if (cancelResize)
				{
					Window.Left = cancelBounds.Left;
					Window.Top = cancelBounds.Top;
					Window.Width = cancelBounds.Width;
					Window.Height = cancelBounds.Height;
					UpdateBounds();
					cancelResize = false;
				}
			}

			if (splittingShape.HasValue && ((GetKeyState(FoxyUtilities.Options.ModifierVirtualKey) & 0x8000) == 0x8000) != FoxyUtilities.Options.ModifierDisables)
			{
				IsLocked = true;
				InvokeIfNecessary(Window.Dispatcher, () =>
				{
					Window.Top = splittingShape.Value.Top;
					Window.Left = splittingShape.Value.Left;
					Window.Width = splittingShape.Value.Width;
					Window.Height = splittingShape.Value.Height;
				});

				foreach (KeyValuePair<int, FoxyWindow> kvp in splittingWindows.Where(kvp => kvp.Key == splittingLevel))
				{
					FoxyWindow splittingWindow = kvp.Value;
					InvokeIfNecessary(splittingWindow.Window.Dispatcher, () =>
					{
						Rect offset = Rect.Intersect(splittingShape.Value, splittingWindow.Bounds); // This represents the space we are losing
						SharedBorder resizeBorder = (new SharedBorder[] { SharedBorder.LeftSame, SharedBorder.RightSame, SharedBorder.TopSame, SharedBorder.BottomSame }).Except(FindSameBorders(splittingWindow.Bounds, offset)).First();
						FoxyLog($"\toffset: {offset} resizeBorder: {resizeBorder}");
						switch (resizeBorder)
						{
							case SharedBorder.RightSame:
								splittingWindow.Window.Left += offset.Width;
								splittingWindow.Window.Width -= offset.Width;
								break;
							case SharedBorder.LeftSame:
								splittingWindow.Window.Width -= offset.Width;
								break;
							case SharedBorder.BottomSame:
								splittingWindow.Window.Top += offset.Height;
								splittingWindow.Window.Height -= offset.Height;
								break;
							case SharedBorder.TopSame:
								splittingWindow.Window.Height -= offset.Height;
								break;
						}

						splittingWindow.UpdateBounds();
					});
				}

				splittingWindows = null;
				splittingLevel = 0;
				splittingShape = null;
				UpdateBounds();
			}
		}

		private void OnResizingTimerTick(object source, System.Timers.ElapsedEventArgs e)
		{
			FoxyLog($"FoxyWindow.OnResizingTimerTick() called: {Window}", LogEventLevel.Debug);
			try
			{
				InvokeIfNecessary(Window.Dispatcher, () =>
				{
					if (IsLocked)
					{
						Rect lastBounds = Bounds;
						if (!resizingTimerOnce)
						{
							cancelBounds = Bounds;
							resizingTimerOnce = true;
							resizingBorders = new List<KeyValuePair<SharedBorder, FoxyWindow>>();

							foreach (FoxyWindow foxyWindow in FoxyLayoutAddOn.FoxyWindows[NinjaTrader.Core.Globals.ActiveWorkspace])
							{
								if (this != foxyWindow && foxyWindow.IsLocked)
								{
									SharedBorder sharedBorder = FindSharedBorder(Bounds, foxyWindow.Bounds);
									if (sharedBorder != SharedBorder.None)
										resizingBorders.Add(new KeyValuePair<SharedBorder, FoxyWindow>(sharedBorder, foxyWindow));
									foreach (SharedBorder sameBorder in FindSameBorders(Bounds, foxyWindow.Bounds))
										resizingBorders.Add(new KeyValuePair<SharedBorder, FoxyWindow>(sameBorder, foxyWindow));
								}
							}
						}
						UpdateBounds();

						foreach (SharedBorder sameBorder in FindSameBorders(cancelBounds, Screen.GetBounds()))
						{
							bool needsCancel = false;
							switch (sameBorder)
							{
								case SharedBorder.LeftSame:
									if (ApproxCompare(Bounds.Left, cancelBounds.Left) != 0)
										needsCancel = true;
									break;
								case SharedBorder.TopSame:
									if (ApproxCompare(Bounds.Top, cancelBounds.Top) != 0)
										needsCancel = true;
									break;
								case SharedBorder.RightSame:
									if (ApproxCompare(Bounds.Right, cancelBounds.Right) != 0)
										needsCancel = true;
									break;
								case SharedBorder.BottomSame:
									if (ApproxCompare(Bounds.Bottom, cancelBounds.Bottom) != 0)
										needsCancel = true;
									break;
							}
							if (needsCancel)
							{
								//Screen?.ClearCanvas();
								Screen?.DrawIntersection(Bounds, cancelBounds, GeometryCombineMode.Xor);
								cancelResize = true;
								return;
							}
						}

						Dictionary<FoxyWindow, Rect> windowsToResize = new Dictionary<FoxyWindow, Rect>(); // Use this so we can rollback resizes

						foreach (KeyValuePair<SharedBorder, FoxyWindow> kvp in resizingBorders)
						{
							FoxyWindow foxyWindow = kvp.Value;
							Rect newBounds = windowsToResize.ContainsKey(foxyWindow) ? windowsToResize[foxyWindow] : foxyWindow.Bounds;
							FoxyLog($"\t'{foxyWindow.Window}' Before Bounds: {foxyWindow.Bounds}");
							switch (kvp.Key)
							{
								case SharedBorder.Left:
									newBounds.Width = Math.Abs(foxyWindow.Bounds.Left - Bounds.Left);
									break;
								case SharedBorder.LeftSame:
									newBounds = new Rect(Bounds.Left, newBounds.Top, Math.Abs(Bounds.Left - foxyWindow.Bounds.Right), newBounds.Height);
									break;
								case SharedBorder.Right:
									newBounds = new Rect(Bounds.Right, newBounds.Top, Math.Abs(Bounds.Right - foxyWindow.Bounds.Right), newBounds.Height);
									break;
								case SharedBorder.RightSame:
									newBounds.Width = Math.Abs(foxyWindow.Bounds.Left - Bounds.Right);
									break;
								case SharedBorder.Top:
									newBounds.Height = Math.Abs(foxyWindow.Bounds.Top - Bounds.Top);
									break;
								case SharedBorder.TopSame:
									newBounds = new Rect(newBounds.Left, Bounds.Top, newBounds.Width, Math.Abs(Bounds.Top - foxyWindow.Bounds.Bottom));
									break;
								case SharedBorder.Bottom:
									newBounds = new Rect(newBounds.Left, Bounds.Bottom, newBounds.Width, Math.Abs(Bounds.Bottom - foxyWindow.Bounds.Bottom));
									break;
								case SharedBorder.BottomSame:
									newBounds.Height = Math.Abs(foxyWindow.Bounds.Top - Bounds.Bottom);
									break;
							}

							if (newBounds != foxyWindow.Bounds)
							{
								if (!foxyWindow.IsValidSize(newBounds))
								{
									//Screen?.ClearCanvas();
									Screen?.DrawIntersection(Bounds, foxyWindow.Bounds, GeometryCombineMode.Intersect);
									if (!cancelResize)
										cancelBounds = lastBounds;
									cancelResize = true;
									return;
								}
								else
								{
									if (windowsToResize.ContainsKey(foxyWindow))
										windowsToResize[foxyWindow] = newBounds;
									else
										windowsToResize.Add(foxyWindow, newBounds);
								}
							}
						}
						cancelResize = false;
						Screen?.ClearCanvas();
						foreach (KeyValuePair<FoxyWindow, Rect> kvp in windowsToResize)
						{
							InvokeIfNecessary(kvp.Key.Window.Dispatcher, () =>
							{
								kvp.Key.Window.Left = kvp.Value.Left;
								kvp.Key.Window.Top = kvp.Value.Top;
								kvp.Key.Window.Width = kvp.Value.Width;
								kvp.Key.Window.Height = kvp.Value.Height;
								kvp.Key.UpdateBounds();
								FoxyLog($"\t'{kvp.Key.Window}' After Bounds: {kvp.Key.Bounds}");
							});
						}
					}
				});
			}
			catch (Exception ex)
			{
				FoxyLog(ex.ToString(), LogEventLevel.Error);
			}
		}

		private void OnUnloaded(object sender, RoutedEventArgs e)
		{
			_hwndSrc.RemoveHook(_hwndSrcHook);
			_hwndSrc.Dispose();
			_hwndSrc = null;
		}

		public void RestoreFromBounds()
		{
			InvokeIfNecessary(Window.Dispatcher, () =>
			{
				AddEventHandlers();

				Window.Top = Bounds.Top;
				Window.Left = Bounds.Left;
				Window.Width = Bounds.Width;
				Window.Height = Bounds.Height;
			});
		}

		public void SetScreen()
		{
			foreach (FoxyScreen foxyScreen in FoxyLayoutAddOn.FoxyScreens)
			{
				if (PointInBounds(Window.Left, Window.Top, foxyScreen.GetBounds()))
				{
					Screen = foxyScreen;
					break;
				}
			}
		}

		public void UpdateBounds()
		{
			InvokeIfNecessary(Window.Dispatcher, () => { Bounds = new Rect(Window.Left, Window.Top, Window.ActualWidth, Window.ActualHeight); });
		}

		public Rect Bounds { get; private set; }
		public bool IsLocked { get; set; }
		public bool IsRestored { get; set; }
		public FoxyScreen Screen { get; set; }
		public Window Window { get; set; }

		[DllImport("user32.dll")]
		private static extern IntPtr GetForegroundWindow();

		[DllImport("user32.dll")]
		private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr ProcessId);

		[DllImport("user32.dll")]
		private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

		[DllImport("user32.dll")]
		public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

		[DllImport("user32.dll")]
		public static extern short GetKeyState(int nVirtKey);
	}
}
