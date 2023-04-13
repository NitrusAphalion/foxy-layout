using NinjaTrader.Core;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Serialization;

namespace FoxyLayout
{
    internal static class FoxyUtilities
    {
        private static readonly double  epsilon = 5; // This needs to be the distance between where cursor switches from updown to diagonal vs the edge of the window
        private static bool             loggerCreated;
        private static Options          options;

        public static int ApproxCompare(double double1, double double2)
        {
            if (double1 == double.MaxValue) return double2 == double.MaxValue ? 0 : 1;
            if (double2 == double.MaxValue) return double1 == double.MaxValue ? 0 : -1;
            if (double1 == double.MinValue) return double2 == double.MinValue ? 0 : -1;
            if (double2 == double.MinValue) return double1 == double.MinValue ? 0 : 1;
            if (double1 > double2 + epsilon) return 1;
            if (double1 < double2 - epsilon) return -1;
            return 0;
        }

        public static List<SharedBorder> FindSameBorders(Rect bounds, Rect bounds2)
        {
            List<SharedBorder> sharedBorders = new List<SharedBorder>();
            if (ApproxCompare(bounds.Top, bounds2.Top) == 0)
                sharedBorders.Add(SharedBorder.TopSame);
            if (ApproxCompare(bounds.Bottom, bounds2.Bottom) == 0)
                sharedBorders.Add(SharedBorder.BottomSame);
            if (ApproxCompare(bounds.Left, bounds2.Left) == 0)
                sharedBorders.Add(SharedBorder.LeftSame);
            if (ApproxCompare(bounds.Right, bounds2.Right) == 0)
                sharedBorders.Add(SharedBorder.RightSame);
            return sharedBorders;
        }

        public static SharedBorder FindSharedBorder(Rect bounds, Rect bounds2)
        {
            if (ApproxCompare(bounds.Top, bounds2.Bottom) == 0)
                return SharedBorder.Top;
            if (ApproxCompare(bounds.Bottom, bounds2.Top) == 0)
                return SharedBorder.Bottom;
            if (ApproxCompare(bounds.Left, bounds2.Right) == 0)
                return SharedBorder.Left;
            if (ApproxCompare(bounds.Right, bounds2.Left) == 0)
                return SharedBorder.Right;
            return SharedBorder.None;
        }

        public static void FoxyLog(string message, LogEventLevel logLevel = LogEventLevel.Verbose)
        {
            if (!loggerCreated)
            {
                string  logPath     = Path.Combine(Globals.UserDataDir, "log");
                string  logDate     = Globals.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
                int     logCount    = 0;

                foreach (string filePath in Directory.GetFiles(logPath, $"*log.{logDate}.*"))
                {
                    string[] split = filePath.Split('.');
                    if (int.TryParse(split[split.Length - 2], out int count))
                        logCount = count > logCount ? count : logCount;
                }

                Log.Logger      = new LoggerConfiguration().MinimumLevel.ControlledBy(LoggingSwitch).WriteTo.Console().WriteTo.File(Path.Combine(FoxyPath, $"foxylayout.{logDate}.{logCount.ToString().PadLeft(5, '0')}.txt")).CreateLogger();
                loggerCreated   = true;
            }

            Log.Write(logLevel, message);
        }

        public static DashStyle GetDashStyle(DashStyleHelper dashStyleHelper)
        {
            switch (dashStyleHelper)
            {
                case DashStyleHelper.Dash: return DashStyles.Dash;
                case DashStyleHelper.DashDot: return DashStyles.DashDot;
                case DashStyleHelper.DashDotDot: return DashStyles.DashDotDot;
                case DashStyleHelper.Dot: return DashStyles.Dot;
                case DashStyleHelper.Solid: return DashStyles.Solid;
            }
            return DashStyles.Solid;
        }

        public static ResizeQuadrant GetResizeQuadrant(Rect windowBounds, Rect screenBounds)
        {
            Dictionary<ResizeQuadrant, Rect> screenQuadrants = new Dictionary<ResizeQuadrant, Rect>();
            screenQuadrants.Add(ResizeQuadrant.LeftTop,     new Rect(screenBounds.Left, screenBounds.Top, screenBounds.Width / 2, screenBounds.Height / 2));
            screenQuadrants.Add(ResizeQuadrant.LeftBottom,  new Rect(screenBounds.Left, screenBounds.Top + screenBounds.Height / 2, screenBounds.Width / 2, screenBounds.Height / 2));
            screenQuadrants.Add(ResizeQuadrant.RightTop,    new Rect(screenBounds.Left + screenBounds.Width / 2, screenBounds.Top, screenBounds.Width / 2, screenBounds.Height / 2));
            screenQuadrants.Add(ResizeQuadrant.RightBottom, new Rect(screenBounds.Left + screenBounds.Width / 2, screenBounds.Top + screenBounds.Height / 2, screenBounds.Width / 2, screenBounds.Height / 2));

            foreach (KeyValuePair<ResizeQuadrant, Rect> kvp in screenQuadrants)
                kvp.Value.Intersect(windowBounds);

            return screenQuadrants.OrderByDescending(kvp => kvp.Value.Width * kvp.Value.Height).First().Key;
        }

        public static void InvokeIfNecessary(Dispatcher dispatcher, Action action)
        {
            if (Thread.CurrentThread == dispatcher.Thread)
                action();
            else
                dispatcher.Invoke(action);
        }

        public static bool PointInBounds(double x, double y, Rect bounds)
        {
            if (ApproxCompare(y, bounds.Top) >= 0
                && ApproxCompare(y, bounds.Top + bounds.Height) <= 0
                && ApproxCompare(x, bounds.Left) >= 0
                && ApproxCompare(x, bounds.Left + bounds.Width) <= 0)
                return true;
            return false;
        }

        public static readonly double SecondsToChangeLevel = 1.4; // * 1000 shuld be divisible by tick interval on timers (currently 200ms)
        public static Options Options
        {
            get
            {
                if (options == null)
                {
                    string filePath = Path.Combine(FoxyPath, "foxylayoutoptions.xml");
                    if (File.Exists(filePath))
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof(Options));

                        using (FileStream fileStream = File.OpenRead(filePath))
                            options = serializer.Deserialize(fileStream) as Options;
                    }
                    else
                        options = new Options();
                }
                return options;
            }
            set
            {
                if (options == value)
                    return;

                options = value;
                XmlSerializer serializer = new XmlSerializer(typeof(Options));

                using (FileStream fileStream = File.Create(Path.Combine(FoxyPath, "foxylayoutoptions.xml")))
                    serializer.Serialize(fileStream, options);

                LoggingSwitch.MinimumLevel = Options.LogLevel;
            }
        }
        public static LoggingLevelSwitch    LoggingSwitch { get; set; } = new LoggingLevelSwitch(Options.LogLevel);
        public static string FoxyPath
        {
            get
            {
                string foxyPath = Path.Combine(Globals.UserDataDir, "foxylayout");
                if (!Directory.Exists(foxyPath))
                    Directory.CreateDirectory(foxyPath);
                return foxyPath;
            }
        }
    }
}
