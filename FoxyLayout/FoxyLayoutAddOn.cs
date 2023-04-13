using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;

using NinjaTrader.Core;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;

using WpfScreenHelper;
using Serilog.Events;

using static FoxyLayout.FoxyUtilities;

namespace FoxyLayout
{
    public sealed class FoxyLayoutAddOn : AddOnBase
    {
		private Window						controlCenter;
		private NTMenuItem					existingMenuItem;
		private string						lastActiveWorkspace;
		private NTMenuItem					newMenuItem;
		private System.Timers.Timer			sortTimer;
		private static List<FoxyWindow>		unsortedFoxyWindows;

		private void OnActiveWorkspaceChanged(object sender, EventArgs e)
        {
			// In the chain of events this is so far always called LAST
			FoxyLog($"FoxyLayoutAddOn.OnActiveWorkspaceChanged() called: {Globals.ActiveWorkspace}", LogEventLevel.Debug);

			lock (FoxyWindows)
            {
				if (!FoxyWindows.ContainsKey(Globals.ActiveWorkspace))
					FoxyWindows.Add(Globals.ActiveWorkspace, new List<FoxyWindow>());

				if (!FoxyWindows[Globals.ActiveWorkspace].Any(fw => fw.Window == controlCenter))
					FoxyWindows[Globals.ActiveWorkspace].Add(new FoxyWindow(controlCenter));

				foreach (FoxyWindow foxyWindow in FoxyWindows[Globals.ActiveWorkspace])
					foxyWindow.RestoreFromXml(Globals.ActiveWorkspace);

				if (!string.IsNullOrEmpty(lastActiveWorkspace))
					FoxyWindows[lastActiveWorkspace].FirstOrDefault(fw => fw.Window is ControlCenter)?.DropEventHandlers();
				FoxyWindows[Globals.ActiveWorkspace].FirstOrDefault(fw => fw.Window is ControlCenter)?.RestoreFromBounds();
				lastActiveWorkspace = Globals.ActiveWorkspace;
			}

			Globals.RandomDispatcher.BeginInvoke(new Action(() =>
			{
				lock (FoxyWindows)
					if (!FoxyWindows[Globals.ActiveWorkspace].Any(fw => fw.Window is FoxyWorkspace))
					{
						FoxyWorkspace foxyWorkspace = new FoxyWorkspace();
						foxyWorkspace.Show();
						FoxyWindows[Globals.ActiveWorkspace].Add(new FoxyWindow(foxyWorkspace));
					}
			}));
        }

		protected override void OnStateChange()
		{
			FoxyLog($"FoxyLayoutAddOn.OnStateChange() called: {State}", LogEventLevel.Debug);
			if (State == State.SetDefaults)
			{
				Description = "(c) 2023 Nitrus Aphalion";
				Name		= "Foxy Layout";
			}
			else if (State == State.Active)
            {
				sortTimer = new System.Timers.Timer(200) { AutoReset = true, Enabled = false };
				sortTimer.Elapsed += OnSortFoxyWindows;

				FoxyScreens			= new List<FoxyScreen>();
				FoxyWindows			= new Dictionary<string, List<FoxyWindow>>();
				FoxyRestore			= new Dictionary<string, XElement>();
				unsortedFoxyWindows = new List<FoxyWindow>();

				lock (FoxyScreens)
				{
					if (FoxyScreens.Count == 0)
					{
						foreach (Screen screen in Screen.AllScreens)
							Globals.RandomDispatcher.Invoke(() => // Toss these on different UI threads
							{
								FoxyLog($"\tAdding screen '{ screen.DeviceName }' Bounds: { screen.Bounds } WorkingArea: { screen.WorkingArea }");
								FoxyScreen foxyScreen = new FoxyScreen(screen);
								foxyScreen.Show();
								FoxyScreens.Add(foxyScreen);
							});
					}
				}
			}
			else if (State == State.Terminated)
            {
				sortTimer.Elapsed -= OnSortFoxyWindows;
            }
		}

		private void OnMenuItemClick(object sender, RoutedEventArgs e)
		{
			Window owner = Window.GetWindow(sender as NTMenuItem);
			new FoxyLayoutOptions(owner).Show();
		}

		private void OnSortFoxyWindows(object sender, EventArgs e)
        {
			lock (unsortedFoxyWindows)
            {
				List<FoxyWindow> windowsToRemove = new List<FoxyWindow>();
				foreach (FoxyWindow foxyWindow in unsortedFoxyWindows)
				{
					string workspaceName = (foxyWindow.Window as IWorkspacePersistence)?.WorkspaceOptions?.WorkspaceName;
					if (!string.IsNullOrEmpty(workspaceName))
					{
						lock (FoxyWindows)
						{
							if (!FoxyWindows.ContainsKey(workspaceName))
								FoxyWindows.Add(workspaceName, new List<FoxyWindow>());
							foxyWindow.RestoreFromXml(workspaceName);
							FoxyWindows[workspaceName].Add(foxyWindow);
							windowsToRemove.Add(foxyWindow);
							FoxyLog($"\tAdded window '{foxyWindow.Window}' IsLocked: {foxyWindow.IsLocked} Bounds: {foxyWindow.Bounds}");
						}
					}
				}
				foreach (FoxyWindow foxyWindow in windowsToRemove)
					unsortedFoxyWindows.Remove(foxyWindow);
				if (unsortedFoxyWindows.Count == 0)
					sortTimer.Enabled = false;
			}
		}

		protected override void OnWindowCreated(Window window)
        {
			FoxyLog($"FoxyLayoutAddOn.OnWindowCreated() called: {window}", LogEventLevel.Debug);

			if (!(window is ControlCenter) && !(window is FoxyWorkspace) && !(bool)typeof(Window).GetField("_showingAsDialog", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window) && window.ResizeMode != ResizeMode.NoResize && !(window is FoxyWorkspace)) // Don't use layout manager on modal windows, this wont work for winforms afaik
				lock (unsortedFoxyWindows)
				{
					unsortedFoxyWindows.Add(new FoxyWindow(window)); // We don't know what workspace FoxyWindow belongs to until after .Loaded
					sortTimer.Enabled = true;
				}

			ControlCenter cc = window as ControlCenter;
			if (cc == null)
				return;

			controlCenter = cc;

			existingMenuItem = cc.FindFirst("ControlCenterMenuItemTools") as NTMenuItem;
			if (existingMenuItem == null)
				return;

			newMenuItem = new NTMenuItem
			{
				Header	= "Foxy Layout",
				Style	= Application.Current.TryFindResource("MainMenuItem") as Style
			};

			existingMenuItem.Items.Add(newMenuItem);
			FoxyLog($"\tAttached Tools -> Foxy Layout menu item...");

			newMenuItem.Click				+= OnMenuItemClick;
			Globals.ActiveWorkspaceChanged	+= OnActiveWorkspaceChanged;
		}

		protected override void OnWindowDestroyed(Window window)
        {
			FoxyLog($"FoxyLayoutAddOn.OnWindowDestroyed() called: {window}", LogEventLevel.Debug);
			lock (FoxyWindows)
				if (FoxyWindows.SelectMany(kvp => kvp.Value).FirstOrDefault(fw => fw.Window == window) is FoxyWindow foxyWindow)
				{
					if (!Globals.IsApplicationExiting && !(window is ControlCenter))
						foxyWindow.ResizeWithout();
					foreach (KeyValuePair<string, List<FoxyWindow>> kvp in FoxyWindows)
					{
						if (kvp.Value.Contains(foxyWindow))
						{
							FoxyWindows[kvp.Key].Remove(foxyWindow);
							FoxyLog($"\tRemoved window '{ foxyWindow.Window }'");
						}
					}
				}

			if (window is ControlCenter)
            {
				lock (FoxyScreens)
					FoxyScreens.Clear();
				lock (FoxyWindows)
					FoxyWindows.Clear();
				lock (unsortedFoxyWindows)
					unsortedFoxyWindows.Clear();
				FoxyLog($"\tCleared all screens and windows");
				Serilog.Log.CloseAndFlush();

				if (newMenuItem != null)
                {
					if (existingMenuItem != null && existingMenuItem.Items.Contains(newMenuItem))
						existingMenuItem.Items.Remove(newMenuItem);

					newMenuItem.Click -= OnMenuItemClick;
					newMenuItem = null;
				}
				controlCenter = null;
				Globals.ActiveWorkspaceChanged -= OnActiveWorkspaceChanged;
			}
		}

		internal static List<FoxyScreen>						FoxyScreens			{ get; set; }
		internal static Dictionary<string, List<FoxyWindow>>	FoxyWindows			{ get; set; }
		internal static Dictionary<string, XElement>			FoxyRestore			{ get; set; }
	}
}
